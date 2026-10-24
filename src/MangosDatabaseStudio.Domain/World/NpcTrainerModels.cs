// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public sealed class NpcTrainerSummary
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

        public long? TrainerTemplateId
        {
            get; set;
        }

        public long SpellCount
        {
            get; set;
        }
    }

    public sealed class NpcTrainerSpell
    {
        public long SpellId
        {
            get; set;
        }

        public string SpellName { get; set; } = string.Empty;
        public long? SpellCost
        {
            get; set;
        }

        public long? RequiredSpell
        {
            get; set;
        }

        public long? RequiredSkill
        {
            get; set;
        }

        public long? RequiredSkillValue
        {
            get; set;
        }

        public long? RequiredLevel
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        public bool IsTemplateSpell
        {
            get; set;
        }

        public long? TemplateId
        {
            get; set;
        }
    }

    public sealed class NpcTrainerSpellOption
    {
        public long SpellId
        {
            get; set;
        }

        public string SpellName { get; set; } = string.Empty;
    }

    public sealed class NpcTrainerSpellValues
    {
        public long SpellId
        {
            get; set;
        }

        public long? SpellCost
        {
            get; set;
        }

        public long? RequiredSpell
        {
            get; set;
        }

        public long? RequiredSkill
        {
            get; set;
        }

        public long? RequiredSkillValue
        {
            get; set;
        }

        public long? RequiredLevel
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }
    }

    public sealed class NpcTrainerEditorCapabilities
    {
        public string EntryColumn { get; set; } = "entry";
        public string SpellColumn { get; set; } = "spell";
        public string? SpellCostColumn
        {
            get; set;
        }

        public string? RequiredSpellColumn
        {
            get; set;
        }

        public string? RequiredSkillColumn
        {
            get; set;
        }

        public string? RequiredSkillValueColumn
        {
            get; set;
        }

        public string? RequiredLevelColumn
        {
            get; set;
        }

        public string? ConditionIdColumn
        {
            get; set;
        }

        public IReadOnlyList<string> UpdateColumns { get; set; } = System.Array.Empty<string>();
        public IReadOnlyList<string> KeyColumns { get; set; } = System.Array.Empty<string>();
        public bool HasSpellCost => !string.IsNullOrWhiteSpace(SpellCostColumn);
        public bool HasRequiredSpell => !string.IsNullOrWhiteSpace(RequiredSpellColumn);
        public bool HasRequiredSkill => !string.IsNullOrWhiteSpace(RequiredSkillColumn);
        public bool HasRequiredSkillValue => !string.IsNullOrWhiteSpace(RequiredSkillValueColumn);
        public bool HasRequiredLevel => !string.IsNullOrWhiteSpace(RequiredLevelColumn);
        public bool HasConditionId => !string.IsNullOrWhiteSpace(ConditionIdColumn);
    }

    public sealed class NpcTrainerMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
    }

    public sealed class NpcTrainerDetail
    {
        public NpcTrainerSummary Trainer { get; set; } = new NpcTrainerSummary();
        public PagedResult<NpcTrainerSpell> Spells { get; set; } = new PagedResult<NpcTrainerSpell>();
    }
}
