// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    public sealed class MySqlOptions
    {
        public const string SectionName = "MySql";
        public int CommandTimeoutSeconds { get; set; } = 60;
        public int MaximumBrowsePageSize { get; set; } = 250;
        public int MaximumSqlRows { get; set; } = 1000;
        public string ImportClientPath { get; set; } = "mysql";
        public string ExportClientPath { get; set; } = "mysqldump";
    }
}
