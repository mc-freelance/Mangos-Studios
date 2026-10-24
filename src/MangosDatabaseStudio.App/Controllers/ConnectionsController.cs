// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.ConnectionsView)]
    public sealed class ConnectionsController : Controller
    {
        private readonly IConnectionProfileStore _store;
        private readonly IActiveConnectionAccessor _active;
        private readonly IConnectionTester _tester;
        private readonly IAuthorizationService _authorization;
        private readonly ILogger<ConnectionsController> _logger;

        // Initialize the connections controller dependencies and state.
        public ConnectionsController(
            IConnectionProfileStore store,
            IActiveConnectionAccessor active,
            IConnectionTester tester,
            IAuthorizationService authorization,
            ILogger<ConnectionsController> logger)
        {
            _store = store;
            _active = active;
            _tester = tester;
            _authorization = authorization;
            _logger = logger;
        }

        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ConnectionsIndexViewModel model = new()
            {
                Profiles = await _store.GetAllAsync(cancellationToken),
                ActiveId = _active.ActiveConnectionId,
                CanSelect = (await _authorization.AuthorizeAsync(User, AppPermissions.ConnectionsSelect)).Succeeded,
                CanTest = (await _authorization.AuthorizeAsync(User, AppPermissions.ConnectionsTest)).Succeeded,
                CanManage = (await _authorization.AuthorizeAsync(User, AppPermissions.ConnectionsManage)).Succeeded
            };
            return View(model);
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.ConnectionsManage)]
        // Handle the create page or action with the surrounding authorization checks.
        public IActionResult Create()
        {
            return View("Edit", new ConnectionProfileViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsManage)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(ConnectionProfileViewModel model, CancellationToken cancellationToken)
        {
            model.Id = Guid.Empty;
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "A password is required for a new connection.");
            }

            if (!ModelState.IsValid)
            {
                return View("Edit", model);
            }

            try
            {
                ConnectionProfile profile = model.ToDomain();
                await _store.CreateAsync(profile, cancellationToken);
                TempData["Success"] = "Connection profile created.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not create connection profile {ConnectionName}.", model.Name);
                ModelState.AddModelError(string.Empty, "The connection profile could not be created. Review the details and try again.");
                return View("Edit", model);
            }
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.ConnectionsManage)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await _store.GetAsync(id, cancellationToken);
            if (profile == null)
            {
                return NotFound();
            }

            if (_active.ActiveConnectionId == id)
            {
                TempData["Error"] = "Disconnect this server profile before editing its settings.";
                return RedirectToAction("Index");
            }

            return View(ConnectionProfileViewModel.FromDomain(profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsManage)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(Guid id, ConnectionProfileViewModel model, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
            {
                id = model.Id;
            }

            if (id == Guid.Empty || (model.Id != Guid.Empty && model.Id != id))
            {
                ModelState.AddModelError(string.Empty, "The connection profile identifier is invalid. Reload the page and try again.");
                return View(model);
            }

            ConnectionProfile? existing = await _store.GetAsync(id, cancellationToken);
            if (existing == null)
            {
                TempData["Error"] = "That connection profile no longer exists.";
                return RedirectToAction("Index");
            }

            if (_active.ActiveConnectionId == id)
            {
                TempData["Error"] = "Disconnect this server profile before editing its settings.";
                return RedirectToAction("Index");
            }

            model.Id = existing.Id;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                ConnectionProfile profile = model.ToDomain();
                profile.Id = existing.Id;
                profile.CreatedAtUtc = existing.CreatedAtUtc;
                profile.Password = string.IsNullOrEmpty(model.Password) ? existing.Password : model.Password;

                bool updated = await _store.UpdateAsync(profile, cancellationToken);
                if (!updated)
                {
                    ModelState.AddModelError(string.Empty, "The connection profile could not be updated because it was not found. Reload the page and try again.");
                    return View(model);
                }

                ConnectionProfile? saved = await _store.GetAsync(existing.Id, cancellationToken);
                if (saved == null || !MatchesSavedValues(saved, profile))
                {
                    throw new InvalidOperationException("The saved connection profile did not match the requested values after the update.");
                }

                TempData["Success"] = "Connection profile updated.";
                return RedirectToAction("Index");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not update connection profile {ConnectionId}.", existing.Id);
                ModelState.AddModelError(string.Empty, "The connection profile could not be saved. No connection was changed. Review the details and try again.");
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsSelect)]
        // Handle select within the connections controller workflow.
        public async Task<IActionResult> Select(Guid id, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await _store.GetAsync(id, cancellationToken);
            if (profile == null)
            {
                return NotFound();
            }

            _active.ActiveConnectionId = id;
            TempData["Success"] = "Active connection changed to " + profile.Name + ".";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsSelect)]
        // Handle disconnect within the connections controller workflow.
        public async Task<IActionResult> Disconnect(string? returnTo, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await _active.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                TempData["Warning"] = "No server connection is currently active.";
            }
            else
            {
                _active.ActiveConnectionId = null;
                TempData["Success"] = "Disconnected from " + profile.Name + ".";
            }

            if (string.Equals(returnTo, "dashboard", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Home");
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsTest)]
        // Handle test within the connections controller workflow.
        public async Task<IActionResult> Test(Guid id, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await _store.GetAsync(id, cancellationToken);
            if (profile == null)
            {
                return NotFound();
            }

            try
            {
                string version = await _tester.TestAsync(profile, cancellationToken);
                TempData["Success"] = "Connection verified. Server version: " + version;
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not connect to the server: " + exception.Message;
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ConnectionsManage)]
        // Handle the delete page or action with the surrounding authorization checks.
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (_active.ActiveConnectionId == id)
            {
                TempData["Error"] = "Disconnect this server profile before deleting it.";
                return RedirectToAction("Index");
            }

            ConnectionProfile? profile = await _store.GetAsync(id, cancellationToken);
            if (profile == null)
            {
                return NotFound();
            }

            await _store.DeleteAsync(id, cancellationToken);
            TempData["Success"] = "Server profile deleted.";
            return RedirectToAction("Index");
        }

        // Handle matches saved values within the connections controller workflow.
        private static bool MatchesSavedValues(ConnectionProfile saved, ConnectionProfile requested)
        {
            return saved.Id == requested.Id
                && string.Equals(saved.Name, requested.Name, StringComparison.Ordinal)
                && string.Equals(saved.Host, requested.Host, StringComparison.Ordinal)
                && saved.Port == requested.Port
                && string.Equals(saved.Username, requested.Username, StringComparison.Ordinal)
                && string.Equals(saved.Password, requested.Password, StringComparison.Ordinal)
                && string.Equals(saved.DefaultDatabase, requested.DefaultDatabase, StringComparison.Ordinal)
                && string.Equals(saved.SslMode, requested.SslMode, StringComparison.Ordinal);
        }
    }
}
