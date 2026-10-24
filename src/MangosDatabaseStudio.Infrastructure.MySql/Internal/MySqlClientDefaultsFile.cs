// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Database access helpers, identifier escaping, and type conversion internals.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.Infrastructure.MySql.Internal
{
    internal sealed class MySqlClientDefaultsFile : IDisposable
    {
        // Initialize the my sql client defaults file dependencies and state.
        private MySqlClientDefaultsFile(string path)
        {
            Path = path;
        }

        public string Path
        {
            get;
        }

        // Create  using schema-aware database operations.
        public static async Task<MySqlClientDefaultsFile> CreateAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mangos-database-studio-" + Guid.NewGuid().ToString("N") + ".cnf");

            StringBuilder builder = new();
            builder.AppendLine("[client]");
            builder.AppendLine("host=" + QuoteOption(profile.Host));
            builder.AppendLine("port=" + profile.Port.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("user=" + QuoteOption(profile.Username));
            builder.AppendLine("password=" + QuoteOption(profile.Password));
            builder.AppendLine("default-character-set=utf8mb4");

            try
            {
                await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken);
                TryRestrictFilePermissions(path);

                return new MySqlClientDefaultsFile(path);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        // Handle dispose within the my sql client defaults file workflow.
        public void Dispose()
        {
            TryDelete(Path);
        }

        // Attempt to delete using schema-aware database operations.
        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Temporary credentials are removed on a best-effort basis.
            }
        }

        // Escape and quote option using schema-aware database operations.
        private static string QuoteOption(string value)
        {
            string escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty);
            return "\"" + escaped + "\"";
        }

        // Attempt to restrict file permissions using schema-aware database operations.
        internal static void TryRestrictFilePermissions(string path)
        {
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Windows uses the application account and directory ACLs.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue when the host prevents changing the Unix file mode.
            }
            catch (IOException)
            {
                // Continue when the file system does not support Unix mode changes.
            }
        }
    }
}
