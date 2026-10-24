// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    internal sealed class SqlExportService : ISqlExportService
    {
        private static readonly Regex UnsafeFileNameCharacters = new("[^A-Za-z0-9._-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly MySqlOptions _options;

        // Initialize the sql export service dependencies and state.
        public SqlExportService(MySqlOptions options)
        {
            _options = options;
        }

        // Export  using schema-aware database operations.
        public async Task<SqlExportResult> ExportAsync(
            ConnectionProfile profile,
            SqlExportRequest request,
            string outputDirectory,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            Directory.CreateDirectory(outputDirectory);
            TryRestrictDirectoryPermissions(outputDirectory);
            RemoveStaleExports(outputDirectory);

            DateTimeOffset generatedAtUtc = DateTimeOffset.UtcNow;
            string token = Guid.NewGuid().ToString("N");
            string bodyPath = Path.Combine(outputDirectory, token + ".body.sql");
            string sqlPath = Path.Combine(outputDirectory, token + ".sql");
            string finalPath = request.CompressOutput ? sqlPath + ".gz" : sqlPath;
            string downloadFileName = BuildDownloadFileName(request.Database, generatedAtUtc, request.CompressOutput);

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                progress?.Report(new OperationProgressUpdate(
                    "Preparing SQL export",
                    "Preparing the database client and temporary export files for " + request.Database + "."));
                using MySqlClientDefaultsFile defaultsFile = await MySqlClientDefaultsFile.CreateAsync(profile, cancellationToken);
                await RunDumpClientAsync(defaultsFile.Path, bodyPath, request, progress, cancellationToken);
                await WriteFinalFileAsync(bodyPath, finalPath, request, generatedAtUtc, progress, cancellationToken);
                MySqlClientDefaultsFile.TryRestrictFilePermissions(finalPath);
                stopwatch.Stop();

                FileInfo output = new(finalPath);
                progress?.Report(new OperationProgressUpdate(
                    "Export ready",
                    FormatBytes(output.Length) + " is ready for download."));
                return new SqlExportResult
                {
                    FilePath = finalPath,
                    DownloadFileName = downloadFileName,
                    ContentType = request.CompressOutput ? "application/gzip" : "application/sql",
                    FileSize = output.Length,
                    DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    GeneratedAtUtc = generatedAtUtc
                };
            }
            catch
            {
                TryDelete(finalPath);
                throw;
            }
            finally
            {
                TryDelete(bodyPath);
                if (request.CompressOutput)
                {
                    TryDelete(sqlPath);
                }
            }
        }

        // Run dump client using schema-aware database operations.
        private async Task RunDumpClientAsync(
            string defaultsPath,
            string bodyPath,
            SqlExportRequest request,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new OperationProgressUpdate(
                "Dumping database objects",
                "The database export client is reading " + request.Tables.Count.ToString("N0", CultureInfo.InvariantCulture) + " selected table(s)."));
            ProcessStartInfo startInfo = new()
            {
                FileName = _options.ExportClientPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--defaults-extra-file=" + defaultsPath);
            startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
            startInfo.ArgumentList.Add("--hex-blob");
            startInfo.ArgumentList.Add("--quote-names");
            startInfo.ArgumentList.Add("--skip-comments");
            startInfo.ArgumentList.Add("--no-tablespaces");
            startInfo.ArgumentList.Add("--quick");
            startInfo.ArgumentList.Add("--skip-lock-tables");
            startInfo.ArgumentList.Add(request.IncludeDropStatements ? "--add-drop-table" : "--skip-add-drop-table");
            startInfo.ArgumentList.Add(request.IncludeTriggers ? "--triggers" : "--skip-triggers");
            startInfo.ArgumentList.Add(request.IncludeRoutines ? "--routines" : "--skip-routines");
            startInfo.ArgumentList.Add(request.IncludeEvents ? "--events" : "--skip-events");

            if (request.UseSingleTransaction)
            {
                startInfo.ArgumentList.Add("--single-transaction");
            }

            switch (request.Content)
            {
                case SqlExportContent.StructureOnly:
                    startInfo.ArgumentList.Add("--no-data");
                    break;
                case SqlExportContent.DataOnly:
                    startInfo.ArgumentList.Add("--no-create-info");
                    break;
            }

            startInfo.ArgumentList.Add("--result-file=" + bodyPath);
            startInfo.ArgumentList.Add(request.Database);
            foreach (string table in request.Tables)
            {
                startInfo.ArgumentList.Add(table);
            }

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
                throw new InvalidOperationException(
                    "The database export client could not be started. Configure MySql:ExportClientPath in appsettings.json.",
                    exception);
            }

            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(standardOutputTask, standardErrorTask);
                throw;
            }

            string standardOutput = await standardOutputTask;
            string standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                string detail = FirstNonEmpty(standardError, standardOutput, "The export client returned an unspecified error.");
                throw new InvalidOperationException("Database export failed: " + Limit(detail.Trim(), 4000));
            }

            if (!File.Exists(bodyPath))
            {
                throw new InvalidOperationException("Database export finished without creating an SQL file.");
            }

            long dumpedBytes = new FileInfo(bodyPath).Length;
            progress?.Report(new OperationProgressUpdate(
                "Database dump complete",
                "The export client produced " + FormatBytes(dumpedBytes) + " of SQL data."));
        }

        // Write final file using schema-aware database operations.
        private static async Task WriteFinalFileAsync(
            string bodyPath,
            string finalPath,
            SqlExportRequest request,
            DateTimeOffset generatedAtUtc,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            byte[] header = new UTF8Encoding(false).GetBytes(BuildHeader(request, generatedAtUtc));
            await using FileStream file = new(
                finalPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (request.CompressOutput)
            {
                await using GZipStream gzip = new(file, CompressionLevel.Optimal, leaveOpen: true);
                await gzip.WriteAsync(header.AsMemory(0, header.Length), cancellationToken);
                await using FileStream body = OpenBody(bodyPath);
                await CopyBodyAsync(body, gzip, "Compressing SQL export", progress, cancellationToken);
                await gzip.FlushAsync(cancellationToken);
            }
            else
            {
                await file.WriteAsync(header.AsMemory(0, header.Length), cancellationToken);
                await using FileStream body = OpenBody(bodyPath);
                await CopyBodyAsync(body, file, "Assembling SQL export", progress, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
        }

        // Handle copy body async within the sql export service workflow.
        private static async Task CopyBodyAsync(
            FileStream body,
            Stream destination,
            string stage,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            long total = body.Length;
            long copied = 0;
            long nextReport = 1024 * 1024;
            byte[] buffer = new byte[64 * 1024];
            progress?.Report(new OperationProgressUpdate(stage, "Preparing " + FormatBytes(total) + " of SQL data.", 0, total, "bytes"));

            int read;
            while ((read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (copied >= nextReport || copied == total)
                {
                    progress?.Report(new OperationProgressUpdate(
                        stage,
                        "Processed " + FormatBytes(copied) + " of " + FormatBytes(total) + ".",
                        copied,
                        total,
                        "bytes"));
                    nextReport = copied + (1024 * 1024);
                }
            }
        }

        // Open body using schema-aware database operations.
        private static FileStream OpenBody(string bodyPath)
        {
            return new FileStream(
                bodyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        // Build header using schema-aware database operations.
        private static string BuildHeader(SqlExportRequest request, DateTimeOffset generatedAtUtc)
        {
            StringBuilder builder = new();
            builder.AppendLine("-- --------------------------------------------------------");
            builder.AppendLine("-- Mangos Database Studio SQL Export");
            builder.AppendLine("-- Generated at: " + generatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
            builder.AppendLine("-- Database: " + SanitizeComment(request.Database));
            builder.AppendLine("-- Content: " + GetContentLabel(request.Content));
            builder.AppendLine("-- Tables: " + (request.AllTablesSelected
                ? "all available tables (" + request.Tables.Count.ToString(CultureInfo.InvariantCulture) + ")"
                : request.Tables.Count.ToString(CultureInfo.InvariantCulture) + " selected"));
            builder.AppendLine("-- Triggers: " + YesNo(request.IncludeTriggers));
            builder.AppendLine("-- Routines: " + YesNo(request.IncludeRoutines));
            builder.AppendLine("-- Events: " + YesNo(request.IncludeEvents));
            builder.AppendLine("-- --------------------------------------------------------");

            if (!request.AllTablesSelected)
            {
                foreach (string table in request.Tables.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine("-- Table: " + SanitizeComment(table));
                }

                builder.AppendLine("-- --------------------------------------------------------");
            }

            builder.AppendLine();
            return builder.ToString();
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

        // Read content label using schema-aware database operations.
        private static string GetContentLabel(SqlExportContent content)
        {
            return content switch
            {
                SqlExportContent.StructureOnly => "structure only",
                SqlExportContent.DataOnly => "data only",
                _ => "structure and data"
            };
        }

        // Build download file name using schema-aware database operations.
        private static string BuildDownloadFileName(string database, DateTimeOffset generatedAtUtc, bool compressed)
        {
            string safeDatabase = UnsafeFileNameCharacters.Replace(database.Trim(), "_").Trim('_');
            if (safeDatabase.Length == 0)
            {
                safeDatabase = "database";
            }

            string extension = compressed ? ".sql.gz" : ".sql";
            return "MangosDatabaseStudio-" + safeDatabase + "-" +
                   generatedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + extension;
        }

        // Validate request using schema-aware database operations.
        private static void ValidateRequest(SqlExportRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
            {
                throw new InvalidOperationException("Choose a database to export.");
            }

            if (!Enum.IsDefined(request.Content))
            {
                throw new InvalidOperationException("Choose a valid SQL export content type.");
            }

            MySqlIdentifier.Quote(request.Database);
            if (request.Tables == null || request.Tables.Count == 0)
            {
                throw new InvalidOperationException("Select at least one table to export.");
            }

            foreach (string table in request.Tables)
            {
                MySqlIdentifier.Quote(table);
            }
        }

        // Handle sanitize comment within the sql export service workflow.
        private static string SanitizeComment(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // Handle yes no within the sql export service workflow.
        private static string YesNo(bool value)
        {
            return value ? "included" : "not included";
        }

        // Handle first non empty within the sql export service workflow.
        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        // Handle limit within the sql export service workflow.
        private static string Limit(string value, int maximumLength)
        {
            return value.Length <= maximumLength ? value : value.AsSpan(0, maximumLength).ToString() + "…";
        }

        // Remove stale exports using schema-aware database operations.
        private static void RemoveStaleExports(string outputDirectory)
        {
            DateTime thresholdUtc = DateTime.UtcNow.AddHours(-24);
            try
            {
                IEnumerable<string> paths = Directory.EnumerateFiles(outputDirectory, "*.sql")
                    .Concat(Directory.EnumerateFiles(outputDirectory, "*.sql.gz"));

                foreach (string path in paths)
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < thresholdUtc)
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                        // A concurrent download or protected file is left untouched.
                    }
                }
            }
            catch
            {
                // Stale-file cleanup must never block a new export.
            }
        }

        // Attempt to restrict directory permissions using schema-aware database operations.
        private static void TryRestrictDirectoryPermissions(string path)
        {
            try
            {
                Directory.CreateDirectory(path);

                if (OperatingSystem.IsLinux() ||
                    OperatingSystem.IsMacOS() ||
                    OperatingSystem.IsFreeBSD())
                {
                    new DirectoryInfo(path).UnixFileMode =
                        UnixFileMode.UserRead |
                        UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute;
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Windows access is controlled by the application account and directory ACLs.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue when the host prevents changing Unix directory permissions.
            }
            catch (IOException)
            {
                // Continue when the file system does not support Unix mode changes.
            }
        }

        // Attempt to kill using schema-aware database operations.
        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Cancellation still propagates when the operating system refuses termination.
            }
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
                // Temporary export files are removed on a best-effort basis.
            }
        }
    }
}
