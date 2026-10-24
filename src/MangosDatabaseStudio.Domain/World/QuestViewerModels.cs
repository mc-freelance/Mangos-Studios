// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class QuestRelationKinds
    {
        public const string CreatureStarter = "creature-starter";
        public const string CreatureEnder = "creature-ender";
        public const string GameObjectStarter = "gameobject-starter";
        public const string GameObjectEnder = "gameobject-ender";

        // Normalize a stored value for stable comparisons and display.
        public static string Normalize(string? value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                CreatureStarter => CreatureStarter,
                CreatureEnder => CreatureEnder,
                GameObjectStarter => GameObjectStarter,
                GameObjectEnder => GameObjectEnder,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Unknown quest relationship type.")
            };
        }

        // Return a readable label for the current entity type.
        public static string EntityLabel(string kind)
        {
            kind = Normalize(kind);
            return kind == CreatureStarter || kind == CreatureEnder ? "Creature" : "GameObject";
        }

        // Return the display label for a relationship role.
        public static string RoleLabel(string kind)
        {
            kind = Normalize(kind);
            return kind == CreatureStarter || kind == GameObjectStarter ? "Starts quest" : "Ends quest";
        }
    }

    public sealed class QuestSummary
    {
        public long Entry
        {
            get; set;
        }

        public string Title { get; set; } = string.Empty;
        public long? MinLevel
        {
            get; set;
        }

        public long? QuestLevel
        {
            get; set;
        }

        public long PrevQuestId
        {
            get; set;
        }

        public long NextQuestId
        {
            get; set;
        }

        public long ExclusiveGroup
        {
            get; set;
        }

        public long NextQuestInChain
        {
            get; set;
        }

        public long CreatureStarterCount
        {
            get; set;
        }

        public long CreatureEnderCount
        {
            get; set;
        }

        public long GameObjectStarterCount
        {
            get; set;
        }

        public long GameObjectEnderCount
        {
            get; set;
        }

        public long StarterCount => CreatureStarterCount + GameObjectStarterCount;
        public long EnderCount => CreatureEnderCount + GameObjectEnderCount;
        public long TotalRelationCount => StarterCount + EnderCount;
    }

    public sealed class QuestRelationRow
    {
        public string Kind { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string RoleLabel { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }

        public string EntityName { get; set; } = string.Empty;
        public string StorageTable { get; set; } = string.Empty;
        public bool IsResolved
        {
            get; set;
        }
    }

    public sealed class QuestChainLink
    {
        public long Entry
        {
            get; set;
        }

        public string Title { get; set; } = string.Empty;
        public string RelationLabel { get; set; } = string.Empty;
        public string RelationCode { get; set; } = string.Empty;
        public long RawValue
        {
            get; set;
        }

        public bool Exists
        {
            get; set;
        }

        public bool IsIncoming
        {
            get; set;
        }
    }

    public sealed class QuestDetail
    {
        public QuestSummary Quest { get; set; } = new QuestSummary();
        public string Details { get; set; } = string.Empty;
        public string Objectives { get; set; } = string.Empty;
        public PagedResult<QuestRelationRow> Relations { get; set; } = new PagedResult<QuestRelationRow>();
        public IReadOnlyList<QuestChainLink> ChainLinks { get; set; } = Array.Empty<QuestChainLink>();
    }

    public sealed class QuestRelationCapability
    {
        public string Kind { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string StorageTable { get; set; } = string.Empty;
    }

    public sealed class QuestEditorCapabilities
    {
        public string EntryColumn { get; set; } = "entry";
        public string? TitleColumn
        {
            get; set;
        }

        public string? DetailsColumn
        {
            get; set;
        }

        public string? ObjectivesColumn
        {
            get; set;
        }

        public string? MinLevelColumn
        {
            get; set;
        }

        public string? QuestLevelColumn
        {
            get; set;
        }

        public string? PrevQuestIdColumn
        {
            get; set;
        }

        public string? NextQuestIdColumn
        {
            get; set;
        }

        public string? ExclusiveGroupColumn
        {
            get; set;
        }

        public string? NextQuestInChainColumn
        {
            get; set;
        }

        public string RelationStorageLabel { get; set; } = "None";
        public IReadOnlyList<QuestRelationCapability> RelationKinds { get; set; } = Array.Empty<QuestRelationCapability>();
        public bool CanMutateQuests
        {
            get; set;
        }

        public bool CanCreateQuests
        {
            get; set;
        }

        public bool HasTitle => !string.IsNullOrWhiteSpace(TitleColumn);
        public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsColumn);
        public bool HasObjectives => !string.IsNullOrWhiteSpace(ObjectivesColumn);
        public bool HasMinLevel => !string.IsNullOrWhiteSpace(MinLevelColumn);
        public bool HasQuestLevel => !string.IsNullOrWhiteSpace(QuestLevelColumn);
        public bool HasPrevQuestId => !string.IsNullOrWhiteSpace(PrevQuestIdColumn);
        public bool HasNextQuestId => !string.IsNullOrWhiteSpace(NextQuestIdColumn);
        public bool HasExclusiveGroup => !string.IsNullOrWhiteSpace(ExclusiveGroupColumn);
        public bool HasNextQuestInChain => !string.IsNullOrWhiteSpace(NextQuestInChainColumn);
    }

    public sealed class QuestTemplateValues
    {
        public long Entry
        {
            get; set;
        }

        public string Title { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Objectives { get; set; } = string.Empty;
        public long? MinLevel
        {
            get; set;
        }

        public long? QuestLevel
        {
            get; set;
        }

        public long? PrevQuestId
        {
            get; set;
        }

        public long? NextQuestId
        {
            get; set;
        }

        public long? ExclusiveGroup
        {
            get; set;
        }

        public long? NextQuestInChain
        {
            get; set;
        }
    }

    public sealed class QuestRelationValues
    {
        public string Kind { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }
    }

    public sealed class QuestMutationResult
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
