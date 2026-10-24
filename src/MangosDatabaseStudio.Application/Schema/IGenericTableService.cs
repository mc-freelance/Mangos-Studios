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
    public interface IGenericTableService
    {
        Task<PagedResult<TableRow>> BrowseAsync(ConnectionProfile profile, TableMetadata table, int page, int pageSize, int displayColumnCount, string? search, string? sort, bool descending, CancellationToken cancellationToken);
        Task<TableRow?> GetRowAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, CancellationToken cancellationToken);
        Task<long?> InsertAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> values, ISet<string> nullColumns, CancellationToken cancellationToken);
        Task UpdateAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, IReadOnlyDictionary<string, string?> values, ISet<string> nullColumns, CancellationToken cancellationToken);
        Task DeleteAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, CancellationToken cancellationToken);
    }
}
