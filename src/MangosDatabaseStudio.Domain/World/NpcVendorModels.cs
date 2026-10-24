// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public sealed class NpcVendorSummary
    {
        public long Entry
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;
        public string SubName { get; set; } = string.Empty;
        public ulong NpcFlags
        {
            get; set;
        }

        public long ItemCount
        {
            get; set;
        }
    }

    public sealed class NpcVendorItem
    {
        public long ItemId
        {
            get; set;
        }

        public string ItemName { get; set; } = string.Empty;
        public long? Slot
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? IncrementTime
        {
            get; set;
        }

        public long? ExtendedCost
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class NpcVendorItemOption
    {
        public long ItemId
        {
            get; set;
        }

        public string ItemName { get; set; } = string.Empty;
    }

    public sealed class NpcVendorItemValues
    {
        public long ItemId
        {
            get; set;
        }

        public long? Slot
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? IncrementTime
        {
            get; set;
        }

        public long? ExtendedCost
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class NpcVendorEditorCapabilities
    {
        public string EntryColumn { get; set; } = "entry";
        public string ItemColumn { get; set; } = "item";
        public string? SlotColumn
        {
            get; set;
        }

        public string? MaxCountColumn
        {
            get; set;
        }

        public string? IncrementTimeColumn
        {
            get; set;
        }

        public string? ExtendedCostColumn
        {
            get; set;
        }

        public string? ConditionIdColumn
        {
            get; set;
        }

        public IReadOnlyList<string> UpdateColumns { get; set; } = System.Array.Empty<string>();
        public IReadOnlyList<string> KeyColumns { get; set; } = System.Array.Empty<string>();
        public bool HasSlot
        {
            get
            {
                return !string.IsNullOrWhiteSpace(SlotColumn);
            }
        }

        public bool HasMaxCount
        {
            get
            {
                return !string.IsNullOrWhiteSpace(MaxCountColumn);
            }
        }

        public bool HasIncrementTime
        {
            get
            {
                return !string.IsNullOrWhiteSpace(IncrementTimeColumn);
            }
        }

        public bool HasExtendedCost
        {
            get
            {
                return !string.IsNullOrWhiteSpace(ExtendedCostColumn);
            }
        }

        public bool HasConditionId
        {
            get
            {
                return !string.IsNullOrWhiteSpace(ConditionIdColumn);
            }
        }
    }

    public sealed class NpcVendorMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
    }

    public sealed class NpcVendorDetail
    {
        public NpcVendorSummary Vendor { get; set; } = new NpcVendorSummary();
        public PagedResult<NpcVendorItem> Items { get; set; } = new PagedResult<NpcVendorItem>();
    }
}
