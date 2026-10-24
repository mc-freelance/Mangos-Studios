// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class AiNameFieldData
    {
        internal static readonly int[] generations = new[] { 0, 1, 2, 3 };

        // Build a field definition from its seed metadata and expansion variants.
        public static FieldDefinitionSeed Create()
        {
            return CreatureTemplateFieldDataFactory.Select(
                "builtin.creature_template.ai_name",
                "AIName",
                "Built-in creature AI implementation.",
                FieldDataSeedFactory.Variant(
                    "generations-0-3",
                    generations,
                    "Built-in creature AI implementation.",
                    FieldDataSeedFactory.Option("", "Default / no built-in AI", "The creature does not use any built-in AI implementation."),
                    FieldDataSeedFactory.Option("NullAI", "NullAI", "The creature uses the NullAI implementation."),
                    FieldDataSeedFactory.Option("AggressorAI", "AggressorAI", "The creature uses the AggressorAI implementation."),
                    FieldDataSeedFactory.Option("ReactorAI", "ReactorAI", "The creature uses the ReactorAI implementation."),
                    FieldDataSeedFactory.Option("GuardAI", "GuardAI", "The creature uses the GuardAI implementation."),
                    FieldDataSeedFactory.Option("PetAI", "PetAI", "The creature uses the PetAI implementation."),
                    FieldDataSeedFactory.Option("TotemAI", "TotemAI", "The creature uses the TotemAI implementation."),
                    FieldDataSeedFactory.Option("EventAI", "EventAI", "The creature uses the EventAI implementation.")));

        }
    }
}
