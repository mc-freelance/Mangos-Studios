// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class RegenerateStatsFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.BitMask(
                "builtin.creature_template.regenerate_stats",
                "RegenerateStats",
                "Controls automatic Health and Power regeneration.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    new[] { 0, 1, 2, 3 },
                    "Vanilla through Cataclysm health/power regeneration from source.",
                    FieldDataSeedFactory.Option(1, "Health"),
                    FieldDataSeedFactory.Option(2, "Mana / Power")));
        }
    }
}
