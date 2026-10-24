// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Text.RegularExpressions;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.SqlConsoleView)]
    public sealed class SqlConsoleController : StudioController
    {
        private readonly IMySqlSchemaService _schema;
        private readonly ISqlExecutionService _sql;
        private readonly IAuthorizationService _authorization;
        private readonly IChangeStorageStore _changeStorage;
        private readonly OperationProgressReporter _operationProgress;
        private static readonly Regex MutationPattern = new(@"\b(INSERT|UPDATE|DELETE|REPLACE|CREATE|ALTER|DROP|TRUNCATE|RENAME)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Initialize the sql console controller dependencies and state.
        public SqlConsoleController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            ISqlExecutionService sql,
            IAuthorizationService authorization,
            IChangeStorageStore changeStorage,
            OperationProgressReporter operationProgress)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _sql = sql;
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

            IReadOnlyList<DatabaseMetadata> databases = DatabaseVisibilityPolicy.FilterVisible(
                await _schema.GetDatabasesAsync(profile, cancellationToken));

            return View(new SqlConsoleViewModel
            {
                Databases = databases,
                Database = DatabaseVisibilityPolicy.ResolveSelection(databases, database, profile.DefaultDatabase)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.SqlConsoleExecute)]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(SqlConsoleViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            model.Databases = DatabaseVisibilityPolicy.FilterVisible(
                await _schema.GetDatabasesAsync(profile, cancellationToken));

            if (!DatabaseVisibilityPolicy.Contains(model.Databases, model.Database))
            {
                ModelState.AddModelError(nameof(model.Database), "Select a visible user database.");
            }

            bool destructive = _sql.IsDestructive(model.Sql);
            if (destructive)
            {
                AuthorizationResult destructiveAuthorization = await _authorization.AuthorizeAsync(User, AppPermissions.SqlConsoleDestructive);
                if (!destructiveAuthorization.Succeeded)
                {
                    ModelState.AddModelError(nameof(model.Sql), "Your role is not allowed to execute destructive or privilege-changing SQL.");
                    return View(model);
                }

                if (!model.ConfirmDestructive)
                {
                    model.Warning = "This query appears to contain destructive or privilege-changing SQL. Review it and confirm before execution.";
                    return View(model);
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IProgress<OperationProgressUpdate>? progress = _operationProgress.Begin(
                "Preparing SQL statement",
                "The SQL Console is validating the request and opening the database connection.");
            try
            {
                model.Result = await _sql.ExecuteAsync(profile, model.Database, model.Sql, model.MaximumRows, progress, cancellationToken);
                Match mutation = MutationPattern.Match(model.Sql ?? string.Empty);
                if (model.Result.Succeeded && mutation.Success)
                {
                    try
                    {
                        string operation = mutation.Groups[1].Value.ToUpperInvariant();
                        string summary = model.Result.AffectedRows > 0
                            ? "Executed " + operation + " from SQL Console; " + model.Result.AffectedRows + " row(s) affected."
                            : "Executed " + operation + " from SQL Console.";
                        await _changeStorage.RecordAsync(new StoredDatabaseChange
                        {
                            UserId = GetCurrentUserId(),
                            Username = User.Identity?.Name ?? string.Empty,
                            ConnectionId = profile.Id,
                            ConnectionName = profile.Name,
                            DatabaseName = model.Database,
                            Source = StoredChangeSources.SqlConsole,
                            Operation = operation,
                            ObjectName = "SQL statement",
                            Summary = summary,
                            SqlText = (model.Sql ?? string.Empty).Trim(),
                            ContributionEligible = true
                        }, cancellationToken);
                    }
                    catch (Exception)
                    {
                        TempData["Warning"] = "The SQL statement completed, but its history could not be saved to Change Storage.";
                    }
                }

                _operationProgress.Complete(model.Result.Succeeded ? "SQL execution completed." : "SQL execution finished with an error result.");
            }
            catch (Exception exception)
            {
                _operationProgress.Fail(exception.Message);
                ModelState.AddModelError(string.Empty, exception.Message);
            }

            return View(model);
        }

        // Read current user id as part of the authorized desktop request.
        private Guid GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }
    }
}
