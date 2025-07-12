// <copyright file="WebhookService.cs" company="slskd Team">
//     Copyright (c) slskd Team. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see https://www.gnu.org/licenses/.
// </copyright>

using Microsoft.Extensions.Options;

namespace slskd.Integrations.Webhooks;

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Serilog;
using slskd.Events;

public class WebhookService
{
    public WebhookService(
        EventBus eventBus,
        IOptionsMonitor<Options> optionsMonitor,
        IHttpClientFactory httpClientFactory)
    {
        Events = eventBus;
        OptionsMonitor = optionsMonitor;
        HttpClientFactory = httpClientFactory;

        var json = new JsonSerializerOptions();
        json.Converters.Add(new IPAddressConverter());
        json.Converters.Add(new JsonStringEnumConverter());
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.DefaultIgnoreCondition = JsonIgnoreCondition.Never; // include properties with null values
        json.Encoder = JavaScriptEncoder.Default; // IMPORTANT! don't change this to 'UnsafeRelaxedJsonEscaping'; as the name implies, it's unsafe!

        JsonSerializerOptions = json;

        Events.Subscribe<Event>(nameof(WebhookService), HandleEvent);

        Log.Debug("{Service} initialized", nameof(WebhookService));
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<WebhookService>();
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private EventBus Events { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private JsonSerializerOptions JsonSerializerOptions { get; }

    private async Task HandleEvent(Event data)
    {
        await Task.Yield();

        Log.Debug("Handling event {Event}", data);

        bool EqualsThisEvent(string type) => type.Equals(data.Type.ToString(), StringComparison.OrdinalIgnoreCase);
        bool EqualsLiterallyAnyEvent(string type) => type.Equals(EventType.Any.ToString(), StringComparison.OrdinalIgnoreCase);

        var options = OptionsMonitor.CurrentValue;
        var webhooksTriggeredByThisEventType = options.Integration.Webhooks
            .Where(kvp => kvp.Value.On.Any(EqualsThisEvent) || kvp.Value.On.Any(EqualsLiterallyAnyEvent));

        foreach (var webhook in webhooksTriggeredByThisEventType)
        {
            _ = Task.Run(async () =>
            {
                var call = webhook.Value.Call;

                using var http = call.IgnoreCertificateErrors
                    ? HttpClientFactory.CreateClient(Constants.IgnoreCertificateErrors)
                    : HttpClientFactory.CreateClient();

                http.Timeout = TimeSpan.FromMilliseconds(webhook.Value.Timeout);

                foreach (var header in call.Headers)
                {
                    http.DefaultRequestHeaders.TryAddWithoutValidation(header.Name, header.Value);
                }

                var content = new StringContent(
                    content: JsonSerializer.Serialize(
                        value: data,
                        inputType: data.GetType(), // if omitted object is serialized as EventType, losing everything else
                        options: JsonSerializerOptions),
                    encoding: Encoding.UTF8,
                    mediaType: "application/json");

                HttpStatusCode? statusCode = null;

                try
                {
                    Log.Debug("Calling webhook '{Name}': {Url}", webhook.Key, webhook.Value.Call.Url);

                    var sw = Stopwatch.StartNew();

                    await Retry.Do(
                        task: async () =>
                        {
                            using var response = await http.PostAsync(call.Url, content);
                            response.EnsureSuccessStatusCode();
                            statusCode = response.StatusCode;
                        },
                        isRetryable: (attempts, ex) => true, // retry everything
                        onFailure: (attempts, ex) =>
                        {
                            if (webhook.Value.Retry.Attempts > 1)
                            {
                                Log.Warning(ex, "Failed attempt #{Attempts} to send webhook '{Name}' for event type {Event}: {Message}", attempts, webhook.Key, data.Type, ex.Message);
                            }
                        },
                        maxAttempts: webhook.Value.Retry.Attempts,
                        maxDelayInMilliseconds: 30000);

                    sw.Stop();

                    Log.Debug("Webhook '{Name}' called successfully in {Duration}ms; status code: {StatusCode}", webhook.Key, sw.ElapsedMilliseconds, statusCode);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to call webhook '{Name}' for event type {Event} after exhausting retries: {Message}", webhook.Key, data.Type, ex.Message);
                }
            });
        }
    }
}