// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class MovementTypeFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.movement_type",
                "MovementType",
                "Default movement behavior after the creature spawns.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    new[] { 0, 1, 2, 3 },
                    "Default movement behavior after the creature spawns.",
                    FieldDataSeedFactory.Option(0, "Idle"),
                    FieldDataSeedFactory.Option(1, "Random"),
                    FieldDataSeedFactory.Option(2, "Waypoint")));
        }
    }
}
