// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.DatabaseUpdates;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.DatabaseUpdates;
using MangosDatabaseStudio.Domain.Sql;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class MangosDatabaseUpdateService : IMangosDatabaseUpdateService
    {
        private const int MaximumFilesToScan = 10000;
        private const int MaximumMetadataCharacters = 256 * 1024;
        private const int MaximumPatchesPerRun = 2000;

        private static readonly Regex SetValuePattern = new(
            @"(?im)^\s*SET\s+@(?<name>cOldVersion|cOldStructure|cOldContent|cNewVersion|cNewStructure|cNewContent|cNewDescription|cNewComment)\s*(?::=|=)\s*(?<value>'(?:''|\\.|[^'])*'|[^;\r\n]+)\s*;",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly ISqlImportService _imports;

        // Initialize the mangos database update service dependencies and state.
        public MangosDatabaseUpdateService(MySqlConnectionFactory connectionFactory, ISqlImportService imports)
        {
            _connectionFactory = connectionFactory;
            _imports = imports;
        }

        // Build plan using schema-aware database operations.
        public async Task<MangosDatabaseUpdatePlan> BuildPlanAsync(
            ConnectionProfile profile,
            string database,
            string sourceDirectory,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            MangosDatabaseUpdatePlan plan = new()
            {
                Database = database ?? string.Empty,
                SourceDirectory = sourceDirectory ?? string.Empty
            };

            List<string> warnings = new();
            List<string> errors = new();

            if (string.IsNullOrWhiteSpace(database))
            {
                errors.Add("Select a database before scanning for updates.");
                plan.Errors = errors;
                return plan;
            }

            progress?.Report(new OperationProgressUpdate(
                "Reading database version",
                "Reading db_version before scanning the configured update source."));
            try
            {
                plan.CurrentVersion = await ReadCurrentVersionAsync(profile, database, cancellationToken);
            }
            catch (MySqlException exception) when (exception.Number == 1146)
            {
                errors.Add("The selected database does not contain a db_version table.");
                plan.Errors = errors;
                return plan;
            }
            catch (Exception exception)
            {
                errors.Add("Unable to read db_version: " + exception.Message);
                plan.Errors = errors;
                return plan;
            }

            if (plan.CurrentVersion == null)
            {
                errors.Add("The db_version table does not contain version information.");
                plan.Errors = errors;
                return plan;
            }

            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                warnings.Add("No update source folder is configured for the active connection.");
                plan.Warnings = warnings;
                return plan;
            }

            string normalizedDirectory;
            try
            {
                normalizedDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourceDirectory.Trim()));
            }
            catch (Exception exception)
            {
                errors.Add("The configured update source path is invalid: " + exception.Message);
                plan.Errors = errors;
                return plan;
            }

            plan.SourceDirectory = normalizedDirectory;
            if (!Directory.Exists(normalizedDirectory))
            {
                errors.Add("The configured update source folder does not exist on the computer running Mangos Database Studio.");
                plan.Errors = errors;
                return plan;
            }

            progress?.Report(new OperationProgressUpdate(
                "Scanning update source",
                "Finding SQL patch files under " + normalizedDirectory + "."));
            List<string> sqlFiles;
            try
            {
                EnumerationOptions options = new()
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                sqlFiles = Directory
                    .EnumerateFiles(normalizedDirectory, "*", options)
                    .Where(path => path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    .Take(MaximumFilesToScan + 1)
                    .ToList();
            }
            catch (Exception exception)
            {
                errors.Add("The update source folder could not be scanned: " + exception.Message);
                plan.Errors = errors;
                return plan;
            }

            if (sqlFiles.Count > MaximumFilesToScan)
            {
                errors.Add("The update source contains more than " + MaximumFilesToScan + " SQL files. Configure a more specific update folder.");
                plan.Errors = errors;
                return plan;
            }

            plan.FilesDiscovered = sqlFiles.Count;
            progress?.Report(new OperationProgressUpdate(
                "Reading patch metadata",
                sqlFiles.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " SQL file(s) discovered.",
                0,
                sqlFiles.Count,
                "files"));
            List<MangosDatabaseUpdatePatch> patches = new(sqlFiles.Count);
            int metadataIndex = 0;
            foreach (string path in sqlFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                patches.Add(await ReadPatchMetadataAsync(normalizedDirectory, path, cancellationToken));
                metadataIndex++;
                if (metadataIndex == sqlFiles.Count || metadataIndex % 25 == 0)
                {
                    progress?.Report(new OperationProgressUpdate(
                        "Reading patch metadata",
                        metadataIndex.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " of " +
                        sqlFiles.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " patch files inspected.",
                        metadataIndex,
                        sqlFiles.Count,
                        "files"));
                }
            }

            progress?.Report(new OperationProgressUpdate(
                "Building update chain",
                "Matching patch old/new versions against the current db_version."));
            BuildChain(plan, patches, warnings, errors);
            plan.Warnings = warnings;
            plan.Errors = errors;
            progress?.Report(new OperationProgressUpdate(
                "Update plan ready",
                plan.PendingPatches.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " pending patch(es) are in the validated chain."));
            return plan;
        }

        // Apply  using schema-aware database operations.
        public async Task<MangosDatabaseUpdateBatchResult> ApplyAsync(
            ConnectionProfile profile,
            string database,
            string sourceDirectory,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            MangosDatabaseUpdatePlan plan = await BuildPlanAsync(profile, database, sourceDirectory, progress, cancellationToken);
            MangosDatabaseUpdateBatchResult batch = new()
            {
                Database = database,
                SourceDirectory = plan.SourceDirectory,
                StartingVersion = plan.CurrentVersion,
                FinalVersion = plan.CurrentVersion,
                PlannedPatchCount = plan.PendingPatches.Count
            };

            if (!plan.CanExecute)
            {
                batch.Succeeded = false;
                batch.Message = plan.Errors.Count > 0
                    ? string.Join(" ", plan.Errors)
                    : "No applicable database updates were found.";
                return batch;
            }

            if (plan.PendingPatches.Count > MaximumPatchesPerRun)
            {
                batch.Succeeded = false;
                batch.Message = "The update plan contains more than " + MaximumPatchesPerRun + " patches and was refused for safety.";
                return batch;
            }

            List<MangosDatabaseUpdatePatchResult> results = new();
            int patchIndex = 0;
            foreach (MangosDatabaseUpdatePatch patch in plan.PendingPatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                patchIndex++;
                progress?.Report(new OperationProgressUpdate(
                    "Applying database update",
                    "Patch " + patchIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " +
                    plan.PendingPatches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + patch.FileName,
                    patchIndex - 1,
                    plan.PendingPatches.Count,
                    "patches"));

                MangosDatabaseVersion? before;
                try
                {
                    before = await ReadCurrentVersionAsync(profile, database, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "Execution stopped because db_version could not be read before the patch: " + exception.Message
                    });
                    batch.PatchResults = results;
                    batch.Succeeded = false;
                    batch.Message = "Database updates stopped before " + patch.FileName + ".";
                    return batch;
                }

                if (before?.Equals(patch.ExpectedVersion) is not true)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "Execution stopped because db_version changed before this patch could run. Expected " +
                                  patch.ExpectedVersion.DisplayValue + ", found " + (before?.DisplayValue ?? "no version") + "."
                    });
                    batch.PatchResults = results;
                    batch.FinalVersion = before;
                    batch.Succeeded = false;
                    batch.Message = "The update chain stopped before " + patch.FileName + " because the database version no longer matched the plan.";
                    return batch;
                }

                SqlImportResult execution;
                try
                {
                    execution = await _imports.ExecuteAsync(profile, database, patch.FullPath, progress, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "The SQL client could not execute this patch: " + exception.Message
                    });
                    batch.PatchResults = results;
                    batch.FinalVersion = before;
                    batch.Succeeded = false;
                    batch.Message = "Database updates stopped at " + patch.FileName + ".";
                    return batch;
                }

                if (!execution.Succeeded)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "The MySQL/MariaDB client reported an error. No later patches were executed.",
                        StandardOutput = execution.StandardOutput,
                        StandardError = execution.StandardError,
                        DurationMilliseconds = execution.DurationMilliseconds
                    });

                    batch.PatchResults = results;

                    try
                    {
                        batch.FinalVersion = await ReadCurrentVersionAsync(profile, database, cancellationToken);
                    }
                    catch
                    {
                        batch.FinalVersion = before;
                    }

                    batch.Succeeded = false;
                    batch.Message = "Database updates stopped at " + patch.FileName + ".";

                    return batch;
                }

                progress?.Report(new OperationProgressUpdate(
                    "Verifying database version",
                    "Patch " + patch.FileName + " finished; checking that db_version advanced to " + patch.NewVersion.DisplayValue + ".",
                    patchIndex,
                    plan.PendingPatches.Count,
                    "patches"));
                MangosDatabaseVersion? after;
                try
                {
                    after = await ReadCurrentVersionAsync(profile, database, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "The SQL client completed, but db_version could not be verified afterward: " + exception.Message,
                        StandardOutput = execution.StandardOutput,
                        StandardError = execution.StandardError,
                        DurationMilliseconds = execution.DurationMilliseconds
                    });

                    batch.PatchResults = results;
                    batch.FinalVersion = before;
                    batch.Succeeded = false;
                    batch.Message = "Database updates stopped because version verification could not complete after " + patch.FileName + ".";

                    return batch;
                }

                if (after?.Equals(patch.NewVersion) is not true)
                {
                    results.Add(new MangosDatabaseUpdatePatchResult
                    {
                        FileName = patch.FileName,
                        RelativePath = patch.RelativePath,
                        ExpectedVersion = patch.ExpectedVersion,
                        NewVersion = patch.NewVersion,
                        Succeeded = false,
                        Message = "The SQL client completed, but db_version did not advance to the version declared by the patch. " +
                                  "Expected " + patch.NewVersion.DisplayValue + ", found " + (after?.DisplayValue ?? "no version") + ". No later patches were executed.",
                        StandardOutput = execution.StandardOutput,
                        StandardError = execution.StandardError,
                        DurationMilliseconds = execution.DurationMilliseconds
                    });

                    batch.PatchResults = results;
                    batch.FinalVersion = after;
                    batch.Succeeded = false;
                    batch.Message = "Database updates stopped because version verification failed after " + patch.FileName + ".";

                    return batch;
                }

                results.Add(new MangosDatabaseUpdatePatchResult
                {
                    FileName = patch.FileName,
                    RelativePath = patch.RelativePath,
                    ExpectedVersion = patch.ExpectedVersion,
                    NewVersion = patch.NewVersion,
                    Succeeded = true,
                    Message = "Patch applied and db_version verified.",
                    StandardOutput = execution.StandardOutput,
                    StandardError = execution.StandardError,
                    DurationMilliseconds = execution.DurationMilliseconds
                });

                batch.FinalVersion = after;
                progress?.Report(new OperationProgressUpdate(
                    "Patch verified",
                    patch.FileName + " applied successfully; db_version is now " + after.DisplayValue + ".",
                    patchIndex,
                    plan.PendingPatches.Count,
                    "patches"));
            }

            batch.PatchResults = results;
            batch.Succeeded = results.Count == plan.PendingPatches.Count && results.All(result => result.Succeeded);
            batch.Message = batch.Succeeded
                ? "Applied and verified " + results.Count + " database update" + (results.Count == 1 ? "." : "s.")
                : "The database update chain did not complete.";
            progress?.Report(new OperationProgressUpdate(
                "Database updates complete",
                batch.Message,
                results.Count,
                plan.PendingPatches.Count,
                "patches"));

            return batch;
        }

        // Read current version using schema-aware database operations.
        private async Task<MangosDatabaseVersion?> ReadCurrentVersionAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT `version`, `structure`, `content`, `description`
