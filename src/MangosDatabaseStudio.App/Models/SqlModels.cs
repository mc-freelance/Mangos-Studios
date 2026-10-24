// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Sql;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class SqlConsoleViewModel
    {
        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();

        [Required]
        public string Database { get; set; } = string.Empty;

        [Required]
        public string Sql { get; set; } = "SELECT * FROM `db_version` LIMIT 50;";

        [Range(1, 1000)]
        public int MaximumRows { get; set; } = 250;
        public bool ConfirmDestructive
        {
            get; set;
        }

        public SqlExecutionResult? Result
        {
            get; set;
        }

        public string? Warning
        {
            get; set;
        }
    }

    public sealed class SqlExportViewModel
    {
        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();
        public IReadOnlyList<TableMetadata> Tables { get; set; } = new List<TableMetadata>();

        [Required]
        public string Database { get; set; } = string.Empty;

        public List<string> SelectedTables { get; set; } = new List<string>();

        [EnumDataType(typeof(SqlExportContent))]
        public SqlExportContent Content { get; set; } = SqlExportContent.StructureAndData;
        public bool IncludeDropStatements { get; set; } = true;
        public bool IncludeTriggers { get; set; } = true;
        public bool IncludeRoutines
        {
            get; set;
        }

        public bool IncludeEvents
        {
            get; set;
        }

        public bool UseSingleTransaction { get; set; } = true;
        public bool CompressOutput
        {
            get; set;
        }

        [Required]
        [RegularExpression("^[a-f0-9]{32}$")]
        public string DownloadToken { get; set; } = string.Empty;
    }

    public sealed class SqlImportUploadViewModel
    {
        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();

        [Required]
        public string Database { get; set; } = string.Empty;

        [Required]
        public IFormFile? File
        {
            get; set;
        }
    }

    public sealed class SqlImportReviewViewModel
    {
        public string Database { get; set; } = string.Empty;
        public SqlImportAnalysis Analysis { get; set; } = new SqlImportAnalysis();
    }

    public sealed class SqlImportConfirmViewModel
    {
        [Required]
        public string Database { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        public bool Confirm
        {
            get; set;
        }
    }

    public sealed class SqlImportResultViewModel
    {
        public string Database { get; set; } = string.Empty;
        public SqlImportResult Result { get; set; } = new SqlImportResult();
    }
}
