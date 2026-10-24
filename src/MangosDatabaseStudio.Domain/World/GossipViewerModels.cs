// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class GossipSourceFilters
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

        // Read label according to the available model or service contract.
        public static string GetLabel(string? value) => Normalize(value) switch
        {
            Creatures => "Creature sources",
            GameObjects => "GameObject sources",
            _ => "All gossip sources"
        };
    }

    public sealed class GossipMenuSummary
    {
        public long Entry
        {
            get; set;
        }

        public long TextRowCount
        {
            get; set;
        }

        public long OptionCount
        {
            get; set;
        }

        public long SourceCount
        {
            get; set;
        }

        public long ParentMenuCount
        {
            get; set;
        }

        public long TotalDefinitionRows => TextRowCount + OptionCount;
    }

    public sealed class GossipMenuTextRow
    {
        public long Entry
        {
            get; set;
        }

        public long TextId
        {
            get; set;
        }

        public string TextPreview { get; set; } = string.Empty;
        public bool IsTextResolved
        {
            get; set;
        }

        public long? ScriptId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class GossipMenuOptionRow
    {
        public long MenuId
        {
            get; set;
        }

        public long Id
        {
            get; set;
        }

        public long? OptionIcon
        {
            get; set;
        }

        public string OptionText { get; set; } = string.Empty;
        public long? OptionBroadcastTextId
        {
            get; set;
        }

        public long? OptionId
        {
            get; set;
        }

        public long? NpcOptionNpcFlag
        {
            get; set;
        }

        public long? ActionMenuId
        {
            get; set;
        }

        public bool ActionMenuExists
        {
            get; set;
        }

        public long? ActionPoiId
        {
            get; set;
        }

        public long? ActionScriptId
        {
            get; set;
        }

        public bool? BoxCoded
        {
            get; set;
        }

        public long? BoxMoney
        {
            get; set;
        }

        public string BoxText { get; set; } = string.Empty;
        public long? BoxBroadcastTextId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class GossipSourceSummary
    {
        public string EntityType { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }

        public string EntityName { get; set; } = string.Empty;
        public long MenuId
        {
            get; set;
        }

        public string LinkKind { get; set; } = string.Empty;
        public bool MenuExists
        {
            get; set;
        }

        public long TextRowCount
        {
            get; set;
        }

        public long OptionCount
        {
            get; set;
        }
    }

    public sealed class GossipCreatureOption
    {
        public long Entry
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;
        public long GossipMenuId
        {
            get; set;
        }

        public ulong NpcFlags
        {
            get; set;
        }
    }

    public sealed class GossipParentMenuLink
    {
        public long MenuId
        {
            get; set;
        }

        public long OptionId
        {
            get; set;
        }

        public string OptionText { get; set; } = string.Empty;
    }

    public sealed class GossipMenuDetail
    {
        public GossipMenuSummary Menu { get; set; } = new GossipMenuSummary();
        public PagedResult<GossipMenuTextRow> TextRows { get; set; } = new PagedResult<GossipMenuTextRow>();
        public PagedResult<GossipMenuOptionRow> Options { get; set; } = new PagedResult<GossipMenuOptionRow>();
        public IReadOnlyList<GossipSourceSummary> Sources { get; set; } = Array.Empty<GossipSourceSummary>();
        public long TotalSourceCount
        {
            get; set;
        }

        public IReadOnlyList<GossipParentMenuLink> ParentMenus { get; set; } = Array.Empty<GossipParentMenuLink>();
        public long TotalParentMenuCount
        {
            get; set;
        }
    }

    public sealed class GossipEditorCapabilities
    {
        public bool CanMutateMenuRows
        {
            get; set;
        }

        public bool CanMutateOptions
        {
            get; set;
        }

        public bool HasMenuScriptId
        {
            get; set;
        }

        public bool HasMenuConditionId
        {
            get; set;
        }

        public bool HasOptionIcon
        {
            get; set;
        }

        public bool HasOptionText
        {
            get; set;
        }

        public bool HasOptionBroadcastTextId
        {
            get; set;
        }

        public bool HasOptionId
        {
            get; set;
        }

        public bool HasNpcOptionNpcFlag
        {
            get; set;
        }

        public bool HasActionMenuId
        {
            get; set;
        }

        public bool HasActionPoiId
        {
            get; set;
        }

        public bool HasActionScriptId
        {
            get; set;
        }

        public bool HasBoxCoded
        {
            get; set;
        }

        public bool HasBoxMoney
        {
            get; set;
        }

        public bool HasBoxText
        {
            get; set;
        }

        public bool HasBoxBroadcastTextId
        {
            get; set;
        }

        public bool HasOptionConditionId
        {
            get; set;
        }
    }

    public sealed class GossipMenuRowValues
    {
        public long Entry
        {
            get; set;
        }

        public long TextId
        {
            get; set;
        }

        public long? ScriptId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class GossipOptionValues
    {
        public long MenuId
        {
            get; set;
        }

        public long Id
        {
            get; set;
        }

        public long? OptionIcon
        {
            get; set;
        }

        public string OptionText { get; set; } = string.Empty;
        public long? OptionBroadcastTextId
        {
            get; set;
        }

        public long? OptionId
        {
            get; set;
        }

        public long? NpcOptionNpcFlag
        {
            get; set;
        }

        public long? ActionMenuId
        {
            get; set;
        }

        public long? ActionPoiId
        {
            get; set;
        }

        public long? ActionScriptId
        {
            get; set;
        }

        public bool? BoxCoded
        {
            get; set;
        }

        public long? BoxMoney
        {
            get; set;
        }

        public string BoxText { get; set; } = string.Empty;
        public long? BoxBroadcastTextId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class GossipMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string ObjectName { get; set; } = string.Empty;
        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
        public long RemainingMenuRows
        {
            get; set;
        }

        public long RemainingOptions
        {
            get; set;
        }

        public bool DefinitionExists => RemainingMenuRows + RemainingOptions > 0;
    }
}
