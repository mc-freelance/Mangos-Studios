// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Database access helpers, identifier escaping, and type conversion internals.

using System;
using System.Diagnostics;
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

        // Write an isolated, short-lived client configuration with matching TLS settings.
        public static async Task<MySqlClientDefaultsFile> CreateAsync(
            ConnectionProfile profile,
            string clientPath,
            CancellationToken cancellationToken)
        {
            // Do not guess the client dialect: the MySQL and MariaDB TLS flags differ.
            ConnectionTlsSecurity.ValidateProfile(profile);
            string clientDialect = await IdentifyClientDialectAsync(clientPath, cancellationToken);
            string mode = ConnectionTlsSecurity.NormalizeMode(profile.SslMode);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mangos-database-studio-" + Guid.NewGuid().ToString("N") + ".cnf");

            StringBuilder builder = new();
            builder.AppendLine("[client]");
            builder.AppendLine("host=" + QuoteOption(profile.Host));
            builder.AppendLine("port=" + profile.Port.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("user=" + QuoteOption(profile.Username));
            builder.AppendLine("password=" + QuoteOption(profile.Password));
            builder.AppendLine("default-character-set=utf8mb4");

            // Propagate the same transport policy to native import and export clients.
            // No client may silently downgrade a requested verified connection.
            if (clientDialect == "mysql")
            {
                string clientMode = mode == "VerifyFull" ? "VERIFY_IDENTITY" : mode.ToUpperInvariant();
                builder.AppendLine("ssl-mode=" + clientMode);
            }
            else
            {
                if (mode == "Disabled")
                {
                    builder.AppendLine("skip-ssl");
                }
                else if (mode == "VerifyFull" || mode == "VerifyCA")
                {
                    builder.AppendLine("ssl");
                    builder.AppendLine("ssl-verify-server-cert");
                }
                else if (mode == "Required")
                {
                    builder.AppendLine("ssl");
                }

                // Preferred follows the MariaDB client's own opportunistic TLS behavior.
            }

            if (mode != "Disabled" && !string.IsNullOrWhiteSpace(profile.SslCa))
            {
                builder.AppendLine("ssl-ca=" + QuoteOption(profile.SslCa.Trim()));
            }

            try
            {
                // Create the credentials file with owner-only permissions before writing secrets.
                FileStreamOptions fileOptions = new()
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                };

                if (!OperatingSystem.IsWindows())
                {
                    fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                await using (FileStream stream = new(path, fileOptions))
                await using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
                {
                    await writer.WriteAsync(builder.ToString().AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }

                TryRestrictFilePermissions(path);

                return new MySqlClientDefaultsFile(path);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        // Probe the client version rather than assuming symlinks named mysql are Oracle clients.
        // Unknown or unsupported client types cannot safely receive verified TLS options.
        private static async Task<string> IdentifyClientDialectAsync(string clientPath, CancellationToken cancellationToken)
        {
            ProcessStartInfo start = new()
            {
                FileName = clientPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--version");

            using Process process = new()
            {
                StartInfo = start
            };
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Could not identify the database client executable.");
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                string version = (await outputTask) + " " + (await errorTask);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("The database client version probe failed.");
                }

                if (version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
                {
                    return "mariadb";
                }

                if (version.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
                    || version.Contains("mysqldump", StringComparison.OrdinalIgnoreCase))
                {
                    return "mysql";
                }

                throw new InvalidOperationException(
                    "Unrecognized MySQL/MariaDB client. Use a supported mysql/mysqldump or mariadb/mariadb-dump executable.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException("Timed out identifying the database client TLS options.");
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
