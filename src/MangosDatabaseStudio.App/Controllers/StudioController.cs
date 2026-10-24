// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using MangosDatabaseStudio.Application.Connections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize]
    public abstract class StudioController : Controller
    {
        protected readonly IActiveConnectionAccessor ActiveConnectionAccessor;

        // Initialize the studio controller dependencies and state.
        protected StudioController(IActiveConnectionAccessor activeConnectionAccessor)
        {
            ActiveConnectionAccessor = activeConnectionAccessor;
        }

        // Handle no active connection within the studio controller workflow.
        protected IActionResult NoActiveConnection()
        {
            TempData["Warning"] = "Select a MySQL or MariaDB connection before opening this area.";
            return RedirectToAction("Index", "Connections");
        }
    }
}
