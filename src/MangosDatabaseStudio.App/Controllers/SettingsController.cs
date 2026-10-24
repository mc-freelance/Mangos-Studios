// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
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
    public sealed class SettingsController : Controller
    {
        private readonly IApplicationUserStore _users;

        // Initialize the settings controller dependencies and state.
        public SettingsController(IApplicationUserStore users)
        {
            _users = users;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Challenge();
            }

            UserPreferences preferences = UserPreferences.Normalize(user.Preferences);
            return View(new UserSettingsViewModel
            {
                DefaultPageSize = preferences.DefaultPageSize,
                BrowserColumnCount = preferences.BrowserColumnCount,
                CellPreviewLength = preferences.CellPreviewLength,
                ShowTechnicalColumnNames = preferences.ShowTechnicalColumnNames,
                Theme = preferences.Theme,
                DesktopZoomPercent = preferences.DesktopZoomPercent,
                DesktopAutoScale = preferences.DesktopAutoScale
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(UserSettingsViewModel model, CancellationToken cancellationToken)
        {
            ValidateSettings(model);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Challenge();
            }

            await _users.UpdatePreferencesAsync(user.Id, new UserPreferences
            {
                DefaultPageSize = model.DefaultPageSize,
                BrowserColumnCount = model.BrowserColumnCount,
                CellPreviewLength = model.CellPreviewLength,
                ShowTechnicalColumnNames = model.ShowTechnicalColumnNames,
                Theme = model.Theme,
                DesktopZoomPercent = model.DesktopZoomPercent,
                DesktopAutoScale = model.DesktopAutoScale
            }, cancellationToken);

            TempData["Success"] = "Display settings saved.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle desktop zoom within the settings controller workflow.
        public async Task<IActionResult> DesktopZoom(DesktopZoomUpdateViewModel model, CancellationToken cancellationToken)
        {
            if (!DesktopZoomPreferences.IsSupported(model.ZoomPercent))
            {
                return BadRequest(new
                {
                    message = "Choose a desktop zoom between 50% and 125% in 5% steps."
                });
            }

            ApplicationUser? user = await GetCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Unauthorized();
            }

            UserPreferences current = UserPreferences.Normalize(user.Preferences);
            current.DesktopZoomPercent = model.ZoomPercent;
            current.DesktopAutoScale = model.AutoScale;
            await _users.UpdatePreferencesAsync(user.Id, current, cancellationToken);

            UserPreferences saved = UserPreferences.Normalize(current);
            return Json(new
            {
                zoomPercent = saved.DesktopZoomPercent,
                autoScale = saved.DesktopAutoScale
            });
        }

        // Validate settings as part of the authorized desktop request.
        private void ValidateSettings(UserSettingsViewModel model)
        {
            if (!UserThemeModes.IsValid(model.Theme))
            {
                ModelState.AddModelError(nameof(model.Theme), "Choose a valid appearance setting.");
            }

            if (model.DefaultPageSize != 10 && model.DefaultPageSize != 20 && model.DefaultPageSize != 50 && model.DefaultPageSize != 100 && model.DefaultPageSize != 200)
            {
                ModelState.AddModelError(nameof(model.DefaultPageSize), "Choose a supported page size.");
            }

            if (model.CellPreviewLength != 40 && model.CellPreviewLength != 80 && model.CellPreviewLength != 120)
            {
                ModelState.AddModelError(nameof(model.CellPreviewLength), "Choose a supported preview length.");
            }

            if (!DesktopZoomPreferences.IsSupported(model.DesktopZoomPercent))
            {
                ModelState.AddModelError(nameof(model.DesktopZoomPercent), "Choose a desktop zoom between 50% and 125% in 5% steps.");
            }
        }

        // Read current user as part of the authorized desktop request.
        private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? await _users.GetAsync(id, cancellationToken) : null;
        }
    }
}
