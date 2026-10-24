// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Sql;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class SqlImportService : ISqlImportService
    {
        private static readonly Regex DangerousPattern = new(@"\b(DROP\s+(DATABASE|TABLE)|TRUNCATE\s+TABLE|CREATE\s+USER|DROP\s+USER|GRANT|REVOKE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly MySqlOptions _options;

        // Initialize the sql import service dependencies and state.
        public SqlImportService(MySqlOptions options)
        {
            _options = options;
        }

        // Handle analyze async within the sql import service workflow.
        public async Task<SqlImportAnalysis> AnalyzeAsync(
            string token,
            string originalFileName,
            string sqlFilePath,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            FileInfo info = new(sqlFilePath);
            if (!info.Exists)
            {
                throw new FileNotFoundException("The uploaded SQL file was not found.", sqlFilePath);
            }

            progress?.Report(new OperationProgressUpdate(
                "Hashing SQL file",
                "Calculating SHA-256 while reading the uploaded SQL file.",
                0,
                info.Length,
                "bytes"));

            string hash;
            await using (FileStream stream = new(
                sqlFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[64 * 1024];
                long hashed = 0;
                long nextReport = 1024 * 1024;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    incrementalHash.AppendData(buffer, 0, read);
                    hashed += read;
                    if (hashed >= nextReport || hashed == info.Length)
                    {
                        progress?.Report(new OperationProgressUpdate(
                            "Hashing SQL file",
                            "Hashed " + FormatBytes(hashed) + " of " + FormatBytes(info.Length) + ".",
                            hashed,
                            info.Length,
                            "bytes"));
                        nextReport = hashed + (1024 * 1024);
                    }
                }

                hash = Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
            }

            progress?.Report(new OperationProgressUpdate(
                "Scanning SQL statements",
                "Checking statements for destructive or privilege-related operations."));

            int statements = 0;
            bool destructive = false;
            List<string> warnings = new();
            using (StreamReader reader = new(sqlFilePath, Encoding.UTF8, true, 64 * 1024))
            {
                char[] buffer = new char[64 * 1024];
                int read;
                long charactersScanned = 0;
                long nextScanReport = 1024 * 1024;
                StringBuilder scanWindow = new();
                while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    charactersScanned += read;
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == ';')
                        {
                            statements++;
                        }
                    }

                    scanWindow.Append(buffer, 0, read);
                    if (scanWindow.Length > 256 * 1024)
                    {
                        scanWindow.Remove(0, scanWindow.Length - (128 * 1024));
                    }

                    if (!destructive && DangerousPattern.IsMatch(scanWindow.ToString()))
                    {
                        destructive = true;
                    }

                    if (charactersScanned >= nextScanReport)
                    {
                        progress?.Report(new OperationProgressUpdate(
                            "Scanning SQL statements",
                            charactersScanned.ToString("N0", CultureInfo.InvariantCulture) + " characters scanned; approximately " +
                            statements.ToString("N0", CultureInfo.InvariantCulture) + " statements found."));
                        nextScanReport = charactersScanned + (1024 * 1024);
                    }
                }
            }

            progress?.Report(new OperationProgressUpdate(
                "SQL analysis complete",
                statements.ToString("N0", CultureInfo.InvariantCulture) + " statements found; SHA-256 and safety scan are ready."));

            if (destructive)
            {
                warnings.Add("The file contains destructive or privilege-related statements. Review the target database before execution.");
            }

            if (info.Length > 100L * 1024L * 1024L)
            {
                warnings.Add("This is a large import. Execution can take several minutes and should be backed up first.");
            }

            return new SqlImportAnalysis
            {
                Token = token,
                OriginalFileName = originalFileName,
                FileSize = info.Length,
                Sha256 = hash,
                ApproximateStatementCount = statements,
                ContainsDestructiveStatements = destructive,
                Warnings = warnings
            };
        }

        // Execute  using schema-aware database operations.
        public async Task<SqlImportResult> ExecuteAsync(
            ConnectionProfile profile,
            string database,
            string sqlFilePath,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            FileInfo sqlInfo = new(sqlFilePath);
            progress?.Report(new OperationProgressUpdate(
                "Preparing database client",
                "Creating the temporary client configuration for " + database + "."));

            using MySqlClientDefaultsFile defaultsFile = await MySqlClientDefaultsFile.CreateAsync(profile, cancellationToken);

            ProcessStartInfo startInfo = new()
            {
                FileName = _options.ImportClientPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--defaults-extra-file=" + defaultsFile.Path);
            startInfo.ArgumentList.Add("--database=" + database);
            startInfo.ArgumentList.Add("--binary-mode");
            startInfo.ArgumentList.Add("--show-warnings");

            Stopwatch stopwatch = Stopwatch.StartNew();
            using Process process = new()
            {
                StartInfo = startInfo
            };
            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The database command-line client could not be started. Configure MySql:ImportClientPath in appsettings.json.", exception);
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            progress?.Report(new OperationProgressUpdate(
                "Streaming SQL to database",
                "Sending " + FormatBytes(sqlInfo.Length) + " to the MySQL/MariaDB client.",
                0,
                sqlInfo.Length,
                "bytes"));

            await using (FileStream sqlStream = new(
                sqlFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[64 * 1024];
                long streamed = 0;
                long nextReport = 1024 * 1024;
                int read;
                while ((read = await sqlStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await process.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    streamed += read;
                    if (streamed >= nextReport || streamed == sqlInfo.Length)
                    {
                        progress?.Report(new OperationProgressUpdate(
                            "Streaming SQL to database",
                            "Sent " + FormatBytes(streamed) + " of " + FormatBytes(sqlInfo.Length) + ".",
                            streamed,
                            sqlInfo.Length,
                            "bytes"));
                        nextReport = streamed + (1024 * 1024);
                    }
                }

                await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            }

            process.StandardInput.Close();
            progress?.Report(new OperationProgressUpdate(
                "Waiting for database execution",
                "All SQL bytes were sent. Waiting for the MySQL/MariaDB client to finish applying them."));

            await process.WaitForExitAsync(cancellationToken);
            string output = await outputTask;
            string error = await errorTask;
            stopwatch.Stop();

            progress?.Report(new OperationProgressUpdate(
                "Database execution finished",
                process.ExitCode == 0
                    ? "The MySQL/MariaDB client completed successfully."
                    : "The MySQL/MariaDB client returned exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "."));

            return new SqlImportResult
            {
                Succeeded = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = output,
                StandardError = error,
                DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        // Format bytes using schema-aware database operations.
        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d * 1024d)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024d * 1024d)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024d).ToString("0.##", CultureInfo.InvariantCulture) + " KB";
            }

            return bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
        }
    }
}
