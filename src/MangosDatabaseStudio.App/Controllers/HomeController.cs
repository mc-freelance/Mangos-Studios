// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    public sealed class HomeController : Controller
    {
        private readonly IApplicationUserStore _users;
        private readonly IActiveConnectionAccessor _activeConnection;
        private readonly IMySqlSchemaService _schema;
        private readonly IAuthorizationService _authorization;

        // Initialize the home controller dependencies and state.
        public HomeController(IApplicationUserStore users, IActiveConnectionAccessor activeConnection, IMySqlSchemaService schema, IAuthorizationService authorization)
        {
            _users = users;
            _activeConnection = activeConnection;
            _schema = schema;
            _authorization = authorization;
        }

        [AllowAnonymous]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (!await _users.AnyAsync(cancellationToken))
            {
                return RedirectToAction("Index", "Setup");
            }

            if (User.Identity?.IsAuthenticated is not true)
            {
                return RedirectToAction("Login", "Account");
            }

            DashboardViewModel model = new()
            {
                CanBrowseWorld = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldBrowse)).Succeeded,
                CanBrowseDatabases = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseBrowse)).Succeeded,
                CanUseSqlConsole = (await _authorization.AuthorizeAsync(User, AppPermissions.SqlConsoleView)).Succeeded,
                CanUseSqlImport = (await _authorization.AuthorizeAsync(User, AppPermissions.SqlImportView)).Succeeded,
                CanUseSqlExport = (await _authorization.AuthorizeAsync(User, AppPermissions.SqlExportView)).Succeeded,
                CanViewConnections = (await _authorization.AuthorizeAsync(User, AppPermissions.ConnectionsView)).Succeeded,
                CanSelectConnections = (await _authorization.AuthorizeAsync(User, AppPermissions.ConnectionsSelect)).Succeeded
            };

            ConnectionProfile? profile = await _activeConnection.GetActiveAsync(cancellationToken);
            model.ActiveConnection = profile;
            if (profile == null)
            {
                return View(model);
            }

            if (model.CanBrowseWorld || model.CanBrowseDatabases)
            {
                try
                {
                    model.Databases = DatabaseVisibilityPolicy.FilterVisible(await _schema.GetDatabasesAsync(profile, cancellationToken));
                    if (DatabaseVisibilityPolicy.IsVisible(profile.DefaultDatabase))
                    {
                        model.DefaultDatabaseTables = await _schema.GetTablesAsync(profile, profile.DefaultDatabase, cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    model.Error = exception.Message;
                }
            }

            return View(model);
        }

        [AllowAnonymous]
        // Handle error within the home controller workflow.
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }

    public sealed class ErrorViewModel
    {
        public string? RequestId
        {
            get; set;
        }

        public bool ShowRequestId
        {
            get
            {
                return !string.IsNullOrEmpty(RequestId);
            }
        }
    }
}
