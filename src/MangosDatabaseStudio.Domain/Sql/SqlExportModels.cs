// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

using System;
using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Sql
{
    public enum SqlExportContent
    {
        StructureAndData = 0,
        StructureOnly = 1,
        DataOnly = 2
    }

    public sealed class SqlExportRequest
    {
        public string Database { get; set; } = string.Empty;
        public IReadOnlyList<string> Tables { get; set; } = new List<string>();
        public bool AllTablesSelected
        {
            get; set;
        }

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

        public string ApplicationVersion { get; set; } = string.Empty;
    }

    public sealed class SqlExportResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string DownloadFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/sql";
        public long FileSize
        {
            get; set;
        }

        public double DurationMilliseconds
        {
            get; set;
        }

        public DateTimeOffset GeneratedAtUtc
        {
            get; set;
        }
    }
}
