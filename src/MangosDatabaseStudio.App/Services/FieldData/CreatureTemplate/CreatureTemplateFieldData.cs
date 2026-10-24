// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class CreatureTemplateFieldData
    {
        // This is the single source of truth for built-in creature_template field data.
        // Keep generation-specific changes inside the Create() method of each field-data file.
        // Do not create versioned CreateV* methods or parallel update files.
        public static IReadOnlyList<FieldDefinitionSeed> Definitions
        {
            get;
        } = new List<FieldDefinitionSeed>
        {
            AiNameFieldData.Create(),
            CivilianFieldData.Create(),
            CreatureTypeFieldData.Create(),
            CreatureTypeFlagsFieldData.Create(),
            DamageSchoolFieldData.Create(),
            DynamicFlagsFieldData.Create(),
            ExtraFlagsFieldData.Create(),
            FactionFieldData.CreateAlliance(),
            FactionFieldData.CreateHorde(),
            FamilyFieldData.Create(),
            InhabitTypeFieldData.Create(),
            MechanicImmuneMaskFieldData.Create(),
            MovementTypeFieldData.Create(),
            NpcFlagsFieldData.Create(),
            RacialLeaderFieldData.Create(),
            RankFieldData.Create(),
            RegenerateStatsFieldData.Create(),
            SchoolImmuneMaskFieldData.Create(),
            TrainerClassFieldData.Create(),
            TrainerTypeFieldData.Create(),
            UnitClassFieldData.Create(),
            UnitFlags2FieldData.Create(),
            UnitFlagsFieldData.Create()
        };
    }
}
