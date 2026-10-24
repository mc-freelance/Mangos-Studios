// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;
using MangosDatabaseStudio.Application.Schema;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    public sealed class FieldDataCategoryRecord
    {
        public long Id
        {
            get; set;
        }

        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-collection";
        public int SortOrder
        {
            get; set;
        }

        public bool IsEnabled { get; set; } = true;
        public int DefinitionCount
        {
            get; set;
        }

        public int VariantCount
        {
            get; set;
        }

        public int OptionCount
        {
            get; set;
        }
    }

    public sealed class FieldDataDefinitionRecord
    {
        public long Id
        {
            get; set;
        }

        public long? CategoryId
        {
            get; set;
        }

        public string CategoryName { get; set; } = string.Empty;
        public string SourceKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public MangosFieldSelectionMode SelectionMode { get; set; } = MangosFieldSelectionMode.Single;
        public MangosFieldValueHandling ValueHandling { get; set; } = MangosFieldValueHandling.Direct;
        public bool PreserveUnknownValues { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public bool IsSystem
        {
            get; set;
        }

        public bool IsEnabled { get; set; } = true;
        public int Priority
        {
            get; set;
        }

        public int VariantCount
        {
            get; set;
        }

        public int OptionCount
        {
            get; set;
        }

        public IReadOnlyList<int> Generations { get; set; } = new List<int>();
    }

    public sealed class FieldDataVariantRecord
    {
        public long Id
        {
            get; set;
        }

        public long DefinitionId
        {
            get; set;
        }

        public string Key { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IReadOnlyList<int> Generations { get; set; } = new List<int>();
        public IReadOnlyList<FieldDataOptionRecord> Options { get; set; } = new List<FieldDataOptionRecord>();
    }

    public sealed class FieldDataOptionRecord
    {
        public long Id
        {
            get; set;
        }

        public long VariantId
        {
            get; set;
        }

        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder
        {
            get; set;
        }
    }

    public sealed class FieldDataCatalogStats
    {
        public int CategoryCount
        {
            get; set;
        }

        public int DefinitionCount
        {
            get; set;
        }

        public int VariantCount
        {
            get; set;
        }

        public int OptionCount
        {
            get; set;
        }
    }
}
