// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class TrainerClassFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.trainer_class",
                "TrainerClass",
                "Class restriction for class and pet trainers.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    new[] { 0, 1, 2, 3 },
                    "Class restriction for class and pet trainers.",
                    FieldDataSeedFactory.Option(1, "Warrior"),
                    FieldDataSeedFactory.Option(2, "Paladin"),
                    FieldDataSeedFactory.Option(3, "Hunter"),
                    FieldDataSeedFactory.Option(4, "Rogue"),
                    FieldDataSeedFactory.Option(5, "Priest"),
                    FieldDataSeedFactory.Option(7, "Shaman"),
                    FieldDataSeedFactory.Option(8, "Mage"),
                    FieldDataSeedFactory.Option(9, "Warlock"),
                    FieldDataSeedFactory.Option(11, "Druid")));
        }
    }
}
