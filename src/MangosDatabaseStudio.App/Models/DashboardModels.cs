// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class DashboardViewModel
    {
        public ConnectionProfile? ActiveConnection
        {
            get; set;
        }

        public IReadOnlyList<DatabaseMetadata> Databases { get; set; } = new List<DatabaseMetadata>();
        public IReadOnlyList<TableMetadata> DefaultDatabaseTables { get; set; } = new List<TableMetadata>();
        public string? Error
        {
            get; set;
        }

        public bool CanBrowseWorld
        {
            get; set;
        }

        public bool CanBrowseDatabases
        {
            get; set;
        }

        public bool CanUseSqlConsole
        {
            get; set;
        }

        public bool CanUseSqlImport
        {
            get; set;
        }

        public bool CanUseSqlExport
        {
            get; set;
        }

        public bool CanViewConnections
        {
            get; set;
        }

        public bool CanSelectConnections
        {
            get; set;
        }
    }
}
