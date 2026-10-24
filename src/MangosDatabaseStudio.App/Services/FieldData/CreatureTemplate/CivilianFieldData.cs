// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class CivilianFieldData
    {
        internal static readonly int[] generations = new[] { 0 };

        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.civilian",
                "Civilian",
                "Marks whether the creature behaves as a civilian.",
                FieldDataSeedFactory.Variant(
                    "generation-0",
                    generations,
                    "Vanilla stores the civilian marker as 2.",
                    FieldDataSeedFactory.Option(0, "Not a civilian", "The creature does not behave as a civilian."),
                    FieldDataSeedFactory.Option(2, "Civilian", "The creature behaves as a civilian.")));
        }
    }
}
