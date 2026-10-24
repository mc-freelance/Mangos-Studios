// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.RolesManage)]
    public sealed class RolesController : Controller
    {
        private readonly IRolePermissionStore _rolePermissions;

        // Initialize the roles controller dependencies and state.
        public RolesController(IRolePermissionStore rolePermissions)
        {
            _rolePermissions = rolePermissions;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> permissions = await _rolePermissions.GetAllAsync(cancellationToken);
            RolePermissionsIndexViewModel model = new()
            {
                Roles = AppRoles.All.Select(role => new RolePermissionSummaryViewModel
                {
                    Role = role,
                    DisplayName = AppRoles.GetDisplayName(role),
                    Description = AppRoles.GetDescription(role),
                    GrantedPermissionCount = permissions.TryGetValue(role, out IReadOnlyCollection<string>? values) ? values.Count : 0,
                    TotalPermissionCount = string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal)
                        ? AppPermissions.All.Count
                        : AppPermissions.All.Count(permission => !permission.AdministratorOnly),
                    IsAdministrator = string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal)
                }).ToArray()
            };

            return View(model);
        }

        [HttpGet]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(string role, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                return NotFound();
            }

            IReadOnlyCollection<string> permissions = await _rolePermissions.GetPermissionsAsync(role, cancellationToken);
            return View(BuildEditModel(role, permissions));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(RolePermissionsEditViewModel model, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(model.Role))
            {
                return NotFound();
            }

            try
            {
                await _rolePermissions.UpdateAsync(model.Role, model.Permissions, cancellationToken);
                TempData["Success"] = string.Equals(model.Role, AppRoles.Administrator, StringComparison.Ordinal)
                    ? "Administrator permissions remain fully granted."
                    : AppRoles.GetDisplayName(model.Role) + " permissions updated.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                return View(BuildEditModel(model.Role, model.Permissions));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle reset within the roles controller workflow.
        public async Task<IActionResult> Reset(string role, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                return NotFound();
            }

            await _rolePermissions.ResetAsync(role, cancellationToken);
            TempData["Success"] = AppRoles.GetDisplayName(role) + " permissions reset to their defaults.";
            return RedirectToAction("Edit", new
            {
                role
            });
        }

        // Build edit model as part of the authorized desktop request.
        private static RolePermissionsEditViewModel BuildEditModel(string role, IEnumerable<string> selectedPermissions)
        {
            HashSet<string> selected = new(AppPermissions.NormalizeForRole(role, selectedPermissions), StringComparer.Ordinal);
            bool isAdministrator = string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal);
            return new RolePermissionsEditViewModel
            {
                Role = role,
                DisplayName = AppRoles.GetDisplayName(role),
                Description = AppRoles.GetDescription(role),
                IsAdministrator = isAdministrator,
                Permissions = selected.ToList(),
                Groups = AppPermissions.All
                    .GroupBy(permission => permission.Category)
                    .Select(group => new PermissionGroupViewModel
                    {
                        Name = group.Key,
                        Permissions = group.Select(permission => new PermissionOptionViewModel
                        {
                            Name = permission.Name,
                            DisplayName = permission.DisplayName,
                            Description = permission.Description,
                            Selected = selected.Contains(permission.Name),
                            AdministratorOnly = permission.AdministratorOnly,
                            Dependencies = permission.Dependencies
                        }).ToArray()
                    }).ToArray()
            };
        }
    }
}
