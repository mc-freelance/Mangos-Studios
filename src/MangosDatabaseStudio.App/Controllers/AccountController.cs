// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

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
    public sealed class AccountController : Controller
    {
        private readonly IApplicationUserStore _users;
        private readonly IRolePermissionStore _rolePermissions;

        // Initialize the account controller dependencies and state.
        public AccountController(IApplicationUserStore users, IRolePermissionStore rolePermissions)
        {
            _users = users;
            _rolePermissions = rolePermissions;
        }

        [HttpGet]
        // Handle the login page or action with the surrounding authorization checks.
        public async Task<IActionResult> Login(string? returnUrl, CancellationToken cancellationToken)
        {
            if (!await _users.AnyAsync(cancellationToken))
            {
                return RedirectToAction("Index", "Setup");
            }

            if (User.Identity?.IsAuthenticated is true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle the login page or action with the surrounding authorization checks.
        public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            ApplicationUser? user = await _users.ValidateAsync(model.Username, model.Password, cancellationToken);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
                return View(model);
            }

            System.Collections.Generic.IReadOnlyCollection<string> permissions = await _rolePermissions.GetPermissionsAsync(user.Role, cancellationToken);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ApplicationPermissionClaims.CreatePrincipal(user, permissions), new AuthenticationProperties
            {
                IsPersistent = model.RememberMe
            });

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle the logout page or action with the surrounding authorization checks.
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // Handle access denied within the account controller workflow.
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
