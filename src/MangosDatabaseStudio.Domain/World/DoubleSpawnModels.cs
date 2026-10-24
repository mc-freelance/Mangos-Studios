// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class DoubleSpawnScopes
    {
        public const string Both = "both";
        public const string Creatures = "creatures";
        public const string GameObjects = "gameobjects";
    }

    public static class DoubleSpawnMatchModes
    {
        public const string SameEntry = "same-entry";
        public const string AnyEntry = "any-entry";
    }

    public static class DoubleSpawnPoolFilters
    {
        public const string All = "all";
        public const string NonPooled = "non-pooled";
        public const string Pooled = "pooled";
    }

    public static class DoubleSpawnLinkedGameObjectFilters
    {
        public const string All = "all";
        public const string NonLinked = "non-linked";
        public const string Linked = "linked";
    }

    public sealed class DoubleSpawnSearchRequest
    {
        public string Scope { get; set; } = DoubleSpawnScopes.Both;
        public string MatchMode { get; set; } = DoubleSpawnMatchModes.SameEntry;
        public string PoolFilter { get; set; } = DoubleSpawnPoolFilters.All;
        public string LinkedGameObjectFilter { get; set; } = DoubleSpawnLinkedGameObjectFilters.All;
        public string Search { get; set; } = string.Empty;
        public int? MapId
        {
            get; set;
        }

        public double DistanceTolerance { get; set; } = 0.1d;
        public bool ExcludeEventRelated { get; set; } = true;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class DoubleSpawnPoolParent
    {
        public long PoolEntry
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public string MembershipDescription { get; set; } = string.Empty;
        public int? MaxLimit
        {
            get; set;
        }

        public string PoolDescription { get; set; } = string.Empty;
    }

    public sealed class DoubleSpawnPoolMembership
    {
        public long PoolEntry
        {
            get; set;
        }

        public string MembershipType { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        public double? Chance
        {
            get; set;
        }

        public string MembershipDescription { get; set; } = string.Empty;
        public int? MaxLimit
        {
            get; set;
        }

        public string PoolDescription { get; set; } = string.Empty;
        public IReadOnlyList<DoubleSpawnPoolParent> ParentPools { get; set; } = new List<DoubleSpawnPoolParent>();
    }

    public sealed class DoubleSpawnPoint
    {
        public string Kind { get; set; } = string.Empty;
        public long Guid
        {
            get; set;
        }

        public long Entry
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;
        public int MapId
        {
            get; set;
        }

        public double PositionX
        {
            get; set;
        }

        public double PositionY
        {
            get; set;
        }

        public double PositionZ
        {
            get; set;
        }

        public bool IsEventRelated
        {
            get; set;
        }

        public int? GameObjectType
        {
            get; set;
        }

        public string GameObjectTypeName { get; set; } = string.Empty;
        public long? LinkedGameObjectEntry
        {
            get; set;
        }

        public string LinkedGameObjectField { get; set; } = string.Empty;
        public IReadOnlyList<DoubleSpawnPoolMembership> Pools { get; set; } = new List<DoubleSpawnPoolMembership>();

        public bool IsPooled
        {
            get
            {
                return Pools.Count > 0;
            }
        }
    }

    public sealed class DoubleSpawnLinkedGameObjectRelation
    {
        public long SourceGuid
        {
            get; set;
        }

        public long SourceEntry
        {
            get; set;
        }

        public int? SourceType
        {
            get; set;
        }

        public string SourceTypeName { get; set; } = string.Empty;
        public string SourceField { get; set; } = string.Empty;
        public long LinkedEntry
        {
            get; set;
        }

        public long TargetGuid
        {
            get; set;
        }

        public long TargetEntry
        {
            get; set;
        }

        public int? TargetType
        {
            get; set;
        }

        public string TargetTypeName { get; set; } = string.Empty;
        public bool IsReciprocal
        {
            get; set;
        }

        public bool TargetIsTrap
        {
            get
            {
                return TargetType == 6;
            }
        }
    }

    public sealed class DoubleSpawnMatch
    {
        public string Kind { get; set; } = string.Empty;
        public DoubleSpawnPoint First { get; set; } = new DoubleSpawnPoint();
        public DoubleSpawnPoint Second { get; set; } = new DoubleSpawnPoint();
        public double Distance
        {
            get; set;
        }

        public DoubleSpawnLinkedGameObjectRelation? LinkedGameObjectRelation
        {
            get; set;
        }

        public bool IsLinkedGameObjectPair
        {
            get
            {
                return LinkedGameObjectRelation != null;
            }
        }
    }

    public sealed class DoubleSpawnSearchResult
    {
        public PagedResult<DoubleSpawnMatch> Matches { get; set; } = new PagedResult<DoubleSpawnMatch>();
        public long CreatureMatchCount
        {
            get; set;
        }

        public long GameObjectMatchCount
        {
            get; set;
        }

        public long PooledMatchCount
        {
            get; set;
        }

        public long LinkedGameObjectMatchCount
        {
            get; set;
        }

        public long CreatureSpawnsScanned
        {
            get; set;
        }

        public long GameObjectSpawnsScanned
        {
            get; set;
        }

        public int MapsScanned
        {
            get; set;
        }

        public bool IsTruncated
        {
            get; set;
        }

        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
    }
}
