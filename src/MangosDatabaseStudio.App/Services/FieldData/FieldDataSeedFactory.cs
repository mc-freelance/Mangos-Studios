// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;
using System.Globalization;
using MangosDatabaseStudio.Application.Schema;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal static class FieldDataSeedFactory
    {
        // Handle select within the field data seed factory workflow.
        public static FieldDefinitionSeed Select(
            string sourceKey,
            string tableName,
            string columnName,
            string description,
            params FieldVariantSeed[] variants)
        {
            return Definition(
                sourceKey,
                tableName,
                columnName,
                MangosFieldEditorKind.Select,
                description,
                variants);
        }

        // Handle bit mask within the field data seed factory workflow.
        public static FieldDefinitionSeed BitMask(
            string sourceKey,
            string tableName,
            string columnName,
            string description,
            params FieldVariantSeed[] variants)
        {
            return Definition(
                sourceKey,
                tableName,
                columnName,
                MangosFieldEditorKind.BitMask,
                description,
                variants);
        }

        // Handle variant within the field data seed factory workflow.
        public static FieldVariantSeed Variant(
            string key,
            IReadOnlyList<int> generations,
            string description,
            params FieldOptionSeed[] options)
        {
            return new FieldVariantSeed(key, generations, description, options);
        }

        // Handle option within the field data seed factory workflow.
        public static FieldOptionSeed Option(ulong value, string label, string description = "")
        {
            return new FieldOptionSeed(
                value.ToString(CultureInfo.InvariantCulture),
                label,
                description);
        }

        // Handle option within the field data seed factory workflow.
        public static FieldOptionSeed Option(string value, string label, string description = "")
        {
            return new FieldOptionSeed(value, label, description);
        }

        // Handle definition within the field data seed factory workflow.
        private static FieldDefinitionSeed Definition(
            string sourceKey,
            string tableName,
            string columnName,
            MangosFieldEditorKind editorKind,
            string description,
            IReadOnlyList<FieldVariantSeed> variants)
        {
            return new FieldDefinitionSeed(
                sourceKey,
                tableName,
                columnName,
                editorKind,
                description,
                variants);
        }
    }
}
