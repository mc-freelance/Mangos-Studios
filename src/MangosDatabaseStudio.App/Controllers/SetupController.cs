// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [AllowAnonymous]
    public sealed class SetupController : Controller
    {
        private readonly IApplicationUserStore _users;
        private readonly IRolePermissionStore _rolePermissions;

        // Initialize the setup controller dependencies and state.
        public SetupController(IApplicationUserStore users, IRolePermissionStore rolePermissions)
        {
            _users = users;
            _rolePermissions = rolePermissions;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (await _users.AnyAsync(cancellationToken))
            {
                return RedirectToAction("Login", "Account");
            }

            return View(new SetupViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(SetupViewModel model, CancellationToken cancellationToken)
        {
            if (await _users.AnyAsync(cancellationToken))
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            await _users.CreateAdministratorAsync(model.Username, model.Password, cancellationToken);
            ApplicationUser? user = await _users.FindAsync(model.Username, cancellationToken);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "The administrator account could not be loaded after setup.");
                return View(model);
            }

            await SignInAsync(user, true, cancellationToken);
            return RedirectToAction("Index", "Connections");
        }

        // Handle sign in async within the setup controller workflow.
        private async Task SignInAsync(ApplicationUser user, bool persistent, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<string> permissions = await _rolePermissions.GetPermissionsAsync(user.Role, cancellationToken);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ApplicationPermissionClaims.CreatePrincipal(user, permissions), new AuthenticationProperties
            {
                IsPersistent = persistent
            });
        }
    }
}
