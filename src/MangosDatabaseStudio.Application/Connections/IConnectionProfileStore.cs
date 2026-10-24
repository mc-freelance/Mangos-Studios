// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Connection abstractions and profile storage contracts.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.Application.Connections
{
    public interface IConnectionProfileStore
    {
        Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken);
        Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken);
        Task CreateAsync(ConnectionProfile profile, CancellationToken cancellationToken);
        Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken cancellationToken);
        Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    }
}
