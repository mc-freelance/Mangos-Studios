// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class PoolMemberSources
    {
        public const string CreatureSpawn = "pool_creature";
        public const string CreatureTemplate = "pool_creature_template";
        public const string GameObjectSpawn = "pool_gameobject";
        public const string GameObjectTemplate = "pool_gameobject_template";
        public const string ChildPool = "pool_pool";
    }


    public static class PoolPopulationFilters
    {
        public const string All = "all";
        public const string Populated = "populated";
        public const string Empty = "empty";

        // Normalize a stored value for stable comparisons and display.
        public static string Normalize(string? value)
        {
            if (string.Equals(value, Populated, StringComparison.OrdinalIgnoreCase))
            {
                return Populated;
            }

            if (string.Equals(value, Empty, StringComparison.OrdinalIgnoreCase))
            {
                return Empty;
            }

            return All;
        }

        // Read label according to the available model or service contract.
        public static string GetLabel(string? value)
        {
            return Normalize(value) switch
            {
                Populated => "Populated pools",
                Empty => "Empty pools",
                _ => "All pools"
            };
        }
    }

    public sealed class PoolSummary
    {
        public long Entry
        {
            get; set;
        }

        public int? MaxLimit
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
        public long CreatureSpawnCount
        {
            get; set;
        }

        public long CreatureTemplateCount
        {
            get; set;
        }

        public long GameObjectSpawnCount
        {
            get; set;
        }

        public long GameObjectTemplateCount
        {
            get; set;
        }

        public long ChildPoolCount
        {
            get; set;
        }

        public long TotalMemberCount => CreatureSpawnCount + CreatureTemplateCount + GameObjectSpawnCount + GameObjectTemplateCount + ChildPoolCount;
    }

    public sealed class PoolMember
    {
        public string Source { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public long MemberId
        {
            get; set;
        }

        public long? EntityEntry
        {
            get; set;
        }

        public string ResolvedName { get; set; } = string.Empty;
        public bool IsResolved
        {
            get; set;
        }

        public string DisplayName { get; set; } = string.Empty;
        public double? Chance
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolParentMembership
    {
        public long PoolEntry
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
        public double? Chance
        {
            get; set;
        }

        public string MembershipDescription { get; set; } = string.Empty;
    }

    public sealed class PoolDetail
    {
        public PoolSummary Pool { get; set; } = new PoolSummary();
        public PagedResult<PoolMember> Members { get; set; } = new PagedResult<PoolMember>();
        public IReadOnlyList<PoolParentMembership> ParentPools { get; set; } = Array.Empty<PoolParentMembership>();
    }

    public sealed class PoolMemberSourceCapability
    {
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string MemberLabel { get; set; } = string.Empty;
        public bool HasChance
        {
            get; set;
        }

        public bool HasDescription
        {
            get; set;
        }
    }

    public sealed class PoolEditorCapabilities
    {
        public string EntryColumn { get; set; } = "entry";
        public string? MaxLimitColumn
        {
            get; set;
        }

        public string? DescriptionColumn
        {
            get; set;
        }

        public IReadOnlyList<PoolMemberSourceCapability> MemberSources { get; set; } = Array.Empty<PoolMemberSourceCapability>();
        public bool HasMaxLimit => !string.IsNullOrWhiteSpace(MaxLimitColumn);
        public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionColumn);
    }

    public sealed class PoolTemplateValues
    {
        public long Entry
        {
            get; set;
        }

        public int? MaxLimit
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolMemberValues
    {
        public string Source { get; set; } = string.Empty;
        public long MemberId
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string ObjectName { get; set; } = string.Empty;
        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
    }
}
