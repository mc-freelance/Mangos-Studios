// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class DynamicFlagsFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.dynamic_flags",
                "DynamicFlags",
                "Dynamic client-visible creature state flags.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    new[] { 0 },
                    "Vanilla UnitDynFlags from source.",
                    FieldDataSeedFactory.Option(1, "Lootable"),
                    FieldDataSeedFactory.Option(2, "Track unit"),
                    FieldDataSeedFactory.Option(4, "Tapped", "Indicates the target as grey for the client (Lua_UnitIsTapped)."),
                    FieldDataSeedFactory.Option(8, "Rooted"),
                    FieldDataSeedFactory.Option(16, "Special info"),
                    FieldDataSeedFactory.Option(32, "Dead")),
                FieldDataSeedFactory.Variant(
                    "generation-1-tbc",
                    new[] { 1 },
                    "The Burning Crusade UnitDynFlags from source.",
                    FieldDataSeedFactory.Option(1, "Lootable"),
                    FieldDataSeedFactory.Option(2, "Track unit"),
                    FieldDataSeedFactory.Option(4, "Tapped"),
                    FieldDataSeedFactory.Option(8, "Rooted"),
                    FieldDataSeedFactory.Option(16, "Special info"),
                    FieldDataSeedFactory.Option(32, "Dead")),
                FieldDataSeedFactory.Variant(
                    "generation-2-wrath",
                    new[] { 2 },
                    "WoTLK UnitDynFlags from source.",
                    FieldDataSeedFactory.Option(1, "Lootable"),
                    FieldDataSeedFactory.Option(2, "Track unit"),
                    FieldDataSeedFactory.Option(4, "Tapped"),
                    FieldDataSeedFactory.Option(8, "Tapped by player"),
                    FieldDataSeedFactory.Option(16, "Special info"),
                    FieldDataSeedFactory.Option(32, "Dead"),
                    FieldDataSeedFactory.Option(64, "Refer a friend"),
                    FieldDataSeedFactory.Option(128, "Tapped by all threat list")),
                FieldDataSeedFactory.Variant(
                    "generation-3",
                    new[] { 3 },
                    "Cataclysm UnitDynFlags from source.",
                    FieldDataSeedFactory.Option(1, "Lootable"),
                    FieldDataSeedFactory.Option(2, "Track unit"),
                    FieldDataSeedFactory.Option(4, "Tapped", "Marks the creature as tapped by another player."),
                    FieldDataSeedFactory.Option(8, "Tapped by player", "Marks the creature as tapped by the current player."),
                    FieldDataSeedFactory.Option(16, "Special info"),
                    FieldDataSeedFactory.Option(32, "Dead"),
                    FieldDataSeedFactory.Option(64, "Refer a friend"),
                    FieldDataSeedFactory.Option(128, "Tapped by all threat list")));
        }
    }
}
