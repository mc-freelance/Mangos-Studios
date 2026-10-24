// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class RankFieldData
    {
        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.rank",
                "Rank",
                "Creature rank shown by the client.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    new[] { 0, 1, 2, 3 },
                    "Creature rank shown by the client.",
                    FieldDataSeedFactory.Option(0, "Normal"),
                    FieldDataSeedFactory.Option(1, "Elite"),
                    FieldDataSeedFactory.Option(2, "Rare elite"),
                    FieldDataSeedFactory.Option(3, "World boss"),
                    FieldDataSeedFactory.Option(4, "Rare")));

        }
    }
}
