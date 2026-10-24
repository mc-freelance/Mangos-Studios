// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class CreatureTypeFlagsFieldData
    {
        internal static readonly int[] generations0 = new[] { 0 };
        internal static readonly int[] generations1 = new[] { 1 };
        internal static readonly int[] generations2 = new[] { 2 };
        internal static readonly int[] generations3 = new[] { 3 };

        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.creature_type_flags",
                "CreatureTypeFlags",
                "Client creature-type capability flags. Composite values are built from the individual bits.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    generations0,
                    "Vanilla creature-type flags from source.",
                    FieldDataSeedFactory.Option(1, "Tameable", "Tameable by a hunter."),
                    FieldDataSeedFactory.Option(2, "Ghost visible", "Also visible while the player is a ghost; used by client interaction logic."),
                    FieldDataSeedFactory.Option(4, "Boss tooltip / Unknown 3", "The Zero source identifies this as the BOSS flag used by client tooltips."),
                    FieldDataSeedFactory.Option(8, "Unknown 4", "May be related to client interaction logic."),
                    FieldDataSeedFactory.Option(16, "Unknown 5", "Controls client tooltip behavior related to creature faction."),
                    FieldDataSeedFactory.Option(32, "Unknown 6", "May be sound related."),
                    FieldDataSeedFactory.Option(64, "Unknown 7", "May affect client helpful/harmful spell interaction logic."),
                    FieldDataSeedFactory.Option(128, "Unknown 8", "Related to unit interaction / quest status requests."),
                    FieldDataSeedFactory.Option(256, "Herbalism loot", "Can be looted by a herbalist."),
                    FieldDataSeedFactory.Option(512, "Mining loot", "Can be looted by a miner."),
                    FieldDataSeedFactory.Option(1024, "Unknown 11", "Used by the client; exact behavior is not documented by the Zero core."),
                    FieldDataSeedFactory.Option(2048, "Unknown 12", "Related to the ability to cast spells while mounted."),
                    FieldDataSeedFactory.Option(4096, "Can assist", "Can aid a player or group in combat; commonly used by escort NPCs."),
                    FieldDataSeedFactory.Option(8192, "Unknown 14", "Checked by the client for pet action-bar behavior."),
                    FieldDataSeedFactory.Option(16384, "Unknown 15", "Affects client GUID-low handling."),
                    FieldDataSeedFactory.Option(32768, "Engineering loot", "Can be looted by an engineer.")),
                FieldDataSeedFactory.Variant(
                    "generation-1-tbc",
                    generations1,
                    "The Burning Crusade creature-type flags from source.",
                    FieldDataSeedFactory.Option(1, "Tameable", "Tameable by a hunter."),
                    FieldDataSeedFactory.Option(2, "Ghost visible", "Also visible while the player is a ghost; used by client interaction logic."),
                    FieldDataSeedFactory.Option(4, "Boss tooltip / Unknown 3", "The Zero source identifies this as the BOSS flag used by client tooltips."),
                    FieldDataSeedFactory.Option(8, "Unknown 4", "May be related to client interaction logic."),
                    FieldDataSeedFactory.Option(16, "Unknown 5", "Controls client tooltip behavior related to creature faction."),
                    FieldDataSeedFactory.Option(32, "Unknown 6", "May be sound related."),
                    FieldDataSeedFactory.Option(64, "Unknown 7", "Related to the creature's ability to interact with the environment."),
                    FieldDataSeedFactory.Option(128, "Unknown 8", "Used by the client for specific interaction behaviors."),
                    FieldDataSeedFactory.Option(256, "Herbalism loot", "Can be looted by a herbalist."),
                    FieldDataSeedFactory.Option(512, "Mining loot", "Can be looted by a miner."),
                    FieldDataSeedFactory.Option(1024, "Unknown 11", "Used by the client; exact behavior is not documented by the Zero core."),
                    FieldDataSeedFactory.Option(2048, "Unknown 12", "Related to the ability to cast spells while mounted."),
                    FieldDataSeedFactory.Option(4096, "Can assist", "Can aid a player or group in combat; commonly used by escort NPCs."),
                    FieldDataSeedFactory.Option(8192, "Unknown 14", "Checked by the client for pet action-bar behavior."),
                    FieldDataSeedFactory.Option(16384, "Unknown 15", "Affects client GUID-low handling."),
                    FieldDataSeedFactory.Option(32768, "Engineering loot", "Can be looted by an engineer.")),
                FieldDataSeedFactory.Variant(
                    "generation-2-wrath",
                    generations2,
                    "WoTLK creature-type flags from source.",
                    FieldDataSeedFactory.Option(1, "Tameable"),
                    FieldDataSeedFactory.Option(2, "Ghost visible"),
                    FieldDataSeedFactory.Option(4, "Boss creature"),
                    FieldDataSeedFactory.Option(8, "No wound parry animation"),
                    FieldDataSeedFactory.Option(16, "No faction tooltip"),
                    FieldDataSeedFactory.Option(32, "More audible"),
                    FieldDataSeedFactory.Option(64, "Spell attackable"),
                    FieldDataSeedFactory.Option(128, "Interact while dead"),
                    FieldDataSeedFactory.Option(256, "Herbalism loot"),
                    FieldDataSeedFactory.Option(512, "Mining loot"),
                    FieldDataSeedFactory.Option(1024, "No death message"),
                    FieldDataSeedFactory.Option(2048, "Allow mounted combat"),
                    FieldDataSeedFactory.Option(4096, "Can assist"),
                    FieldDataSeedFactory.Option(8192, "No pet bar"),
                    FieldDataSeedFactory.Option(16384, "Mask UID"),
                    FieldDataSeedFactory.Option(32768, "Engineering loot"),
                    FieldDataSeedFactory.Option(65536, "Tameable exotic"),
                    FieldDataSeedFactory.Option(131072, "Use model collision size"),
                    FieldDataSeedFactory.Option(262144, "Allow interaction in combat"),
                    FieldDataSeedFactory.Option(524288, "Collide with missiles"),
                    FieldDataSeedFactory.Option(1048576, "No name plate"),
                    FieldDataSeedFactory.Option(2097152, "Do not play mounted animations"),
                    FieldDataSeedFactory.Option(4194304, "Link all"),
                    FieldDataSeedFactory.Option(8388608, "Interact only with creator"),
                    FieldDataSeedFactory.Option(16777216, "Do not play unit event sounds"),
                    FieldDataSeedFactory.Option(33554432, "No shadow blob"),
                    FieldDataSeedFactory.Option(67108864, "Treat as raid unit"),
                    FieldDataSeedFactory.Option(134217728, "Force gossip"),
                    FieldDataSeedFactory.Option(268435456, "Do not sheathe"),
                    FieldDataSeedFactory.Option(536870912, "Do not target on interaction"),
                    FieldDataSeedFactory.Option(1073741824, "Do not render object name"),
                    FieldDataSeedFactory.Option(2147483648, "Unit is quest boss")),
                FieldDataSeedFactory.Variant(
                    "generation-3",
                    generations3,
                    "Cataclysm creature-type flags from source.",
                    FieldDataSeedFactory.Option(1, "Tameable", "Makes the creature tameable when it is a Beast with a valid family."),
                    FieldDataSeedFactory.Option(2, "Ghost visible", "Creature can also be seen while the player is a ghost."),
                    FieldDataSeedFactory.Option(4, "Boss creature", "Marks the creature as a boss for client tooltip behavior."),
                    FieldDataSeedFactory.Option(8, "No wound parry animation", "The creature does not play a wound parry animation."),
                    FieldDataSeedFactory.Option(16, "No faction tooltip", "The creature does not display a faction tooltip."),
                    FieldDataSeedFactory.Option(32, "More audible", "The creature's sounds are more audible."),
                    FieldDataSeedFactory.Option(64, "Spell attackable", "The creature can be attacked by spells."),
                    FieldDataSeedFactory.Option(128, "Interact while dead", "The creature can be interacted with while dead."),
                    FieldDataSeedFactory.Option(256, "Herbalism loot", "Can be looted by a herbalist."),
                    FieldDataSeedFactory.Option(512, "Mining loot", "Can be looted by a miner."),
                    FieldDataSeedFactory.Option(1024, "No death message", "The creature does not display a death message."),
                    FieldDataSeedFactory.Option(2048, "Allow mounted combat", "The creature can engage in combat while mounted."),
                    FieldDataSeedFactory.Option(4096, "Can assist", "Can aid a player or group in combat; commonly used by escort NPCs."),
                    FieldDataSeedFactory.Option(8192, "No pet bar", "The creature does not display a pet action-bar."),
                    FieldDataSeedFactory.Option(16384, "Mask UID", "Masks the creature's unique ID."),
                    FieldDataSeedFactory.Option(32768, "Engineering loot", "Can be looted by an engineer."),
                    FieldDataSeedFactory.Option(65536, "Exotic tameable", "The creature can be tamed as an exotic pet."),
                    FieldDataSeedFactory.Option(131072, "Use model collision size", "Uses the model's collision size for interaction."),
                    FieldDataSeedFactory.Option(262144, "Allow interaction in combat", "Allows interaction with the creature while it is in combat."),
                    FieldDataSeedFactory.Option(524288, "Collide with missiles", "The creature collides with incoming missiles."),
                    FieldDataSeedFactory.Option(1048576, "No name plate", "The creature does not display a name plate."),
                    FieldDataSeedFactory.Option(2097152, "Do not play mounted animations", "The creature does not play mounted animations."),
                    FieldDataSeedFactory.Option(4194304, "Link all", "Links all instances of the creature together."),
                    FieldDataSeedFactory.Option(8388608, "Interact only with creator", "The creature can only be interacted with by its creator."),
                    FieldDataSeedFactory.Option(16777216, "Do not play unit event sounds", "The creature does not play unit event sounds."),
                    FieldDataSeedFactory.Option(33554432, "No shadow blob", "The creature does not display a shadow blob."),
                    FieldDataSeedFactory.Option(67108864, "Treat as raid unit", "The creature is treated as a raid unit."),
                    FieldDataSeedFactory.Option(134217728, "Force gossip", "Forces the creature to display gossip."),
                    FieldDataSeedFactory.Option(268435456, "Do not sheathe", "The creature does not sheathe its weapon."),
                    FieldDataSeedFactory.Option(536870912, "Do not target on interaction", "The creature cannot be targeted when interacted with."),
                    FieldDataSeedFactory.Option(1073741824, "Do not render object name", "The creature's name is not displayed."),
                    FieldDataSeedFactory.Option(2147483648UL, "Unit is quest boss", "The creature is a quest boss.")));
        }
    }
}
