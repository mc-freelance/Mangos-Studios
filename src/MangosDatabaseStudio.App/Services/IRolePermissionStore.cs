// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MangosDatabaseStudio.App.Services
{
    public interface IRolePermissionStore
    {
        Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetAllAsync(CancellationToken cancellationToken);
        Task<IReadOnlyCollection<string>> GetPermissionsAsync(string role, CancellationToken cancellationToken);
        Task<bool> HasPermissionAsync(string role, string permission, CancellationToken cancellationToken);
        Task UpdateAsync(string role, IEnumerable<string> permissions, CancellationToken cancellationToken);
        Task ResetAsync(string role, CancellationToken cancellationToken);
    }
}
