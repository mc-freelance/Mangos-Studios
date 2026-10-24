// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MangosDatabaseStudio.App.Services
{
    public interface IApplicationUserStore
    {
        Task<bool> AnyAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<ApplicationUser>> GetAllAsync(CancellationToken cancellationToken);
        Task<ApplicationUser?> GetAsync(Guid id, CancellationToken cancellationToken);
        Task<ApplicationUser?> FindAsync(string username, CancellationToken cancellationToken);
        Task<ApplicationUser?> ValidateAsync(string username, string password, CancellationToken cancellationToken);
        Task CreateAdministratorAsync(string username, string password, CancellationToken cancellationToken);
        Task CreateAsync(string username, string password, string role, CancellationToken cancellationToken);
        Task UpdateAsync(Guid id, string username, string role, string? newPassword, CancellationToken cancellationToken);
        Task UpdateProfileAsync(Guid id, string email, string avatarMode, string? avatarFileName, CancellationToken cancellationToken);
        Task UpdatePreferencesAsync(Guid id, UserPreferences preferences, CancellationToken cancellationToken);
        Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken);
        Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    }
}
