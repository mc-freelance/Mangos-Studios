// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.Domain.Sql;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.SqlImportView)]
    public sealed class ImportController : StudioController
    {
        private static readonly Regex TokenPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ConcurrentDictionary<string, byte> ActiveExecutions = new(StringComparer.Ordinal);
        private readonly IMySqlSchemaService _schema;
        private readonly ISqlImportService _imports;
        private readonly IChangeStorageStore _changeStorage;
        private readonly OperationProgressReporter _operationProgress;
        private readonly string _importDirectory;

        // Initialize the import controller dependencies and state.
        public ImportController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            ISqlImportService imports,
            IChangeStorageStore changeStorage,
            OperationProgressReporter operationProgress,
            IWebHostEnvironment environment)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _imports = imports;
            _changeStorage = changeStorage;
            _operationProgress = operationProgress;
            _importDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "imports");
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(string? database, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            return View(new SqlImportUploadViewModel
            {
                Databases = databases,
                Database = DatabaseVisibilityPolicy.ResolveSelection(databases, database, profile.DefaultDatabase)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(512L * 1024L * 1024L)]
        [Authorize(Policy = AppPermissions.SqlImportUpload)]
        // Handle upload within the import controller workflow.
        public async Task<IActionResult> Upload(SqlImportUploadViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            model.Databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            if (!DatabaseVisibilityPolicy.Contains(model.Databases, model.Database))
            {
                ModelState.AddModelError(nameof(model.Database), "Select a visible user database.");
            }

            if (model.File == null || model.File.Length == 0)
            {
                ModelState.AddModelError(nameof(model.File), "Choose a non-empty .sql or .sql.gz file.");
            }

            string originalName = model.File == null ? string.Empty : Path.GetFileName(model.File.FileName);
            bool isSql = originalName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
            bool isGzip = originalName.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase);
            if (!isSql && !isGzip)
            {
                ModelState.AddModelError(nameof(model.File), "Only .sql and .sql.gz files are accepted.");
            }

            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            IProgress<OperationProgressUpdate>? progress = _operationProgress.Begin(
                "Preparing SQL upload",
                isGzip ? "The uploaded gzip stream is being decompressed into a temporary SQL file." : "The uploaded SQL stream is being copied into a temporary analysis file.");
            Directory.CreateDirectory(_importDirectory);
            string token = Guid.NewGuid().ToString("N");
            string sqlPath = GetImportPath(token);
            try
            {
                await using (Stream input = model.File!.OpenReadStream())
                await using (FileStream output = new(
                    sqlPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (isGzip)
                    {
                        await using GZipStream gzip = new(input, CompressionMode.Decompress, leaveOpen: true);
                        await CopyWithLimitAsync(gzip, output, 512L * 1024L * 1024L, null, "Decompressing SQL upload", progress, cancellationToken);
                    }
                    else
                    {
                        await CopyWithLimitAsync(input, output, 512L * 1024L * 1024L, model.File.Length, "Copying SQL upload", progress, cancellationToken);
                    }

                    await output.FlushAsync(cancellationToken);
                }

                SqlImportAnalysis analysis = await _imports.AnalyzeAsync(token, originalName, sqlPath, progress, cancellationToken);
                _operationProgress.Complete("SQL upload analysis is ready for review.");
                return View("Review", new SqlImportReviewViewModel
                {
                    Database = model.Database,
                    Analysis = analysis
                });
            }
            catch (Exception exception)
            {
                _operationProgress.Fail(exception.Message);
                TryDelete(sqlPath);
                ModelState.AddModelError(nameof(model.File), exception.Message);
                return View("Index", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.SqlImportExecute)]
        // Perform the requested operation after applying the relevant validation.
        public async Task<IActionResult> Execute(SqlImportConfirmViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            if (!DatabaseVisibilityPolicy.Contains(databases, model.Database))
            {
                return BadRequest("The selected database is not available for user imports.");
            }

            if (!model.Confirm)
            {
                ModelState.AddModelError(nameof(model.Confirm), "Confirm the target database and import before execution.");
            }

            if (!TokenPattern.IsMatch(model.Token))
            {
                return BadRequest();
            }

            string path = GetImportPath(model.Token);
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                SqlImportAnalysis analysis = await _imports.AnalyzeAsync(model.Token, model.Token + ".sql", path, null, cancellationToken);
                return View("Review", new SqlImportReviewViewModel { Database = model.Database, Analysis = analysis });
            }

            if (!ActiveExecutions.TryAdd(model.Token, 0))
            {
                return View("Result", new SqlImportResultViewModel
                {
                    Database = model.Database,
                    Result = new SqlImportResult
                    {
                        Succeeded = false,
                        ExitCode = -1,
                        StandardError = "This SQL import is already being processed. Wait for the active import to finish before trying again."
                    }
                });
            }

            IProgress<OperationProgressUpdate>? progress = _operationProgress.Begin(
                "Preparing SQL import",
                "The validated SQL file is being prepared for the MySQL/MariaDB command-line client.");
            SqlImportResult result;
            try
            {
                result = await _imports.ExecuteAsync(profile, model.Database, path, progress, cancellationToken);
                _operationProgress.Complete(result.Succeeded ? "SQL import completed successfully." : "SQL import finished with a database-client error.");
            }
            catch (Exception exception)
            {
                _operationProgress.Fail(exception.Message);
                throw;
            }
            finally
            {
                ActiveExecutions.TryRemove(model.Token, out _);
                TryDelete(path);
            }

            if (result.Succeeded)
            {
                try
                {
                    await _changeStorage.RecordAsync(new StoredDatabaseChange
                    {
                        UserId = GetCurrentUserId(),
                        Username = User.Identity?.Name ?? string.Empty,
                        ConnectionId = profile.Id,
                        ConnectionName = profile.Name,
                        DatabaseName = model.Database,
                        Source = StoredChangeSources.SqlImport,
                        Operation = "IMPORT",
                        ObjectName = "SQL import",
                        Summary = "Executed SQL import into " + model.Database + ".",
                        SqlText = string.Empty,
                        ContributionEligible = false
                    }, cancellationToken);
                }
                catch (Exception)
                {
                    TempData["Warning"] = "The SQL import completed, but its history could not be saved to Change Storage.";
                }
            }

            return View("Result", new SqlImportResultViewModel
            {
                Database = model.Database,
                Result = result
            });
        }

        // Read current user id as part of the authorized desktop request.
        private Guid GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }

        // Read visible databases as part of the authorized desktop request.
        private async Task<IReadOnlyList<DatabaseMetadata>> GetVisibleDatabasesAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            return DatabaseVisibilityPolicy.FilterVisible(
                await _schema.GetDatabasesAsync(profile, cancellationToken));
        }

        // Handle copy with limit async within the import controller workflow.
        private static async Task CopyWithLimitAsync(
            Stream input,
            Stream output,
            long maximumBytes,
            long? expectedBytes,
            string stage,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long total = 0;
            long nextReport = 1024 * 1024;
            progress?.Report(new OperationProgressUpdate(stage, "Starting SQL file preparation.", 0, expectedBytes, "bytes"));
            try
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maximumBytes)
                    {
                        throw new InvalidOperationException("The decompressed SQL file exceeds the 512 MB import limit.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (total >= nextReport || (expectedBytes.HasValue && total >= expectedBytes.Value))
                    {
                        progress?.Report(new OperationProgressUpdate(
                            stage,
                            total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " SQL bytes prepared.",
                            total,
                            expectedBytes,
                            "bytes"));
                        nextReport = total + (1024 * 1024);
                    }
                }

                progress?.Report(new OperationProgressUpdate(
                    stage,
                    total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " SQL bytes prepared.",
                    total,
                    expectedBytes ?? total,
                    "bytes"));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Attempt to delete as part of the authorized desktop request.
        private static void TryDelete(string path)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch
            {
                // Temporary SQL files are removed on a best-effort basis.
            }
        }

        // Read import path as part of the authorized desktop request.
        private string GetImportPath(string token)
        {
            return Path.Combine(_importDirectory, token + ".sql");
        }
    }
}
