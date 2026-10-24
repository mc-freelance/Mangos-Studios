// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class CreatureTypeFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.creature_type",
                "CreatureType",
                "Creature type reported to the client.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    new[] { 0 },
                    "Vanilla CreatureType values from source.",
                    FieldDataSeedFactory.Option(0, "None / not set", "Database default / no creature type."),
                    FieldDataSeedFactory.Option(1, "Beast"),
                    FieldDataSeedFactory.Option(2, "Dragonkin"),
                    FieldDataSeedFactory.Option(3, "Demon"),
                    FieldDataSeedFactory.Option(4, "Elemental"),
                    FieldDataSeedFactory.Option(5, "Giant"),
                    FieldDataSeedFactory.Option(6, "Undead"),
                    FieldDataSeedFactory.Option(7, "Humanoid"),
                    FieldDataSeedFactory.Option(8, "Critter"),
                    FieldDataSeedFactory.Option(9, "Mechanical"),
                    FieldDataSeedFactory.Option(10, "Not Specified"),
                    FieldDataSeedFactory.Option(11, "Totem")),
                FieldDataSeedFactory.Variant(
                    "generations-1-3",
                    new[] { 1, 2, 3 },
                    "Existing TBC through Cataclysm seed data; these generations have not yet been source-audited in this pass.",
                    FieldDataSeedFactory.Option(0, "None"),
                    FieldDataSeedFactory.Option(1, "Beast"),
                    FieldDataSeedFactory.Option(2, "Dragonkin"),
                    FieldDataSeedFactory.Option(3, "Demon"),
                    FieldDataSeedFactory.Option(4, "Elemental"),
                    FieldDataSeedFactory.Option(5, "Giant"),
                    FieldDataSeedFactory.Option(6, "Undead"),
                    FieldDataSeedFactory.Option(7, "Humanoid"),
                    FieldDataSeedFactory.Option(8, "Critter"),
                    FieldDataSeedFactory.Option(9, "Mechanical"),
                    FieldDataSeedFactory.Option(10, "Not Specified"),
                    FieldDataSeedFactory.Option(11, "Totem"),
                    FieldDataSeedFactory.Option(12, "Non-combat pet"),
                    FieldDataSeedFactory.Option(13, "Gas Cloud")));
        }
    }
}
