// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Claims;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.DatabaseUpdates;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.DatabaseUpdates;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.DatabaseUpdatesView)]
    public sealed class DatabaseUpdatesController : StudioController
    {
        private static readonly ConcurrentDictionary<string, byte> ActiveExecutions = new(StringComparer.OrdinalIgnoreCase);

        private readonly IMySqlSchemaService _schema;
        private readonly IMangosDatabaseUpdateService _updates;
        private readonly IMangosDatabaseUpdateSourceStore _sources;
        private readonly IAuthorizationService _authorization;
        private readonly IChangeStorageStore _changeStorage;
        private readonly OperationProgressReporter _operationProgress;

        // Initialize the database updates controller dependencies and state.
        public DatabaseUpdatesController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            IMangosDatabaseUpdateService updates,
            IMangosDatabaseUpdateSourceStore sources,
            IAuthorizationService authorization,
            IChangeStorageStore changeStorage,
            OperationProgressReporter operationProgress)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _updates = updates;
            _sources = sources;
            _authorization = authorization;
            _changeStorage = changeStorage;
            _operationProgress = operationProgress;
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
            string selectedDatabase = DatabaseVisibilityPolicy.ResolveSelection(databases, database, profile.DefaultDatabase);
            bool canScan = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseUpdatesScan)).Succeeded;
            bool canExecute = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseUpdatesExecute)).Succeeded;
            bool canConfigure = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseUpdatesConfigure)).Succeeded;

            MangosDatabaseUpdateSource? source = string.IsNullOrWhiteSpace(selectedDatabase)
                ? null
                : await _sources.GetAsync(profile.Id, selectedDatabase, cancellationToken);
            string sourceDirectory = source?.SourceDirectory ?? string.Empty;

            MangosDatabaseUpdatePlan? plan = null;
            if (canScan && !string.IsNullOrWhiteSpace(selectedDatabase))
            {
                plan = await _updates.BuildPlanAsync(
                    profile,
                    selectedDatabase,
                    sourceDirectory,
                    null,
                    cancellationToken);
            }

            return View(new DatabaseUpdatesViewModel
            {
                Databases = databases,
                Database = selectedDatabase,
                ConnectionName = profile.Name,
                SourceDirectory = sourceDirectory,
                Plan = plan,
                CanScan = canScan,
                CanExecute = canExecute,
                CanConfigure = canConfigure
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.DatabaseUpdatesConfigure)]
        // Prepare the selected view or operation from the supplied settings.
        public async Task<IActionResult> Configure(DatabaseUpdateSourceViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            string selectedDatabase = DatabaseVisibilityPolicy.ResolveSelection(databases, model.Database, profile.DefaultDatabase);
            if (!DatabaseVisibilityPolicy.Contains(databases, selectedDatabase))
            {
                return BadRequest("The selected database is not available.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "The update source path is too long or invalid.";
                return RedirectToAction("Index", new
                {
                    database = selectedDatabase
                });
            }

            string sourceDirectory = (model.SourceDirectory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                await _sources.DeleteAsync(profile.Id, selectedDatabase, cancellationToken);
                TempData["Success"] = "Database update source cleared for " + selectedDatabase + ".";
            }
            else
            {
                await _sources.SaveAsync(new MangosDatabaseUpdateSource
                {
                    ConnectionId = profile.Id,
                    DatabaseName = selectedDatabase,
                    SourceDirectory = sourceDirectory
                }, cancellationToken);
                TempData["Success"] = "Database update source saved for " + selectedDatabase + ". Database Studio will scan this folder and its subfolders for .sql updates.";
            }

            return RedirectToAction("Index", new
            {
                database = selectedDatabase
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.DatabaseUpdatesExecute)]
        // Perform the requested operation after applying the relevant validation.
        public async Task<IActionResult> Execute(DatabaseUpdateExecuteViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            if (!DatabaseVisibilityPolicy.Contains(databases, model.Database))
            {
                return BadRequest("The selected database is not available for updates.");
            }

            if (!model.Confirm)
            {
                TempData["Error"] = "Confirm the validated update plan before execution.";
                return RedirectToAction("Index", new
                {
                    database = model.Database
                });
            }

            MangosDatabaseUpdateSource? source = await _sources.GetAsync(profile.Id, model.Database, cancellationToken);
            if (source == null || string.IsNullOrWhiteSpace(source.SourceDirectory))
            {
                TempData["Error"] = "Configure an update source folder for this database before applying updates.";
                return RedirectToAction("Index", new
                {
                    database = model.Database
                });
            }

            string executionKey = profile.Id.ToString("D") + "|" + model.Database;
            if (!ActiveExecutions.TryAdd(executionKey, 0))
            {
                TempData["Warning"] = "An update operation is already running for this database.";
                return RedirectToAction("Index", new
                {
                    database = model.Database
                });
            }

            IProgress<OperationProgressUpdate>? progress = _operationProgress.Begin(
                "Preparing database updates",
                "Database Studio is validating the update chain before applying any patch.");
            MangosDatabaseUpdateBatchResult result;
            try
            {
                result = await _updates.ApplyAsync(
                    profile,
                    model.Database,
                    source.SourceDirectory,
                    progress,
                    cancellationToken);
                _operationProgress.Complete(result.Message);
            }
            catch (Exception exception)
            {
                _operationProgress.Fail(exception.Message);
                throw;
            }
            finally
            {
                ActiveExecutions.TryRemove(executionKey, out _);
            }

            int appliedCount = result.PatchResults.Count(patch => patch.Succeeded);
            if (appliedCount > 0)
            {
                try
                {
                    string finalVersion = result.FinalVersion?.DisplayValue ?? "unknown";
                    await _changeStorage.RecordAsync(new StoredDatabaseChange
                    {
                        UserId = GetCurrentUserId(),
                        Username = User.Identity?.Name ?? string.Empty,
                        ConnectionId = profile.Id,
                        ConnectionName = profile.Name,
                        DatabaseName = model.Database,
                        Source = StoredChangeSources.DatabaseUpdates,
                        Operation = "PATCH",
                        ObjectName = "db_version update chain",
                        Summary = "Applied " + appliedCount + " database update patch(es); final version " + finalVersion + ".",
                        SqlText = string.Empty,
                        ContributionEligible = false
                    }, cancellationToken);
                }
                catch (Exception)
                {
                    TempData["Warning"] = "The database updates completed, but their history could not be saved to Change Storage.";
                }
            }

            return View("Result", new DatabaseUpdateResultViewModel
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
    }
}
