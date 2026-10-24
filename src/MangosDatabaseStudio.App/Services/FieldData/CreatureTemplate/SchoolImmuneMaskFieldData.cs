// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class SchoolImmuneMaskFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.school_immune_mask",
                "SchoolImmuneMask",
                "Spell-school immunities. Multiple schools can be combined.",
                FieldDataSeedFactory.Variant(
                    "generations-0-1",
                    new[] { 0, 1 },
                    "Vanilla spell-school immunities. ",
                    FieldDataSeedFactory.Option(1, "Physical / Normal"),
                    FieldDataSeedFactory.Option(2, "Holy"),
                    FieldDataSeedFactory.Option(4, "Fire"),
                    FieldDataSeedFactory.Option(8, "Nature"),
                    FieldDataSeedFactory.Option(16, "Frost"),
                    FieldDataSeedFactory.Option(32, "Shadow"),
                    FieldDataSeedFactory.Option(64, "Arcane")),
                FieldDataSeedFactory.Variant(
                    "generation-2-wrath",
                    new[] { 2 },
                    "WoTLK spell-school immunities.",
                    FieldDataSeedFactory.Option(1, "Physical / Normal"),
                    FieldDataSeedFactory.Option(2, "Holy"),
                    FieldDataSeedFactory.Option(4, "Fire"),
                    FieldDataSeedFactory.Option(8, "Nature"),
                    FieldDataSeedFactory.Option(16, "Frost"),
                    FieldDataSeedFactory.Option(32, "Shadow"),
                    FieldDataSeedFactory.Option(64, "Arcane")),
                FieldDataSeedFactory.Variant(
                    "generation-3",
                    new[] { 3 },
                    "Cataclysm spell-school immunities.",
                    FieldDataSeedFactory.Option(1, "Physical / Normal"),
                    FieldDataSeedFactory.Option(2, "Holy"),
                    FieldDataSeedFactory.Option(4, "Fire"),
                    FieldDataSeedFactory.Option(8, "Nature"),
                    FieldDataSeedFactory.Option(16, "Frost"),
                    FieldDataSeedFactory.Option(32, "Shadow"),
                    FieldDataSeedFactory.Option(64, "Arcane")));
        }
    }
}
