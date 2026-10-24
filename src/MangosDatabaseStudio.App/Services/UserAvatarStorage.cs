// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class UserAvatarStorage : IUserAvatarStorage
    {
        private const long MaximumBytes = 3L * 1024L * 1024L;
        private readonly string _directory;

        // Initialize the user avatar storage dependencies and state.
        public UserAvatarStorage(IWebHostEnvironment environment)
        {
            _directory = Path.Combine(environment.ContentRootPath, "App_Data", "avatars");
            Directory.CreateDirectory(_directory);
        }

        // Persist  according to the available model or service contract.
        public async Task<string> SaveAsync(Guid userId, IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                throw new InvalidOperationException("Choose an image to upload.");
            }

            if (file.Length > MaximumBytes)
            {
                throw new InvalidOperationException("Avatar images cannot exceed 3 MB.");
            }

            string temporary = Path.Combine(_directory, userId.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".upload");
            try
            {
                await using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await file.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                byte[] header = new byte[16];
                int read;
                await using (FileStream input = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    read = await input.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
                }

                (string Extension, string ContentType)? format = DetectFormat(header, read);
                if (!format.HasValue)
                {
                    throw new InvalidOperationException("Only PNG, JPEG, GIF, and WebP avatar images are supported.");
                }

                string fileName = userId.ToString("N") + "-" + Guid.NewGuid().ToString("N") + format.Value.Extension;
                string destination = Path.Combine(_directory, fileName);
                File.Move(temporary, destination, true);
                return fileName;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        // Handle open within the user avatar storage workflow.
        public AvatarFile? Open(string fileName)
        {
            string safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, fileName, StringComparison.Ordinal))
            {
                return null;
            }

            string path = Path.Combine(_directory, safeName);
            if (!File.Exists(path))
            {
                return null;
            }

            string contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
            return new AvatarFile(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), contentType);
        }

        // Handle delete within the user avatar storage workflow.
        public void Delete(string? fileName)
        {
            string safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, fileName, StringComparison.Ordinal))
            {
                return;
            }

            string path = Path.Combine(_directory, safeName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Avatar cleanup is best-effort and must not roll back a completed profile or user update.
            }
            catch (UnauthorizedAccessException)
            {
                // Avatar cleanup is best-effort and must not roll back a completed profile or user update.
            }
        }

        // Detect format according to the available model or service contract.
        private static (string Extension, string ContentType)? DetectFormat(byte[] header, int length)
        {
            if (length >= 8 && header.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                return (".png", "image/png");
            }

            if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return (".jpg", "image/jpeg");
            }

            if (length >= 6)
            {
                string gif = System.Text.Encoding.ASCII.GetString(header, 0, 6);
                if (string.Equals(gif, "GIF87a", StringComparison.Ordinal) || string.Equals(gif, "GIF89a", StringComparison.Ordinal))
                {
                    return (".gif", "image/gif");
                }
            }

            if (length >= 12 &&
                System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" &&
                System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP")
            {
                return (".webp", "image/webp");
            }

            return null;
        }
    }
}
