// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class MechanicImmuneMaskFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.mechanic_immune_mask",
                "MechanicImmuneMask",
                "Spell-mechanic immunities. Each option is the bit for one mechanics enum value.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    new[] { 0 },
                    "Vanilla mechanics values encoded as 1 << (mechanic - 1).",
                    FieldDataSeedFactory.Option(1, "Charm"),
                    FieldDataSeedFactory.Option(2, "Disoriented"),
                    FieldDataSeedFactory.Option(4, "Disarm"),
                    FieldDataSeedFactory.Option(8, "Distract"),
                    FieldDataSeedFactory.Option(16, "Fear"),
                    FieldDataSeedFactory.Option(32, "Fumble"),
                    FieldDataSeedFactory.Option(64, "Root"),
                    FieldDataSeedFactory.Option(128, "Pacify"),
                    FieldDataSeedFactory.Option(256, "Silence"),
                    FieldDataSeedFactory.Option(512, "Sleep"),
                    FieldDataSeedFactory.Option(1024, "Snare"),
                    FieldDataSeedFactory.Option(2048, "Stun"),
                    FieldDataSeedFactory.Option(4096, "Freeze"),
                    FieldDataSeedFactory.Option(8192, "Knockout"),
                    FieldDataSeedFactory.Option(16384, "Bleed"),
                    FieldDataSeedFactory.Option(32768, "Bandage"),
                    FieldDataSeedFactory.Option(65536, "Polymorph"),
                    FieldDataSeedFactory.Option(131072, "Banish"),
                    FieldDataSeedFactory.Option(262144, "Shield"),
                    FieldDataSeedFactory.Option(524288, "Shackle"),
                    FieldDataSeedFactory.Option(1048576, "Mount"),
                    FieldDataSeedFactory.Option(2097152, "Persuade"),
                    FieldDataSeedFactory.Option(4194304, "Turn"),
                    FieldDataSeedFactory.Option(8388608, "Horror"),
                    FieldDataSeedFactory.Option(16777216, "Invulnerability"),
                    FieldDataSeedFactory.Option(33554432, "Interrupt"),
                    FieldDataSeedFactory.Option(67108864, "Daze"),
                    FieldDataSeedFactory.Option(134217728, "Discovery"),
                    FieldDataSeedFactory.Option(268435456, "Immune shield"),
                    FieldDataSeedFactory.Option(536870912, "Sapped")),
                FieldDataSeedFactory.Variant(
                    "generations-1-3",
                    new[] { 1, 2, 3 },
                    "The Burning Crusade through Cataclysm use the 30-bit creature mechanic immunity mask.",
                    FieldDataSeedFactory.Option(1, "Charm"),
                    FieldDataSeedFactory.Option(2, "Confused"),
                    FieldDataSeedFactory.Option(4, "Disarm"),
                    FieldDataSeedFactory.Option(8, "Distract"),
                    FieldDataSeedFactory.Option(16, "Fear"),
                    FieldDataSeedFactory.Option(32, "Fumble"),
                    FieldDataSeedFactory.Option(64, "Root"),
                    FieldDataSeedFactory.Option(128, "Pacify"),
                    FieldDataSeedFactory.Option(256, "Silence"),
                    FieldDataSeedFactory.Option(512, "Sleep"),
                    FieldDataSeedFactory.Option(1024, "Snare"),
                    FieldDataSeedFactory.Option(2048, "Stun"),
                    FieldDataSeedFactory.Option(4096, "Freeze"),
                    FieldDataSeedFactory.Option(8192, "Knockout"),
                    FieldDataSeedFactory.Option(16384, "Bleed"),
                    FieldDataSeedFactory.Option(32768, "Bandage"),
                    FieldDataSeedFactory.Option(65536, "Polymorph"),
                    FieldDataSeedFactory.Option(131072, "Banish"),
                    FieldDataSeedFactory.Option(262144, "Shield"),
                    FieldDataSeedFactory.Option(524288, "Shackle"),
                    FieldDataSeedFactory.Option(1048576, "Mount"),
                    FieldDataSeedFactory.Option(2097152, "Persuade"),
                    FieldDataSeedFactory.Option(4194304, "Turn"),
                    FieldDataSeedFactory.Option(8388608, "Horror"),
                    FieldDataSeedFactory.Option(16777216, "Invulnerability"),
                    FieldDataSeedFactory.Option(33554432, "Interrupt"),
                    FieldDataSeedFactory.Option(67108864, "Daze"),
                    FieldDataSeedFactory.Option(134217728, "Discovery"),
                    FieldDataSeedFactory.Option(268435456, "Immune shield"),
                    FieldDataSeedFactory.Option(536870912, "Sapped")));
        }
    }
}
