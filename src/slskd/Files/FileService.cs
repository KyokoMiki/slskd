﻿// <copyright file="FileService.cs" company="slskd Team">
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

namespace slskd.Files
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    ///     Manages files on disk.
    /// </summary>
    public class FileService : IFileService
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="FileService"/> class.
        /// </summary>
        /// <param name="optionsSnapshot"></param>
        public FileService(
            IOptionsSnapshot<Options> optionsSnapshot)
        {
            OptionsSnapshot = optionsSnapshot;
        }

        private IOptionsSnapshot<Options> OptionsSnapshot { get; }

        /// <summary>
        ///     Lists all of the directories in the specified <paramref name="parentDirectory"/>, optionally applying the
        ///     specified <paramref name="enumerationOptions"/>.
        /// </summary>
        /// <param name="parentDirectory">The directory from which to start the listing.</param>
        /// <param name="enumerationOptions">Optional enumeration options to apply.</param>
        /// <returns>The list of found directories.</returns>
        /// <exception cref="InvalidDirectoryException">
        ///     Thrown if the specified directory is not rooted in an allowed directory.
        /// </exception>
        public async Task<IEnumerable<DirectoryInfo>> ListDirectoriesAsync(string parentDirectory, EnumerationOptions enumerationOptions = null)
        {
            if (!IsPermissibleDirectory(parentDirectory, OptionsSnapshot.Value.Directories))
            {
                throw new InvalidDirectoryException($"The directory '{parentDirectory}' is not rooted in any of the allowed directories");
            }

            return await Task.Run(() => new DirectoryInfo(parentDirectory).GetDirectories("*", enumerationOptions));
        }

        /// <summary>
        ///     Lists all of the files in the specified <paramref name="parentDirectory"/>, optionally applying the specified <paramref name="enumerationOptions"/>.
        /// </summary>
        /// <param name="parentDirectory">An optional parent directory from which to begin searching.</param>
        /// <param name="enumerationOptions">Optional enumeration options to apply.</param>
        /// <returns>The list of found files.</returns>
        /// <exception cref="InvalidDirectoryException">
        ///     Thrown if the specified directory is not rooted in an allowed directory.
        /// </exception>
        public async Task<IEnumerable<FileInfo>> ListFilesAsync(string parentDirectory, EnumerationOptions enumerationOptions = null)
        {
            if (!IsPermissibleDirectory(parentDirectory, OptionsSnapshot.Value.Directories))
            {
                throw new InvalidDirectoryException($"The directory '{parentDirectory}' is not rooted in any of the applowed directories");
            }

            return await Task.Run(() => new DirectoryInfo(parentDirectory).GetFiles("*", enumerationOptions));
        }

        private bool IsPermissibleDirectory(string dir, Options.DirectoriesOptions options)
        {
            var allowedRoots = new[] { options.Downloads, options.Incomplete };
            return allowedRoots.Any(r => dir.StartsWith(r));
        }
    }
}