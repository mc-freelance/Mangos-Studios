// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.Domain.DatabaseUpdates;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class DatabaseUpdatesViewModel
    {
        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();

        [Required]
        public string Database { get; set; } = string.Empty;

        public string ConnectionName { get; set; } = string.Empty;
        public string SourceDirectory { get; set; } = string.Empty;
        public MangosDatabaseUpdatePlan? Plan
        {
            get; set;
        }

        public bool CanScan
        {
            get; set;
        }

        public bool CanExecute
        {
            get; set;
        }

        public bool CanConfigure
        {
            get; set;
        }
    }

    public sealed class DatabaseUpdateSourceViewModel
    {
        [Required]
        public string Database { get; set; } = string.Empty;

        [StringLength(2048)]
        public string SourceDirectory { get; set; } = string.Empty;
    }

    public sealed class DatabaseUpdateExecuteViewModel
    {
        [Required]
        public string Database { get; set; } = string.Empty;

        public bool Confirm
        {
            get; set;
        }
    }

    public sealed class DatabaseUpdateResultViewModel
    {
        public string Database { get; set; } = string.Empty;
        public MangosDatabaseUpdateBatchResult Result { get; set; } = new MangosDatabaseUpdateBatchResult();
    }
}
