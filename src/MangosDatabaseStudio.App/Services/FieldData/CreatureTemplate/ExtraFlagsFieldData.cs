// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class ExtraFlagsFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.extra_flags",
                "ExtraFlags",
                "Server-side creature behavior flags.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    new[] { 0 },
                    "Vanilla creature extra flags from source.",
                    FieldDataSeedFactory.Option(1, "Instance bind", "A kill binds the instance to the killer and the killer's group."),
                    FieldDataSeedFactory.Option(2, "No aggro", "Ignore faction/reputation hostility for automatic aggro."),
                    FieldDataSeedFactory.Option(4, "No parry", "Creature cannot parry."),
                    FieldDataSeedFactory.Option(8, "No parry hasten", "Creature cannot counter-attack faster after a parry."),
                    FieldDataSeedFactory.Option(16, "No block", "Creature cannot block."),
                    FieldDataSeedFactory.Option(32, "No crush", "Creature cannot perform crushing attacks."),
                    FieldDataSeedFactory.Option(64, "No XP at kill", "Killing the creature does not award XP."),
                    FieldDataSeedFactory.Option(128, "Invisible", "Creature is always invisible to players; commonly used for trigger creatures."),
                    FieldDataSeedFactory.Option(256, "Not tauntable", "Immune to taunt auras and attack-me effects."),
                    FieldDataSeedFactory.Option(512, "Aggro zone", "Entering aggro places the creature in combat with the zone."),
                    FieldDataSeedFactory.Option(1024, "Guard", "Creature is treated as a guard."),
                    FieldDataSeedFactory.Option(2048, "No call assist", "Creature should not call for assistance when it aggros."),
                    FieldDataSeedFactory.Option(4096, "Active", "Creature is kept active and its grid remains loaded."),
                    FieldDataSeedFactory.Option(8192, "MMap force enable", "Force the creature to use MMaps."),
                    FieldDataSeedFactory.Option(16384, "MMap force disable", "Force the creature not to use MMaps."),
                    FieldDataSeedFactory.Option(32768, "Walk in water", "Force walking in water even when the creature can swim."),
                    FieldDataSeedFactory.Option(65536, "No swim animation", "Creature has no swim animation.")),
                FieldDataSeedFactory.Variant(
                    "generation-1-tbc",
                    new[] { 1 },
                    "The Burning Crusade creature extra flags from source.",
                    FieldDataSeedFactory.Option(1, "Instance bind"),
                    FieldDataSeedFactory.Option(2, "No aggro"),
                    FieldDataSeedFactory.Option(4, "No parry"),
                    FieldDataSeedFactory.Option(8, "No parry haste"),
                    FieldDataSeedFactory.Option(16, "No block"),
                    FieldDataSeedFactory.Option(32, "No crush"),
                    FieldDataSeedFactory.Option(64, "No XP at kill"),
                    FieldDataSeedFactory.Option(128, "Invisible"),
                    FieldDataSeedFactory.Option(256, "Not tauntable"),
                    FieldDataSeedFactory.Option(512, "Aggro zone"),
                    FieldDataSeedFactory.Option(1024, "Guard"),
                    FieldDataSeedFactory.Option(2048, "No call assist"),
                    FieldDataSeedFactory.Option(4096, "Active"),
                    FieldDataSeedFactory.Option(8192, "MMap force enable"),
                    FieldDataSeedFactory.Option(16384, "MMap force disable")),
                FieldDataSeedFactory.Variant(
                    "generation-2-wrath",
                    new[] { 2 },
                    "WoTLK creature extra flags from source.",
                    FieldDataSeedFactory.Option(1, "Instance bind"),
                    FieldDataSeedFactory.Option(2, "Civilian"),
                    FieldDataSeedFactory.Option(4, "No parry"),
                    FieldDataSeedFactory.Option(8, "No parry hasten"),
                    FieldDataSeedFactory.Option(16, "No block"),
                    FieldDataSeedFactory.Option(32, "No crush"),
                    FieldDataSeedFactory.Option(64, "No XP at kill"),
                    FieldDataSeedFactory.Option(128, "Invisible / trigger"),
                    FieldDataSeedFactory.Option(256, "Not tauntable"),
                    FieldDataSeedFactory.Option(512, "No movement flags update"),
                    FieldDataSeedFactory.Option(1024, "Aggro zone"),
                    FieldDataSeedFactory.Option(2048, "Guard"),
                    FieldDataSeedFactory.Option(4096, "No call assist"),
                    FieldDataSeedFactory.Option(8192, "No talk-to credit"),
                    FieldDataSeedFactory.Option(16384, "Active"),
                    FieldDataSeedFactory.Option(32768, "MMap force enable"),
                    FieldDataSeedFactory.Option(65536, "MMap force disable"),
                    FieldDataSeedFactory.Option(131072, "Walk in water")),
                FieldDataSeedFactory.Variant(
                    "generation-3",
                    new[] { 3 },
                    "Cataclysm creature extra flags from source.",
                    FieldDataSeedFactory.Option(1, "Instance bind", "Creature kill binds the instance to the killer and group."),
                    FieldDataSeedFactory.Option(2, "Civilian", "Creature ignores normal aggro hostility."),
                    FieldDataSeedFactory.Option(4, "No parry"),
                    FieldDataSeedFactory.Option(8, "No parry hasten"),
                    FieldDataSeedFactory.Option(16, "No block"),
                    FieldDataSeedFactory.Option(32, "No crush"),
                    FieldDataSeedFactory.Option(64, "No XP at kill"),
                    FieldDataSeedFactory.Option(128, "Invisible"),
                    FieldDataSeedFactory.Option(256, "Not tauntable"),
                    FieldDataSeedFactory.Option(512, "Aggro zone"),
                    FieldDataSeedFactory.Option(1024, "Guard"),
                    FieldDataSeedFactory.Option(2048, "No call assist"),
                    FieldDataSeedFactory.Option(4096, "Active"),
                    FieldDataSeedFactory.Option(8192, "MMap force enable"),
                    FieldDataSeedFactory.Option(16384, "MMap force disable"),
                    FieldDataSeedFactory.Option(32768, "Walk in water"),
                    FieldDataSeedFactory.Option(65536, "No swim animation"),
                    FieldDataSeedFactory.Option(131072, "No melee"),
                    FieldDataSeedFactory.Option(262144, "Unkillable")));
        }
    }
}
