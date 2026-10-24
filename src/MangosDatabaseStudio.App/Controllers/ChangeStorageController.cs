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
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.ChangeStorageView)]
    public sealed class ChangeStorageController : StudioController
    {
        private readonly IChangeStorageStore _changes;
        private readonly IAuthorizationService _authorization;
        private readonly IChangeRevertExecutionService _revertExecution;

        // Initialize the change storage controller dependencies and state.
        public ChangeStorageController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IChangeStorageStore changes,
            IAuthorizationService authorization,
            IChangeRevertExecutionService revertExecution)
            : base(activeConnectionAccessor)
        {
            _changes = changes;
            _authorization = authorization;
            _revertExecution = revertExecution;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(
            string? database = null,
            string? status = null,
            string? source = null,
            string? search = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            string selectedDatabase = (database ?? string.Empty).Trim();
            bool canReview = (await _authorization.AuthorizeAsync(User, AppPermissions.ChangeStorageReview)).Succeeded;
            bool canBuildContribution = (await _authorization.AuthorizeAsync(User, AppPermissions.ChangeStorageContribution)).Succeeded;

            ChangeStoragePage result = await _changes.SearchAsync(new ChangeStorageQuery
            {
                ConnectionId = profile.Id,
                DatabaseName = selectedDatabase,
                Status = status ?? string.Empty,
                Source = source ?? string.Empty,
                Search = search ?? string.Empty,
                Page = page,
                PageSize = pageSize
            }, cancellationToken);

            return View(new ChangeStorageViewModel
            {
                ConnectionName = profile.Name,
                Database = selectedDatabase,
                Status = StoredChangeStatuses.IsValid(status) ? StoredChangeStatuses.Normalize(status) : string.Empty,
                Source = (source ?? string.Empty).Trim(),
                Search = (search ?? string.Empty).Trim(),
                Page = result,
                Counts = await _changes.GetCountsAsync(profile.Id, selectedDatabase, cancellationToken),
                Databases = await _changes.GetDatabaseNamesAsync(profile.Id, cancellationToken),
                Sources = await _changes.GetSourcesAsync(profile.Id, cancellationToken),
                CanReview = canReview,
                CanBuildContribution = canBuildContribution
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ChangeStorageReview)]
        // Handle review within the change storage controller workflow.
        public async Task<IActionResult> Review(ChangeStorageReviewPostModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (!StoredChangeStatuses.IsValid(model.Status))
            {
                TempData["Error"] = "Choose a valid review status.";
            }
            else if (model.Ids == null || model.Ids.Count == 0)
            {
                TempData["Warning"] = "Select at least one stored change first.";
            }
            else
            {
                Guid userId = GetCurrentUserId();
                await _changes.UpdateStatusAsync(
                    profile.Id,
                    model.Ids,
                    model.Status,
                    userId,
                    User.Identity?.Name ?? string.Empty,
                    cancellationToken);

                string label = StoredChangeStatuses.Normalize(model.Status) switch
                {
                    StoredChangeStatuses.Approved => "approved",
                    StoredChangeStatuses.Ignored => "ignored",
                    _ => "returned to pending"
                };
                TempData["Success"] = model.Ids.Count == 1
                    ? "Stored change " + label + "."
                    : model.Ids.Count + " stored changes " + label + ".";
            }

            return RedirectToAction(nameof(Index), new
            {
                database = model.Database,
                status = model.CurrentStatus,
                source = model.Source,
                search = model.Search,
                page = Math.Max(1, model.Page),
                pageSize = Math.Clamp(model.PageSize, 10, 100)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ChangeStorageContribution)]
        // Generate contribution as part of the authorized desktop request.
        public IActionResult GenerateContribution(ChangeStorageContributionPostModel model)
        {
            Guid[] requestedIds = (model.Ids ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();

            if (requestedIds.Length == 0)
            {
                TempData["Warning"] = "Select at least one approved reusable change before generating contribution SQL.";
                return RedirectToIndex(model);
            }

            return RedirectToAction(nameof(Contribution), new
            {
                ids = string.Join(",", requestedIds.Select(id => id.ToString("D"))),
                returnDatabase = model.Database,
                returnStatus = model.CurrentStatus,
                returnSource = model.Source,
                returnSearch = model.Search,
                returnPage = Math.Max(1, model.Page),
                returnPageSize = Math.Clamp(model.PageSize, 10, 100)
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.ChangeStorageContribution)]
        // Handle contribution within the change storage controller workflow.
        public async Task<IActionResult> Contribution(
            string? ids = null,
            string? returnDatabase = null,
            string? returnStatus = null,
            string? returnSource = null,
            string? returnSearch = null,
            int returnPage = 1,
            int returnPageSize = 20,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            Guid[] requestedIds = (ids ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out Guid parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(100)
                .ToArray();

            if (requestedIds.Length == 0)
            {
                TempData["Warning"] = "Select at least one approved reusable change before generating contribution SQL.";
                return RedirectToAction(nameof(Index), new
                {
                    database = returnDatabase,
                    status = returnStatus,
                    source = returnSource,
                    search = returnSearch,
                    page = Math.Max(1, returnPage),
                    pageSize = Math.Clamp(returnPageSize, 10, 100)
                });
            }

            IReadOnlyList<StoredDatabaseChange> selected = await _changes.GetByIdsAsync(
                profile.Id,
                requestedIds,
                cancellationToken);

            if (selected.Count != requestedIds.Length)
            {
                TempData["Warning"] = "One or more selected Change Storage records no longer exist. Refresh the page and select them again.";
                return RedirectToAction(nameof(Index), new
                {
                    database = returnDatabase,
                    status = returnStatus,
                    source = returnSource,
                    search = returnSearch,
                    page = Math.Max(1, returnPage),
                    pageSize = Math.Clamp(returnPageSize, 10, 100)
                });
            }

            StoredDatabaseChange? unavailable = selected.FirstOrDefault(change =>
                !string.Equals(
                    StoredChangeStatuses.Normalize(change.ReviewStatus),
                    StoredChangeStatuses.Approved,
                    StringComparison.OrdinalIgnoreCase) ||
                !change.ContributionEligible ||
                string.IsNullOrWhiteSpace(change.SqlText));

            if (unavailable != null)
            {
                TempData["Warning"] =
                    "Contribution SQL can only be generated from approved changes marked as reusable SQL. " +
                    "Review the selected records and try again.";
                return RedirectToAction(nameof(Index), new
                {
                    database = returnDatabase,
                    status = returnStatus,
                    source = returnSource,
                    search = returnSearch,
                    page = Math.Max(1, returnPage),
                    pageSize = Math.Clamp(returnPageSize, 10, 100)
                });
            }

            string[] databases = selected
                .Select(change => change.DatabaseName.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (databases.Length != 1)
            {
                TempData["Warning"] = "Select approved changes from one database at a time when generating contribution SQL.";
                return RedirectToAction(nameof(Index), new
                {
                    database = returnDatabase,
                    status = returnStatus,
                    source = returnSource,
                    search = returnSearch,
                    page = Math.Max(1, returnPage),
                    pageSize = Math.Clamp(returnPageSize, 10, 100)
                });
            }

            StoredDatabaseChange[] ordered = selected
                .OrderBy(change => change.CreatedAtUtc)
                .ThenBy(change => change.Id)
                .ToArray();

            return View(new ChangeStorageContributionViewModel
            {
                ConnectionName = profile.Name,
                Database = databases[0],
                Changes = ordered,
                ReturnDatabase = (returnDatabase ?? string.Empty).Trim(),
                ReturnStatus = (returnStatus ?? string.Empty).Trim(),
                ReturnSource = (returnSource ?? string.Empty).Trim(),
                ReturnSearch = (returnSearch ?? string.Empty).Trim(),
                ReturnPage = Math.Max(1, returnPage),
                ReturnPageSize = Math.Clamp(returnPageSize, 10, 100)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.ChangeStorageReview)]
        // Handle the delete page or action with the surrounding authorization checks.
        public async Task<IActionResult> Delete(ChangeStorageDeletePostModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (model.Ids == null || model.Ids.Count == 0)
            {
                TempData["Warning"] = "Select at least one stored change first.";
                return RedirectToIndex(model);
            }

            IReadOnlyList<StoredDatabaseChange> selected = await _changes.GetByIdsAsync(
                profile.Id,
                model.Ids,
                cancellationToken);

            if (selected.Count == 0)
            {
                TempData["Warning"] = "The selected Change Storage records no longer exist.";
                return RedirectToIndex(model);
            }

            int deleted = 0;
            int reverted = 0;
            string? failure = null;

            StoredDatabaseChange[] ordered = selected
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToArray();
            StoredDatabaseChange[] pendingChanges = ordered
                .Where(change => string.Equals(
                    StoredChangeStatuses.Normalize(change.ReviewStatus),
                    StoredChangeStatuses.Pending,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            StoredDatabaseChange? unavailable = pendingChanges.FirstOrDefault(change => !change.RevertAvailable);
            if (unavailable != null)
            {
                TempData["Warning"] =
                    "The pending change '" + unavailable.Summary + "' cannot be deleted automatically because rollback data is unavailable. " +
                    "Approve or ignore it first if you only want to remove the history record.";
                return RedirectToIndex(model);
            }

            foreach (StoredDatabaseChange change in pendingChanges)
            {
                try
                {
                    await _revertExecution.ExecuteAsync(
                        profile,
                        change.DatabaseName,
                        change.RevertSqlText,
                        change.RevertExpectedRows,
                        cancellationToken);
                    await _changes.DeleteAsync(profile.Id, change.Id, cancellationToken);
                    reverted++;
                    deleted++;
                }
                catch (Exception exception)
                {
                    failure = "Revert stopped at '" + change.Summary + "': " + exception.Message;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(failure))
            {
                foreach (StoredDatabaseChange change in ordered.Where(change =>
                             !string.Equals(
                                 StoredChangeStatuses.Normalize(change.ReviewStatus),
                                 StoredChangeStatuses.Pending,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    await _changes.DeleteAsync(profile.Id, change.Id, cancellationToken);
                    deleted++;
                }
            }

            if (!string.IsNullOrWhiteSpace(failure))
            {
                TempData["Error"] = failure;
            }
            else
            {
                string message = deleted == 1
                    ? "1 Change Storage record deleted."
                    : deleted + " Change Storage records deleted.";
                if (reverted > 0)
                {
                    message += reverted == 1
                        ? " The pending database change was reverted first."
                        : " " + reverted + " pending database changes were reverted first.";
                }

                TempData["Success"] = message;
            }

            return RedirectToIndex(model);
        }

        // Handle redirect to index within the change storage controller workflow.
        private RedirectToActionResult RedirectToIndex(ChangeStorageContributionPostModel model)
        {
            return RedirectToAction(nameof(Index), new
            {
                database = model.Database,
                status = model.CurrentStatus,
                source = model.Source,
                search = model.Search,
                page = Math.Max(1, model.Page),
                pageSize = Math.Clamp(model.PageSize, 10, 100)
            });
        }

        // Handle redirect to index within the change storage controller workflow.
        private RedirectToActionResult RedirectToIndex(ChangeStorageDeletePostModel model)
        {
            return RedirectToAction(nameof(Index), new
            {
                database = model.Database,
                status = model.CurrentStatus,
                source = model.Source,
                search = model.Search,
                page = Math.Max(1, model.Page),
                pageSize = Math.Clamp(model.PageSize, 10, 100)
            });
        }

        // Read current user id as part of the authorized desktop request.
        private Guid GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }
    }
}
