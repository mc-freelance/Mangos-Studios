// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Security;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SqliteRolePermissionStore : IRolePermissionStore
    {
        private readonly ApplicationDatabase _database;

        // Initialize the sqlite role permission store dependencies and state.
        public SqliteRolePermissionStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Read all in encrypted local application storage.
        public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> GetAllAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, List<string>> stored = AppRoles.All.ToDictionary(
                role => role,
                _ => new List<string>(),
                StringComparer.Ordinal);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT role, permission FROM role_permissions ORDER BY role, permission;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string role = reader.GetString(0);
                string permission = reader.GetString(1);
                if (stored.TryGetValue(role, out List<string>? permissions))
                {
                    permissions.Add(permission);
                }
            }

            Dictionary<string, IReadOnlyCollection<string>> result = new(StringComparer.Ordinal);
            foreach (string role in AppRoles.All)
            {
                result[role] = stored[role].Count == 0
                    ? AppPermissions.GetDefaultPermissions(role).ToArray()
                    : AppPermissions.NormalizeForRole(role, stored[role]).ToArray();
            }

            return result;
        }

        // Read permissions in encrypted local application storage.
        public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(string role, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                return Array.Empty<string>();
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT permission FROM role_permissions WHERE role = $role ORDER BY permission;";
            command.Parameters.AddWithValue("$role", role);

            List<string> permissions = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                permissions.Add(reader.GetString(0));
            }

            return permissions.Count == 0
                ? AppPermissions.GetDefaultPermissions(role).ToArray()
                : AppPermissions.NormalizeForRole(role, permissions).ToArray();
        }

        // Determine whether permission in encrypted local application storage.
        public async Task<bool> HasPermissionAsync(string role, string permission, CancellationToken cancellationToken)
        {
            if (!AppPermissions.IsValid(permission))
            {
                return false;
            }

            IReadOnlyCollection<string> permissions = await GetPermissionsAsync(role, cancellationToken);
            return permissions.Contains(permission, StringComparer.Ordinal);
        }

        // Update  in encrypted local application storage.
        public Task UpdateAsync(string role, IEnumerable<string> permissions, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                throw new InvalidOperationException("Choose a valid application role.");
            }

            IReadOnlyCollection<string> normalized = AppPermissions.NormalizeForRole(role, permissions);
            return ReplaceAsync(role, normalized, cancellationToken);
        }

        // Reset  in encrypted local application storage.
        public Task ResetAsync(string role, CancellationToken cancellationToken)
        {
            if (!AppRoles.IsValid(role))
            {
                throw new InvalidOperationException("Choose a valid application role.");
            }

            return ReplaceAsync(role, AppPermissions.GetDefaultPermissions(role), cancellationToken);
        }

        // Handle replace async within the sqlite role permission store workflow.
        private async Task ReplaceAsync(
            string role,
            IEnumerable<string> permissions,
            CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                await using (SqliteCommand deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText = "DELETE FROM role_permissions WHERE role = $role;";
                    deleteCommand.Parameters.AddWithValue("$role", role);
                    await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (string permission in permissions.Distinct(StringComparer.Ordinal))
                {
                    await using SqliteCommand insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = "INSERT INTO role_permissions(role, permission) VALUES ($role, $permission);";
                    insertCommand.Parameters.AddWithValue("$role", role);
                    insertCommand.Parameters.AddWithValue("$permission", permission);
                    await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
