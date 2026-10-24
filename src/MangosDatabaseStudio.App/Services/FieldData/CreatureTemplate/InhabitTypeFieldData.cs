// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class InhabitTypeFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.inhabit_type",
                "InhabitType",
                "Allowed movement environments. Multiple environments can be combined.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    new[] { 0 },
                    "Vanilla InhabitTypeValues from the Vanilla core source.",
                    FieldDataSeedFactory.Option(1, "Ground movement"),
                    FieldDataSeedFactory.Option(2, "Water movement"),
                    FieldDataSeedFactory.Option(4, "Air movement")),
                FieldDataSeedFactory.Variant(
                    "generations-1-3",
                    new[] { 1, 2, 3 },
                    "TBC through Cataclysm use the ground/water/air inhabit bit layout; Cataclysm maps 0x04 to air movement.",
                    FieldDataSeedFactory.Option(1, "Ground movement"),
                    FieldDataSeedFactory.Option(2, "Water movement"),
                    FieldDataSeedFactory.Option(4, "Flying")));
        }
    }
}
