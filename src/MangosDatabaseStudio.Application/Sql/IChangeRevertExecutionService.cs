// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.Application.Sql
{
    public interface IChangeRevertExecutionService
    {
        Task<int> ExecuteAsync(
            ConnectionProfile profile,
            string database,
            string sql,
            int expectedAffectedRows,
            CancellationToken cancellationToken);
    }
}
