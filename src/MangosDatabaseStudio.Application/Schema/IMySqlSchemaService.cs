// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Application.Schema
{
    public interface IMySqlSchemaService
    {
        Task<IReadOnlyList<DatabaseMetadata>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken cancellationToken);
        Task<IReadOnlyList<TableMetadata>> GetTablesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken);
        Task<TableMetadata> GetTableAsync(ConnectionProfile profile, string database, string table, CancellationToken cancellationToken);
        Task<string> GetCreateTableSqlAsync(ConnectionProfile profile, string database, string table, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetEnginesAsync(ConnectionProfile profile, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetCollationsAsync(ConnectionProfile profile, CancellationToken cancellationToken);
    }
}
