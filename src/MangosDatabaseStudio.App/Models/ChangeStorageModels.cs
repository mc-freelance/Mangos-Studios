// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.App.Services;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class ChangeStorageViewModel
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public ChangeStoragePage Page { get; set; } = new ChangeStoragePage();
        public ChangeStorageCounts Counts { get; set; } = new ChangeStorageCounts();
        public IReadOnlyList<string> Databases { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Sources { get; set; } = Array.Empty<string>();
        public bool CanReview
        {
            get; set;
        }

        public bool CanBuildContribution
        {
            get; set;
        }
    }

    public sealed class ChangeStorageReviewPostModel
    {
        public List<Guid> Ids { get; set; } = new List<Guid>();
        public string Status { get; set; } = StoredChangeStatuses.Pending;
        public string Database { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class ChangeStorageContributionPostModel
    {
        public List<Guid> Ids { get; set; } = new List<Guid>();
        public string Database { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class ChangeStorageContributionViewModel
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public IReadOnlyList<StoredDatabaseChange> Changes { get; set; } = Array.Empty<StoredDatabaseChange>();
        public string ReturnDatabase { get; set; } = string.Empty;
        public string ReturnStatus { get; set; } = string.Empty;
        public string ReturnSource { get; set; } = string.Empty;
        public string ReturnSearch { get; set; } = string.Empty;
        public int ReturnPage { get; set; } = 1;
        public int ReturnPageSize { get; set; } = 20;
    }

    public sealed class ChangeStorageDeletePostModel
    {
        public List<Guid> Ids { get; set; } = new List<Guid>();
        public string Database { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

}
