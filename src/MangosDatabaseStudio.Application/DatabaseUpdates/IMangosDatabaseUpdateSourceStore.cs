// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MaNGOS database update discovery and execution contracts.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.DatabaseUpdates;

namespace MangosDatabaseStudio.Application.DatabaseUpdates
{
    public interface IMangosDatabaseUpdateSourceStore
    {
        Task<MangosDatabaseUpdateSource?> GetAsync(Guid connectionId, string databaseName, CancellationToken cancellationToken);
        Task<IReadOnlyList<MangosDatabaseUpdateSource>> GetForConnectionAsync(Guid connectionId, CancellationToken cancellationToken);
        Task SaveAsync(MangosDatabaseUpdateSource source, CancellationToken cancellationToken);
        Task DeleteAsync(Guid connectionId, string databaseName, CancellationToken cancellationToken);
    }
}
