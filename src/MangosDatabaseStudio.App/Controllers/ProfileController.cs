// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize]
    public sealed class ProfileController : Controller
    {
        private readonly IApplicationUserStore _users;
        private readonly IUserAvatarStorage _avatars;
        private readonly IDesktopDeviceIdentityAccessor _deviceIdentity;

        // Initialize the profile controller dependencies and state.
        public ProfileController(
            IApplicationUserStore users,
            IUserAvatarStorage avatars,
            IDesktopDeviceIdentityAccessor deviceIdentity)
        {
            _users = users;
            _avatars = avatars;
            _deviceIdentity = deviceIdentity;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            return user == null ? Challenge() : View(BuildProfile(user));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Apply a change to the tracked progress snapshot safely.
        public async Task<IActionResult> Update(ProfileViewModel model, CancellationToken cancellationToken)
        {
            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Challenge();
            }

            model.Id = user.Id;
            model.Username = user.Username;
            model.Role = user.Role;
            model.HasUploadedAvatar = !string.IsNullOrWhiteSpace(user.AvatarFileName);
            model.Initials = UserAvatarPresentation.GetInitials(user);
            model.DeviceIdentity = BuildDeviceIdentity();

            if (!UserAvatarModes.IsValid(model.AvatarMode))
            {
                ModelState.AddModelError(nameof(model.AvatarMode), "Choose a valid avatar source.");
            }

            bool usesGravatar = string.Equals(model.AvatarMode, UserAvatarModes.Gravatar, StringComparison.Ordinal);
            if (!usesGravatar && string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.Remove(nameof(model.Email));
            }

            if (usesGravatar && string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "An email address is required to use Gravatar.");
            }

            if (string.Equals(model.AvatarMode, UserAvatarModes.Upload, StringComparison.Ordinal) &&
                model.AvatarFile == null && string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                ModelState.AddModelError(nameof(model.AvatarFile), "Choose an image to use an uploaded avatar.");
            }

            if (!ModelState.IsValid)
            {
                model.AvatarUrl = UserAvatarPresentation.GetImageUrl(user);
                return View("Index", model);
            }

            string? newFileName = user.AvatarFileName;
            bool createdNewFile = false;
            try
            {
                if (model.AvatarFile != null)
                {
                    newFileName = await _avatars.SaveAsync(user.Id, model.AvatarFile, cancellationToken);
                    createdNewFile = true;
                }

                await _users.UpdateProfileAsync(user.Id, model.Email, model.AvatarMode, newFileName, cancellationToken);

                if (!string.IsNullOrWhiteSpace(user.AvatarFileName) &&
                    (!string.Equals(model.AvatarMode, UserAvatarModes.Upload, StringComparison.Ordinal) ||
                     !string.Equals(user.AvatarFileName, newFileName, StringComparison.Ordinal)))
                {
                    _avatars.Delete(user.AvatarFileName);
                }

                TempData["Success"] = "Profile settings saved.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                if (createdNewFile)
                {
                    _avatars.Delete(newFileName);
                }

                ModelState.AddModelError(string.Empty, exception.Message);
                model.AvatarUrl = UserAvatarPresentation.GetImageUrl(user);
                return View("Index", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle change password within the profile controller workflow.
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage));
                return RedirectToAction("Index");
            }

            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Challenge();
            }

            try
            {
                await _users.ChangePasswordAsync(user.Id, model.CurrentPassword, model.NewPassword, cancellationToken);
                TempData["Success"] = "Password updated successfully.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction("Index");
        }

        [HttpGet("/profile/avatar/{id:guid}")]
        // Handle avatar within the profile controller workflow.
        public async Task<IActionResult> Avatar(Guid id, CancellationToken cancellationToken)
        {
            ApplicationUser? user = await _users.GetAsync(id, cancellationToken);
            if (user == null || !string.Equals(user.AvatarMode, UserAvatarModes.Upload, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                return NotFound();
            }

            AvatarFile? avatar = _avatars.Open(user.AvatarFileName);
            if (avatar == null)
            {
                return NotFound();
            }

            return File(avatar.Stream, avatar.ContentType, enableRangeProcessing: true);
        }

        // Read current user as part of the authorized desktop request.
        private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? await _users.GetAsync(id, cancellationToken) : null;
        }

        // Build profile as part of the authorized desktop request.
        private ProfileViewModel BuildProfile(ApplicationUser user)
        {
            return new ProfileViewModel
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                Email = user.Email,
                AvatarMode = user.AvatarMode,
                HasUploadedAvatar = !string.IsNullOrWhiteSpace(user.AvatarFileName),
                AvatarUrl = UserAvatarPresentation.GetImageUrl(user),
                Initials = UserAvatarPresentation.GetInitials(user),
                DeviceIdentity = BuildDeviceIdentity()
            };
        }

        // Build device identity as part of the authorized desktop request.
        private DesktopDeviceIdentityViewModel? BuildDeviceIdentity()
        {
            DesktopDeviceIdentitySnapshot identity = _deviceIdentity.Current;
            return new DesktopDeviceIdentityViewModel
            {
                IsEnabled = identity.IsEnabled,
                IsAvailable = identity.IsAvailable,
                Identifier = identity.Identifier,
                Source = identity.Source,
                Platform = identity.Platform,
                Manufacturer = identity.Manufacturer,
                Model = identity.Model,
                StatusMessage = identity.StatusMessage,
                CollectedAtUtc = identity.CollectedAtUtc
            };
        }
    }
}
