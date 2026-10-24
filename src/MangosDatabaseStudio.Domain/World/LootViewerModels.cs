// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class LootViewerModes
    {
        public const string Templates = "templates";
        public const string Sources = "sources";

        // Normalize a stored value for stable comparisons and display.
        public static string Normalize(string? value) =>
            string.Equals(value, Sources, StringComparison.OrdinalIgnoreCase) ? Sources : Templates;
    }

    public static class LootSourceFilters
    {
        public const string All = "all";
        public const string Creatures = "creatures";
        public const string GameObjects = "gameobjects";

        // Normalize a stored value for stable comparisons and display.
        public static string Normalize(string? value)
        {
            if (string.Equals(value, Creatures, StringComparison.OrdinalIgnoreCase))
            {
                return Creatures;
            }

            if (string.Equals(value, GameObjects, StringComparison.OrdinalIgnoreCase))
            {
                return GameObjects;
            }

            return All;
        }
    }

    public sealed class LootTableDescriptor
    {
        public string TableName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public long ApproximateRows
        {
            get; set;
        }
    }

    public sealed class LootTemplateSummary
    {
        public string TableName { get; set; } = string.Empty;
        public string TableLabel { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public long RowCount
        {
            get; set;
        }

        public long DirectItemCount
        {
            get; set;
        }

        public long ReferenceCount
        {
            get; set;
        }
    }

    public sealed class LootItemRow
    {
        public long Entry
        {
            get; set;
        }

        public long ItemId
        {
            get; set;
        }

        public string ItemName { get; set; } = string.Empty;
        public bool IsItemResolved
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public long? GroupId
        {
            get; set;
        }

        public long? MinCountOrReference
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        public bool IsReference => MinCountOrReference < 0;
        public long? ReferenceEntry => IsReference ? Math.Abs(MinCountOrReference!.Value) : null;
        public bool ReferenceExists
        {
            get; set;
        }
    }

    public sealed class LootSourceSummary
    {
        public string EntityType { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }

        public string EntityName { get; set; } = string.Empty;
        public string LootKind { get; set; } = string.Empty;
        public string LootTableName { get; set; } = string.Empty;
        public string LootTableLabel { get; set; } = string.Empty;
        public long LootEntry
        {
            get; set;
        }

        public bool TargetTableAvailable
        {
            get; set;
        }

        public bool TargetExists
        {
            get; set;
        }

        public long LootRowCount
        {
            get; set;
        }
    }


    public sealed class LootEditorCapabilities
    {
        public string TableName { get; set; } = string.Empty;
        public string TableLabel { get; set; } = string.Empty;
        public bool CanMutate
        {
            get; set;
        }

        public bool HasItem
        {
            get; set;
        }

        public bool HasChance
        {
            get; set;
        }

        public bool HasGroupId
        {
            get; set;
        }

        public bool HasMinCountOrReference
        {
            get; set;
        }

        public bool SupportsReferences
        {
            get; set;
        }

        public bool HasMaxCount
        {
            get; set;
        }

        public bool HasConditionId
        {
            get; set;
        }
    }

    public sealed class LootRowValues
    {
        public long Entry
        {
            get; set;
        }

        public long ItemId
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public long? GroupId
        {
            get; set;
        }

        public long? MinCountOrReference
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class LootMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string ObjectName { get; set; } = string.Empty;
        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
        public long RemainingRowsInDefinition
        {
            get; set;
        }
    }

    public sealed class LootTemplateDetail
    {
        public LootTemplateSummary Template { get; set; } = new LootTemplateSummary();
        public PagedResult<LootItemRow> Rows { get; set; } = new PagedResult<LootItemRow>();
        public IReadOnlyList<LootSourceSummary> Sources { get; set; } = Array.Empty<LootSourceSummary>();
        public long TotalSourceCount
        {
            get; set;
        }
    }
}
