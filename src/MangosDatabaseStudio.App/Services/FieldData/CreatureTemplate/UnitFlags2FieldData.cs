// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class UnitFlags2FieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.unit_flags_2",
                "UnitFlags2",
                "Secondary unit-state flags used by Cataclysm.",
                FieldDataSeedFactory.Variant(
                    "generation-3",
                    new[] { 3 },
                    "Cataclysm UnitFlags2 from source.",
                    FieldDataSeedFactory.Option(1, "Feign death"),
                    FieldDataSeedFactory.Option(2, "Unknown 1", "Hides body and body armor; some equipment remains visible."),
                    FieldDataSeedFactory.Option(4, "Unknown 2"),
                    FieldDataSeedFactory.Option(8, "Comprehend language"),
                    FieldDataSeedFactory.Option(16, "Cloned", "Used by mirror-image effects."),
                    FieldDataSeedFactory.Option(32, "Unknown 5"),
                    FieldDataSeedFactory.Option(64, "Force move"),
                    FieldDataSeedFactory.Option(128, "Disarm offhand"),
                    FieldDataSeedFactory.Option(256, "Unknown 8"),
                    FieldDataSeedFactory.Option(512, "Unknown 9"),
                    FieldDataSeedFactory.Option(1024, "Disarm ranged"),
                    FieldDataSeedFactory.Option(2048, "Regenerate power"),
                    FieldDataSeedFactory.Option(524288, "Worgen transform"),
                    FieldDataSeedFactory.Option(1048576, "Worgen transform 2"),
                    FieldDataSeedFactory.Option(2097152, "Worgen transform 3")));
        }
    }
}
