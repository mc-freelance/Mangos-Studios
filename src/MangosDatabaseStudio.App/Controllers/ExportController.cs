// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.SqlExportView)]
    public sealed class ExportController : StudioController
    {
        private const string DownloadCookieName = "MdsExportDownload";
        private readonly IMySqlSchemaService _schema;
        private readonly ISqlExportService _exports;
        private readonly ApplicationVersionInfo _applicationVersion;
        private readonly OperationProgressReporter _operationProgress;
        private readonly string _exportDirectory;

        // Initialize the export controller dependencies and state.
        public ExportController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            ISqlExportService exports,
            ApplicationVersionInfo applicationVersion,
            OperationProgressReporter operationProgress,
            IWebHostEnvironment environment)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _exports = exports;
            _applicationVersion = applicationVersion;
            _operationProgress = operationProgress;
            _exportDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "exports");
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

            SqlExportViewModel model = await BuildModelAsync(profile, database, cancellationToken);
            model.SelectedTables = model.Tables.Select(table => table.Name).ToList();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.SqlExportExecute)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(SqlExportViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            DatabaseMetadata? selectedDatabase = databases.FirstOrDefault(database =>
                string.Equals(database.Name, model.Database, StringComparison.OrdinalIgnoreCase));
            bool validDatabase = selectedDatabase != null;
            if (!validDatabase)
            {
                ModelState.AddModelError(nameof(model.Database), "Select a visible user database.");
            }
            else
            {
                model.Database = selectedDatabase!.Name;
            }

            IReadOnlyList<TableMetadata> tables = validDatabase
                ? await _schema.GetTablesAsync(profile, model.Database, cancellationToken)
                : new List<TableMetadata>();
            List<string> selectedTables = ResolveSelectedTables(tables, model.SelectedTables);
            if (selectedTables.Count == 0)
            {
                ModelState.AddModelError(nameof(model.SelectedTables), "Select at least one table to export.");
            }

            if (!ModelState.IsValid)
            {
                model.Databases = databases;
                model.Tables = tables;
                model.SelectedTables = selectedTables.ToList();
                model.DownloadToken = Guid.NewGuid().ToString("N");
                return View("Index", model);
            }

            SqlExportRequest request = new()
            {
                Database = model.Database,
                Tables = selectedTables,
                AllTablesSelected = selectedTables.Count == tables.Count,
                Content = model.Content,
                IncludeDropStatements = model.IncludeDropStatements,
                IncludeTriggers = model.IncludeTriggers,
                IncludeRoutines = model.IncludeRoutines,
                IncludeEvents = model.IncludeEvents,
                UseSingleTransaction = model.UseSingleTransaction,
                CompressOutput = model.CompressOutput,
                ApplicationVersion = _applicationVersion.Version + " (" + _applicationVersion.Stage + ")"
            };

            IProgress<OperationProgressUpdate>? progress = _operationProgress.Begin(
                "Preparing SQL export",
                "Database Studio is preparing the selected database objects for export.");
            SqlExportResult result;
            try
            {
                result = await _exports.ExportAsync(profile, request, _exportDirectory, progress, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _operationProgress.Fail(exception.Message);
                model.Databases = databases;
                model.Tables = tables;
                model.SelectedTables = selectedTables.ToList();
                model.DownloadToken = Guid.NewGuid().ToString("N");
                ModelState.AddModelError(string.Empty, exception.Message);
                return View("Index", model);
            }

            FileStream stream;
            try
            {
                stream = new FileStream(
                    result.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            }
            catch (Exception exception)
            {
                _operationProgress.Fail(exception.Message);
                TryDelete(result.FilePath);
                throw;
            }

            Response.Cookies.Append(DownloadCookieName, model.DownloadToken, new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/"
            });

            _operationProgress.Complete("SQL export is ready for download.");
            return File(stream, result.ContentType, result.DownloadFileName);
        }

        // Build model as part of the authorized desktop request.
        private async Task<SqlExportViewModel> BuildModelAsync(
            ConnectionProfile profile,
            string? requestedDatabase,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DatabaseMetadata> databases = await GetVisibleDatabasesAsync(profile, cancellationToken);
            string resolvedDatabase = DatabaseVisibilityPolicy.ResolveSelection(databases, requestedDatabase, profile.DefaultDatabase);
            string database = databases.FirstOrDefault(item =>
                string.Equals(item.Name, resolvedDatabase, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
            IReadOnlyList<TableMetadata> tables = string.IsNullOrWhiteSpace(database)
                ? new List<TableMetadata>()
                : await _schema.GetTablesAsync(profile, database, cancellationToken);

            return new SqlExportViewModel
            {
                Databases = databases,
                Database = database,
                Tables = tables,
                DownloadToken = Guid.NewGuid().ToString("N")
            };
        }

        // Read visible databases as part of the authorized desktop request.
        private async Task<IReadOnlyList<DatabaseMetadata>> GetVisibleDatabasesAsync(
            ConnectionProfile profile,
            CancellationToken cancellationToken)
        {
            return DatabaseVisibilityPolicy.FilterVisible(
                await _schema.GetDatabasesAsync(profile, cancellationToken));
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
                // Temporary exports are also removed by the next stale-file cleanup.
            }
        }

        // Resolve selected tables as part of the authorized desktop request.
        private static List<string> ResolveSelectedTables(
            IReadOnlyList<TableMetadata> tables,
            IEnumerable<string>? requestedTables)
        {
            Dictionary<string, string> available = tables.ToDictionary(
                table => table.Name,
                table => table.Name,
                StringComparer.OrdinalIgnoreCase);
            List<string> selected = new();

            foreach (string requested in requestedTables ?? Array.Empty<string>())
            {
                if (available.TryGetValue(requested, out string? actualName) &&
                    !selected.Contains(actualName, StringComparer.OrdinalIgnoreCase))
                {
                    selected.Add(actualName);
                }
            }

            selected.Sort(StringComparer.OrdinalIgnoreCase);
            return selected;
        }
    }
}
