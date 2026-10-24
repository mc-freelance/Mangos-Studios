// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System.Security.Claims;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize]
    [Route("operation-progress")]
    public sealed class OperationProgressController : Controller
    {
        private readonly OperationProgressStore _store;

        // Initialize the operation progress controller dependencies and state.
        public OperationProgressController(OperationProgressStore store)
        {
            _store = store;
        }

        [HttpGet("status")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        // Handle status within the operation progress controller workflow.
        public IActionResult Status(string id)
        {
            string owner = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (!_store.TryGet(owner, id ?? string.Empty, out OperationProgressSnapshot snapshot))
            {
                return NotFound();
            }

            return Json(snapshot);
        }
    }
}