FROM `db_version`
ORDER BY `version` DESC, `structure` DESC, `content` DESC
LIMIT 1;";

            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new MangosDatabaseVersion
            {
                Version = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0)) ?? string.Empty,
                Structure = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1)) ?? string.Empty,
                Content = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2)) ?? string.Empty,
                Description = reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3)) ?? string.Empty
            };
        }

        // Read patch metadata using schema-aware database operations.
        private static async Task<MangosDatabaseUpdatePatch> ReadPatchMetadataAsync(
            string rootDirectory,
            string path,
            CancellationToken cancellationToken)
        {
            FileInfo info = new(path);
            MangosDatabaseUpdatePatch patch = new()
            {
                FileName = info.Name,
                FullPath = info.FullName,
                RelativePath = Path.GetRelativePath(rootDirectory, info.FullName),
                FileSize = info.Exists ? info.Length : 0,
                Status = MangosDatabaseUpdatePatchStatus.Unrecognized
            };

            string text;
            try
            {
                char[] buffer = new char[MaximumMetadataCharacters];
                await using FileStream stream = new(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using StreamReader reader = new(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: false);
                int read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                text = new string(buffer, 0, read);
            }
            catch (Exception exception)
            {
                patch.Status = MangosDatabaseUpdatePatchStatus.Invalid;
                patch.StatusDetail = "Unable to read file: " + exception.Message;
                return patch;
            }

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in SetValuePattern.Matches(text))
            {
                values[match.Groups["name"].Value] = DecodeSqlValue(match.Groups["value"].Value);
            }

            string[] required =
            {
                "cOldVersion",
                "cOldStructure",
                "cOldContent",
                "cNewVersion",
                "cNewStructure",
                "cNewContent"
            };

            if (required.Any(name => !values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value)))
            {
                patch.StatusDetail = "MaNGOS Expected/New version metadata was not found in the first 256 KB of the file.";
                return patch;
            }

            patch.ExpectedVersion = new MangosDatabaseVersion
            {
                Version = values["cOldVersion"],
                Structure = values["cOldStructure"],
                Content = values["cOldContent"]
            };

            patch.NewVersion = new MangosDatabaseVersion
            {
                Version = values["cNewVersion"],
                Structure = values["cNewStructure"],
                Content = values["cNewContent"],
                Description = values.TryGetValue("cNewDescription", out string? description) ? description : string.Empty
            };

            patch.Description = patch.NewVersion.Description;
            patch.Comment = values.TryGetValue("cNewComment", out string? comment) ? comment : string.Empty;
            patch.IsRecognized = true;
            patch.Status = MangosDatabaseUpdatePatchStatus.Unrelated;
            patch.StatusDetail = "Recognized MaNGOS update metadata.";

            return patch;
        }

        // Handle decode sql value within the mangos database update service workflow.
        private static string DecodeSqlValue(string raw)
        {
            string value = raw.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1];
                value = value.Replace("''", "'");
                value = value.Replace("\\'", "'");
                value = value.Replace("\\\\", "\\");
            }

            return value.Trim();
        }

        // Build chain using schema-aware database operations.
        private static void BuildChain(
            MangosDatabaseUpdatePlan plan,
            List<MangosDatabaseUpdatePatch> patches,
            List<string> warnings,
            List<string> errors)
        {
            MangosDatabaseVersion current = plan.CurrentVersion!;
            List<MangosDatabaseUpdatePatch> recognized = patches.Where(patch => patch.IsRecognized).ToList();
            plan.RecognizedFiles = recognized.Count;
            plan.UnrecognizedFiles = patches.Count(patch => !patch.IsRecognized);

            HashSet<MangosDatabaseUpdatePatch> applied = new();
            MangosDatabaseVersion backwardCursor = current;
            HashSet<MangosDatabaseVersion> backwardVersions = new()
            {
                backwardCursor
            };

            while (true)
            {
                List<MangosDatabaseUpdatePatch> previous = recognized
                    .Where(patch => !applied.Contains(patch) && patch.NewVersion.Equals(backwardCursor))
                    .ToList();

                if (previous.Count != 1)
                {
                    if (previous.Count > 1)
                    {
                        warnings.Add("Multiple historical patches lead into the installed version. Historical 'Applied' status may be incomplete.");
                    }

                    break;
                }

                MangosDatabaseUpdatePatch patch = previous[0];
                if (!backwardVersions.Add(patch.ExpectedVersion))
                {
                    break;
                }

                patch.Status = MangosDatabaseUpdatePatchStatus.Applied;
                patch.StatusDetail = "This patch is part of the version chain leading to the installed database version.";
                applied.Add(patch);
                backwardCursor = patch.ExpectedVersion;
            }

            List<MangosDatabaseUpdatePatch> pending = new();
            HashSet<MangosDatabaseUpdatePatch> forwardUsed = new();
            HashSet<MangosDatabaseVersion> visitedVersions = new();
            MangosDatabaseVersion cursor = current;
            visitedVersions.Add(cursor);

            while (true)
            {
                List<MangosDatabaseUpdatePatch> next = recognized
                    .Where(patch => !forwardUsed.Contains(patch) && patch.ExpectedVersion.Equals(cursor))
                    .ToList();

                if (next.Count == 0)
                {
                    break;
                }

                if (next.Count > 1)
                {
                    foreach (MangosDatabaseUpdatePatch patch in next)
                    {
                        patch.Status = MangosDatabaseUpdatePatchStatus.Ambiguous;
                        patch.StatusDetail = "Multiple SQL files declare the same Expected version, so Database Studio cannot choose a safe next patch.";
                    }

                    errors.Add("The update chain is ambiguous at " + cursor.DisplayValue + ": " + string.Join(", ", next.Select(patch => patch.RelativePath)) + ".");
                    break;
                }

                MangosDatabaseUpdatePatch selected = next[0];
                if (selected.NewVersion.Equals(selected.ExpectedVersion) || !visitedVersions.Add(selected.NewVersion))
                {
                    selected.Status = MangosDatabaseUpdatePatchStatus.Invalid;
                    selected.StatusDetail = "The patch creates a cycle in the version chain.";
                    errors.Add("A version cycle was detected in " + selected.RelativePath + ".");
                    break;
                }

                selected.Status = pending.Count == 0
                    ? MangosDatabaseUpdatePatchStatus.Next
                    : MangosDatabaseUpdatePatchStatus.Required;
                selected.StatusDetail = pending.Count == 0
                    ? "This is the next patch for the installed database version."
                    : "This patch follows the previous required patch in the validated update chain.";
                pending.Add(selected);
                forwardUsed.Add(selected);
                cursor = selected.NewVersion;
            }

            plan.PendingPatches = pending;
            plan.TargetVersion = pending.Count > 0 ? cursor : current;
            plan.AppliedFiles = patches.Count(patch => patch.Status == MangosDatabaseUpdatePatchStatus.Applied);
            plan.UnrelatedFiles = patches.Count(patch => patch.IsRecognized &&
                                                       patch.Status == MangosDatabaseUpdatePatchStatus.Unrelated);

            if (patches.Count == 0)
            {
                warnings.Add("No .sql files were found beneath the configured update source folder.");
            }
            else if (recognized.Count == 0)
            {
                warnings.Add("SQL files were found, but none contained the MaNGOS Expected/New version metadata used by Database Studio.");
            }
            else if (pending.Count == 0 && errors.Count == 0)
            {
                bool currentAppearsAsCompletedPatch = recognized.Any(patch => patch.NewVersion.Equals(current));
                if (currentAppearsAsCompletedPatch)
                {
                    warnings.Add("No later recognized patch starts from the installed version " + current.DisplayValue + ". The database appears current for the reachable chain in this source.");
                }
                else
                {
                    warnings.Add("The installed version " + current.DisplayValue + " is not represented by a recognized update transition in this source. The source may target a different release, or the required next patch may be missing.");
                }
            }

            if (pending.Count > 0 && plan.UnrelatedFiles > 0)
            {
                warnings.Add(plan.UnrelatedFiles + " recognized SQL file" + (plan.UnrelatedFiles == 1 ? " is" : "s are") + " not connected to the validated chain and will not be executed.");
            }

            List<MangosDatabaseUpdatePatch> ordered = patches
                .OrderBy(patch => StatusOrder(patch.Status))
                .ThenBy(patch => patch.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            plan.Patches = ordered;
        }

        // Handle status order within the mangos database update service workflow.
        private static int StatusOrder(MangosDatabaseUpdatePatchStatus status)
        {
            return status switch
            {
                MangosDatabaseUpdatePatchStatus.Next => 0,
                MangosDatabaseUpdatePatchStatus.Required => 1,
                MangosDatabaseUpdatePatchStatus.Ambiguous => 2,
                MangosDatabaseUpdatePatchStatus.Invalid => 3,
                MangosDatabaseUpdatePatchStatus.Applied => 4,
                MangosDatabaseUpdatePatchStatus.Unrelated => 5,
                _ => 6
            };
        }
    }
}
