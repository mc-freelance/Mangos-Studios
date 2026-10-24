// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class FieldDataIndexViewModel
    {
        public FieldDataCatalogStats Stats { get; set; } = new FieldDataCatalogStats();
        public IReadOnlyList<FieldDataCategoryRecord> Categories { get; set; } = new List<FieldDataCategoryRecord>();
        public IReadOnlyList<FieldDataDefinitionRecord> Definitions { get; set; } = new List<FieldDataDefinitionRecord>();
        public long? CategoryId
        {
            get; set;
        }

        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalDefinitions
        {
            get; set;
        }

        public int TotalPages
        {
            get; set;
        }

        public int FirstItem => TotalDefinitions == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int LastItem => TotalDefinitions == 0 ? 0 : Math.Min(Page * PageSize, TotalDefinitions);
        public bool CanManage
        {
            get; set;
        }
    }

    public sealed class FieldDataCategoryEditViewModel
    {
        public long Id
        {
            get; set;
        }

        public bool IsCreate
        {
            get; set;
        }

        [Required, StringLength(120)]
        public string Key { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;

        [StringLength(80)]
        public string Icon { get; set; } = "bi-collection";

        public int SortOrder
        {
            get; set;
        }

        public bool IsEnabled { get; set; } = true;
    }

    public sealed class FieldDataDefinitionEditViewModel
    {
        public long Id
        {
            get; set;
        }

        public bool IsCreate
        {
            get; set;
        }

        public long? CategoryId
        {
            get; set;
        }

        [StringLength(160)]
        public string DisplayName { get; set; } = string.Empty;

        [Required, StringLength(128)]
        public string TableName { get; set; } = string.Empty;

        [Required, StringLength(128)]
        public string ColumnName { get; set; } = string.Empty;

        public MangosFieldSelectionMode SelectionMode { get; set; } = MangosFieldSelectionMode.Single;
        public MangosFieldValueHandling ValueHandling { get; set; } = MangosFieldValueHandling.Direct;
        public bool PreserveUnknownValues { get; set; } = true;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
        public int Priority
        {
            get; set;
        }

        public bool IsSystem
        {
            get; set;
        }

        public string SourceKey { get; set; } = string.Empty;
        public string ActiveDatabase { get; set; } = string.Empty;
        public bool HasActiveDatabaseConnection
        {
            get; set;
        }

        public bool CanBrowseDatabaseSchema
        {
            get; set;
        }

        public IReadOnlyList<FieldDataCategoryRecord> Categories { get; set; } = new List<FieldDataCategoryRecord>();
        public IReadOnlyList<string> AvailableTables { get; set; } = new List<string>();
        public IReadOnlyList<string> AvailableColumns { get; set; } = new List<string>();
        public IReadOnlyList<FieldDataVariantRecord> Variants { get; set; } = new List<FieldDataVariantRecord>();
    }

    public sealed class FieldDataVariantEditViewModel
    {
        public long Id
        {
            get; set;
        }

        public long DefinitionId
        {
            get; set;
        }

        public bool IsCreate
        {
            get; set;
        }

        public string DefinitionName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public MangosFieldSelectionMode SelectionMode
        {
            get; set;
        }

        public MangosFieldValueHandling ValueHandling
        {
            get; set;
        }

        [Required, StringLength(160)]
        public string Key { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public List<int> Generations { get; set; } = new List<int>();
        public IReadOnlyList<FieldDataOptionRecord> Options { get; set; } = new List<FieldDataOptionRecord>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalEntries
        {
            get; set;
        }

        public int TotalPages { get; set; } = 1;
        public int FirstItem => TotalEntries == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int LastItem => TotalEntries == 0 ? 0 : Math.Min(Page * PageSize, TotalEntries);
    }

    public sealed class FieldDataOptionEditViewModel
    {
        public long Id
        {
            get; set;
        }

        public long VariantId
        {
            get; set;
        }

        public long DefinitionId
        {
            get; set;
        }

        public bool IsCreate
        {
            get; set;
        }

        public string DefinitionName { get; set; } = string.Empty;
        public string VariantName { get; set; } = string.Empty;
        public MangosFieldSelectionMode SelectionMode
        {
            get; set;
        }

        public MangosFieldValueHandling ValueHandling
        {
            get; set;
        }

        [StringLength(255)]
        public string Value { get; set; } = string.Empty;

        [Required, StringLength(255)]
        public string Label { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public int SortOrder
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
