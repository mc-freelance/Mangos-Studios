// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Connection abstractions and profile storage contracts.

using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.Application.Connections
{
    public interface IConnectionTester
    {
        Task<string> TestAsync(ConnectionProfile profile, CancellationToken cancellationToken);
    }
}
