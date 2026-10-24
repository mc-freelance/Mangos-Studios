// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class DamageSchoolFieldData
    {
        internal static readonly int[] generations = new[] { 0, 1, 2, 3 };

        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.damage_school",
                "DamageSchool",
                "Primary SpellSchools value used by creature melee attacks; values follow the core SpellSchools enum.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    generations,
                    "Primary SpellSchools value used by creature melee attacks; values follow the core SpellSchools enum.",
                    FieldDataSeedFactory.Option(0, "Normal", "Normal damage."),
                    FieldDataSeedFactory.Option(1, "Holy", "Holy damage."),
                    FieldDataSeedFactory.Option(2, "Fire", "Fire damage."),
                    FieldDataSeedFactory.Option(3, "Nature", "Nature damage."),
                    FieldDataSeedFactory.Option(4, "Frost", "Frost damage."),
                    FieldDataSeedFactory.Option(5, "Shadow", "Shadow damage."),
                    FieldDataSeedFactory.Option(6, "Arcane", "Arcane damage.")));
        }
    }
}
