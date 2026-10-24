// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;
using MangosDatabaseStudio.Application.Schema;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal sealed class FieldDefinitionSeed
    {
        // Handle field definition seed within the field data seed models workflow.
        public FieldDefinitionSeed(
            string sourceKey,
            string tableName,
            string columnName,
            MangosFieldEditorKind editorKind,
            string description,
            IReadOnlyList<FieldVariantSeed> variants)
        {
            SourceKey = sourceKey;
            TableName = tableName;
            ColumnName = columnName;
            EditorKind = editorKind;
            Description = description;
            Variants = variants;
        }

        public string SourceKey
        {
            get;
        }

        public string TableName
        {
            get;
        }

        public string ColumnName
        {
            get;
        }

        public MangosFieldEditorKind EditorKind
        {
            get;
        }

        public string Description
        {
            get;
        }

        public IReadOnlyList<FieldVariantSeed> Variants
        {
            get;
        }
    }

    internal sealed class FieldVariantSeed
    {
        // Handle field variant seed within the field data seed models workflow.
        public FieldVariantSeed(
            string key,
            IReadOnlyList<int> generations,
            string description,
            IReadOnlyList<FieldOptionSeed> options)
        {
            Key = key;
            Generations = generations;
            Description = description;
            Options = options;
        }

        public string Key
        {
            get;
        }

        public IReadOnlyList<int> Generations
        {
            get;
        }

        public string Description
        {
            get;
        }

        public IReadOnlyList<FieldOptionSeed> Options
        {
            get;
        }
    }

    internal sealed class FieldOptionSeed
    {
        // Handle field option seed within the field data seed models workflow.
        public FieldOptionSeed(string value, string label, string description = "")
        {
            Value = value;
            Label = label;
            Description = description;
        }

        public string Value
        {
            get;
        }

        public string Label
        {
            get;
        }

        public string Description
        {
            get;
        }
    }
}
