// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class DatabaseAdminIndexViewModel
    {
        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();
    }

    public sealed class DatabaseTablesViewModel
    {
        public string Database { get; set; } = string.Empty;
        public IReadOnlyList<TableMetadata> Tables { get; set; } = new List<TableMetadata>();
    }

    public sealed class TableStructureViewModel
    {
        public TableMetadata Table { get; set; } = new TableMetadata();
        public bool CanModifyColumns
        {
            get; set;
        }

        public bool CanAddColumns
        {
            get; set;
        }

        public bool CanDropColumns
        {
            get; set;
        }

        public bool CanManageIndexes
        {
            get; set;
        }

        public bool CanManageTableProperties
        {
            get; set;
        }

        public bool CanWrite => CanModifyColumns || CanAddColumns || CanDropColumns || CanManageIndexes || CanManageTableProperties;
    }

    public sealed class SchemaColumnEditorViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public bool IsNew
        {
            get; set;
        }

        public string? OriginalColumnName
        {
            get; set;
        }

        [Required, StringLength(64)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(2048)]
        public string ColumnType { get; set; } = string.Empty;

        public bool IsNullable
        {
            get; set;
        }

        public string DefaultMode { get; set; } = "none";
        [StringLength(2048)]
        public string? DefaultValue
        {
            get; set;
        }

        public bool IsAutoIncrement
        {
            get; set;
        }

        [StringLength(1024)]
        public string? Comment
        {
            get; set;
        }

        public string Position { get; set; } = "LAST";
        public string? AfterColumn
        {
            get; set;
        }

        public IReadOnlyList<ColumnMetadata> AvailableColumns { get; set; } = new List<ColumnMetadata>();
    }

    public sealed class SchemaIndexEditorViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        [StringLength(64)]
        public string? Name
        {
            get; set;
        }

        public string Kind { get; set; } = "INDEX";
        public List<string> Columns { get; set; } = new List<string>();
        public TableMetadata TableMetadata { get; set; } = new TableMetadata();
    }

    public sealed class SchemaTablePropertiesEditorViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        [Required]
        public string Engine { get; set; } = string.Empty;
        [Required]
        public string Collation { get; set; } = string.Empty;
        [StringLength(2048)]
        public string? Comment
        {
            get; set;
        }

        public IReadOnlyList<string> Engines { get; set; } = new List<string>();
        public IReadOnlyList<string> Collations { get; set; } = new List<string>();
    }

    public sealed class SchemaChangeRequestViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string? OriginalColumnName
        {
            get; set;
        }

        public string? ColumnName
        {
            get; set;
        }

        public string? ColumnType
        {
            get; set;
        }

        public bool IsNullable
        {
            get; set;
        }

        public string DefaultMode { get; set; } = "none";
        public string? DefaultValue
        {
            get; set;
        }

        public bool IsAutoIncrement
        {
            get; set;
        }

        public string? Comment
        {
            get; set;
        }

        public string Position { get; set; } = "LAST";
        public string? AfterColumn
        {
            get; set;
        }

        public string? IndexName
        {
            get; set;
        }

        public string IndexKind { get; set; } = "INDEX";
        public List<string> IndexColumns { get; set; } = new List<string>();
        public string? Engine
        {
            get; set;
        }

        public string? Collation
        {
            get; set;
        }

        public string? TableComment
        {
            get; set;
        }

        public string? Confirmation
        {
            get; set;
        }

        public string PreviewSql { get; set; } = string.Empty;
        public string PreviewToken { get; set; } = string.Empty;
    }

    public sealed class SchemaChangePreviewViewModel
    {
        public SchemaChangeRequestViewModel Request { get; set; } = new SchemaChangeRequestViewModel();
        public SchemaChangePlan Plan { get; set; } = new SchemaChangePlan();
        public TableMetadata Table { get; set; } = new TableMetadata();
    }
}
