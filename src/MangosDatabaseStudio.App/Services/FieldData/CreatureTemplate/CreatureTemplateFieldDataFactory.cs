// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate
{
    internal static class CreatureTemplateFieldDataFactory
    {
        private const string TableName = "creature_template";

        // Handle select within the creature template field data factory workflow.
        public static FieldDefinitionSeed Select(
            string sourceKey,
            string columnName,
            string description,
            params FieldVariantSeed[] variants)
        {
            return FieldDataSeedFactory.Select(
                sourceKey,
                TableName,
                columnName,
                description,
                variants);
        }

        // Handle bit mask within the creature template field data factory workflow.
        public static FieldDefinitionSeed BitMask(
            string sourceKey,
            string columnName,
            string description,
            params FieldVariantSeed[] variants)
        {
            return FieldDataSeedFactory.BitMask(
                sourceKey,
                TableName,
                columnName,
                description,
                variants);
        }
    }
}
