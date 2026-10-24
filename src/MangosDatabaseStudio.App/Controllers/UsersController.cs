// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.UsersView)]
    public sealed class UsersController : Controller
    {
        private readonly IApplicationUserStore _users;
        private readonly IUserAvatarStorage _avatars;
        private readonly IRolePermissionStore _rolePermissions;
        private readonly IAuthorizationService _authorization;

        // Initialize the users controller dependencies and state.
        public UsersController(
            IApplicationUserStore users,
            IUserAvatarStorage avatars,
            IRolePermissionStore rolePermissions,
            IAuthorizationService authorization)
        {
            _users = users;
            _avatars = avatars;
            _rolePermissions = rolePermissions;
            _authorization = authorization;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            IReadOnlyList<ApplicationUser> users = await _users.GetAllAsync(cancellationToken);
            HashSet<Guid> manageableUserIds = new();
            foreach (ApplicationUser user in users)
            {
                if (await CanManageUserAsync(user, cancellationToken))
                {
                    manageableUserIds.Add(user.Id);
                }
            }

            return View(new UsersIndexViewModel
            {
                Users = users,
                CurrentUserId = GetCurrentUserId(),
                CanCreate = await HasPermissionAsync(AppPermissions.UsersCreate),
                CanEdit = await HasPermissionAsync(AppPermissions.UsersEdit),
                CanDelete = await HasPermissionAsync(AppPermissions.UsersDelete),
                CanManageRoles = await HasPermissionAsync(AppPermissions.RolesManage),
                IsAdministrator = User.IsInRole(AppRoles.Administrator),
                ManageableUserIds = manageableUserIds
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.UsersCreate)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            UserEditViewModel model = await PrepareModelAsync(new UserEditViewModel
            {
                IsCreate = true,
                Role = AppRoles.Viewer
            }, cancellationToken);
            if (!model.AvailableRoles.Contains(model.Role, StringComparer.Ordinal) && model.AvailableRoles.Count > 0)
            {
                model.Role = model.AvailableRoles[0];
            }

            return View("Edit", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.UsersCreate)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(UserEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = true;
            await PrepareModelAsync(model, cancellationToken);
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "A password is required for a new user.");
            }

            ValidatePassword(model.Password);
            ValidateRole(model.Role);
            await ValidateRoleAssignmentAsync(model.Role, cancellationToken);
            if (!ModelState.IsValid)
            {
                return View("Edit", model);
            }

            try
            {
                await _users.CreateAsync(model.Username, model.Password!, model.Role, cancellationToken);
                TempData["Success"] = "User account created.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                return View("Edit", model);
            }
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.UsersEdit)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
        {
            ApplicationUser? user = await _users.GetAsync(id, cancellationToken);
            if (user == null)
            {
                return NotFound();
            }

            if (!await CanManageUserAsync(user, cancellationToken))
            {
                return Forbid();
            }

            return View(await PrepareModelAsync(new UserEditViewModel
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                IsCreate = false,
                IsCurrentUser = GetCurrentUserId() == user.Id
            }, cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.UsersEdit)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(UserEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = false;
            await PrepareModelAsync(model, cancellationToken);
            Guid? currentUserId = GetCurrentUserId();
            model.IsCurrentUser = currentUserId == model.Id;
            ApplicationUser? existing = await _users.GetAsync(model.Id, cancellationToken);
            if (existing == null)
            {
                return NotFound();
            }

            if (!await CanManageUserAsync(existing, cancellationToken))
            {
                return Forbid();
            }

            ValidatePassword(model.Password);
            ValidateRole(model.Role);
            await ValidateRoleAssignmentAsync(model.Role, cancellationToken);

            if (model.IsCurrentUser && !User.IsInRole(AppRoles.Administrator) && !string.Equals(model.Role, existing.Role, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.Role), "You cannot change your own role.");
            }

            if (model.IsCurrentUser && string.Equals(existing.Role, AppRoles.Administrator, StringComparison.Ordinal) &&
                !string.Equals(model.Role, AppRoles.Administrator, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.Role), "You cannot remove your own administrator role while signed in.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                await _users.UpdateAsync(model.Id, model.Username, model.Role, model.Password, cancellationToken);
                TempData["Success"] = model.IsCurrentUser
                    ? "Your account was updated. Your current session will refresh automatically."
                    : "User account updated.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.UsersDelete)]
        // Handle the delete page or action with the surrounding authorization checks.
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (GetCurrentUserId() == id)
            {
                TempData["Error"] = "You cannot delete the account that is currently signed in.";
                return RedirectToAction("Index");
            }

            try
            {
                ApplicationUser? user = await _users.GetAsync(id, cancellationToken);
                if (user == null)
                {
                    return RedirectToAction("Index");
                }

                if (!await CanManageUserAsync(user, cancellationToken))
                {
                    return Forbid();
                }

                await _users.DeleteAsync(id, cancellationToken);
                _avatars.Delete(user.AvatarFileName);
                TempData["Success"] = "User account deleted.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction("Index");
        }

        // Handle prepare model async within the users controller workflow.
        private async Task<UserEditViewModel> PrepareModelAsync(UserEditViewModel model, CancellationToken cancellationToken)
        {
            model.AvailableRoles = await GetAssignableRolesAsync(cancellationToken);
            return model;
        }

        // Read assignable roles as part of the authorized desktop request.
        private async Task<IReadOnlyList<string>> GetAssignableRolesAsync(CancellationToken cancellationToken)
        {
            if (User.IsInRole(AppRoles.Administrator))
            {
                return AppRoles.All;
            }

            List<string> roles = new();
            foreach (string role in AppRoles.All)
            {
                if (string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyCollection<string> required = await _rolePermissions.GetPermissionsAsync(role, cancellationToken);
                if (User.HasAllPermissions(required))
                {
                    roles.Add(role);
                }
            }

            return roles;
        }

        // Determine whether manage user as part of the authorized desktop request.
        private async Task<bool> CanManageUserAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            if (User.IsInRole(AppRoles.Administrator))
            {
                return true;
            }

            if (string.Equals(user.Role, AppRoles.Administrator, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyCollection<string> required = await _rolePermissions.GetPermissionsAsync(user.Role, cancellationToken);
            return User.HasAllPermissions(required);
        }

        // Validate role assignment as part of the authorized desktop request.
        private async Task ValidateRoleAssignmentAsync(string role, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                return;
            }

            if (User.IsInRole(AppRoles.Administrator))
            {
                return;
            }

            if (string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(UserEditViewModel.Role), "Only an administrator can assign the Administrator role.");
                return;
            }

            IReadOnlyCollection<string> required = await _rolePermissions.GetPermissionsAsync(role, cancellationToken);
            if (!User.HasAllPermissions(required))
            {
                ModelState.AddModelError(nameof(UserEditViewModel.Role), "You cannot assign a role that grants permissions you do not have.");
            }
        }

        // Determine whether permission as part of the authorized desktop request.
        private async Task<bool> HasPermissionAsync(string permission)
        {
            AuthorizationResult result = await _authorization.AuthorizeAsync(User, permission);
            return result.Succeeded;
        }

        // Validate password as part of the authorized desktop request.
        private void ValidatePassword(string? password)
        {
            if (!string.IsNullOrWhiteSpace(password) && password.Length < 12)
            {
                ModelState.AddModelError(nameof(UserEditViewModel.Password), "Passwords must contain at least 12 characters.");
            }
        }

        // Validate role as part of the authorized desktop request.
        private void ValidateRole(string role)
        {
            if (!AppRoles.IsValid(role))
            {
                ModelState.AddModelError(nameof(UserEditViewModel.Role), "Choose a valid application role.");
            }
        }

        // Read current user id as part of the authorized desktop request.
        private Guid? GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : null;
        }
    }
}
