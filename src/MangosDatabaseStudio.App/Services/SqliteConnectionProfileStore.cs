// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Domain.Connections;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SqliteConnectionProfileStore : IConnectionProfileStore
    {
        private readonly ApplicationDatabase _database;

        // Initialize the sqlite connection profile store dependencies and state.
        public SqliteConnectionProfileStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Read saved database connections from protected local storage.
        public async Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SelectSql + " ORDER BY name COLLATE NOCASE;";
            return await ReadProfilesAsync(command, cancellationToken);
        }

        // Load a stored connection profile by its identifier.
        public async Task<ConnectionProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SelectSql + " WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            IReadOnlyList<ConnectionProfile> profiles = await ReadProfilesAsync(command, cancellationToken);
            return profiles.Count == 0 ? null : profiles[0];
        }

        // Add a new connection profile to encrypted application storage.
        public async Task CreateAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (profile.Id == Guid.Empty)
            {
                profile.Id = Guid.NewGuid();
            }

            profile.CreatedAtUtc = profile.CreatedAtUtc == default ? now : profile.CreatedAtUtc;
            profile.UpdatedAtUtc = now;

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO connection_profiles(
    id, name, host, port, username, password, default_database, ssl_mode, created_at_utc, updated_at_utc)
VALUES (
    $id, $name, $host, $port, $username, $password, $defaultDatabase, $sslMode, $createdAtUtc, $updatedAtUtc);";
            AddParameters(command, profile, includeCreatedAt: true);

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException("The connection profile was not created.");
            }
        }

        // Update the selected connection profile using parameterized SQLite statements.
        public async Task<bool> UpdateAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            if (profile.Id == Guid.Empty)
            {
                throw new ArgumentException("An existing connection profile requires an ID.", nameof(profile));
            }

            profile.UpdatedAtUtc = DateTime.UtcNow;

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE connection_profiles
SET name = $name,
    host = $host,
    port = $port,
    username = $username,
    password = $password,
    default_database = $defaultDatabase,
    ssl_mode = $sslMode,
    updated_at_utc = $updatedAtUtc
WHERE id = $id;";
            AddParameters(command, profile, includeCreatedAt: false);

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected == 1;
        }

        // Delete  in encrypted local application storage.
        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM connection_profiles WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Add parameters in encrypted local application storage.
        private static void AddParameters(SqliteCommand command, ConnectionProfile profile, bool includeCreatedAt)
        {
            command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", profile.Name ?? string.Empty);
            command.Parameters.AddWithValue("$host", profile.Host ?? string.Empty);
            command.Parameters.AddWithValue("$port", (long)profile.Port);
            command.Parameters.AddWithValue("$username", profile.Username ?? string.Empty);
            command.Parameters.AddWithValue("$password", profile.Password ?? string.Empty);
            command.Parameters.AddWithValue("$defaultDatabase", profile.DefaultDatabase ?? string.Empty);
            command.Parameters.AddWithValue("$sslMode", profile.SslMode ?? "Preferred");
            if (includeCreatedAt)
            {
                command.Parameters.AddWithValue("$createdAtUtc", profile.CreatedAtUtc.ToUniversalTime().ToString("O"));
            }

            command.Parameters.AddWithValue("$updatedAtUtc", profile.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        }

        // Read profiles in encrypted local application storage.
        private static async Task<IReadOnlyList<ConnectionProfile>> ReadProfilesAsync(
            SqliteCommand command,
            CancellationToken cancellationToken)
        {
            List<ConnectionProfile> profiles = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long portValue = reader.GetInt64(3);
                profiles.Add(new ConnectionProfile
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    Host = reader.GetString(2),
                    Port = portValue < 0 ? 0u : portValue > uint.MaxValue ? uint.MaxValue : (uint)portValue,
                    Username = reader.GetString(4),
                    Password = reader.GetString(5),
                    DefaultDatabase = reader.GetString(6),
                    SslMode = reader.GetString(7),
                    CreatedAtUtc = ParseUtc(reader.GetString(8)),
                    UpdatedAtUtc = ParseUtc(reader.GetString(9))
                });
            }

            return profiles;
        }

        // Parse utc in encrypted local application storage.
        private static DateTime ParseUtc(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.UtcNow;
        }

        private const string SelectSql = @"
SELECT id, name, host, port, username, password, default_database, ssl_mode, created_at_utc, updated_at_utc
FROM connection_profiles";
    }
}
