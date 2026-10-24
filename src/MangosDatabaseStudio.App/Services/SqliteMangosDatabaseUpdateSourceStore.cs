// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.DatabaseUpdates;
using MangosDatabaseStudio.Domain.DatabaseUpdates;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SqliteMangosDatabaseUpdateSourceStore : IMangosDatabaseUpdateSourceStore
    {
        private readonly ApplicationDatabase _database;

        // Initialize the sqlite mangos database update source store dependencies and state.
        public SqliteMangosDatabaseUpdateSourceStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Read  in encrypted local application storage.
        public async Task<MangosDatabaseUpdateSource?> GetAsync(Guid connectionId, string databaseName, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT connection_id, database_name, source_directory, updated_at_utc
FROM database_update_sources
WHERE connection_id = $connectionId AND database_name = $databaseName COLLATE NOCASE
LIMIT 1;";
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
            command.Parameters.AddWithValue("$databaseName", databaseName ?? string.Empty);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadSource(reader);
        }

        // Read for connection in encrypted local application storage.
        public async Task<IReadOnlyList<MangosDatabaseUpdateSource>> GetForConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT connection_id, database_name, source_directory, updated_at_utc
FROM database_update_sources
WHERE connection_id = $connectionId
ORDER BY database_name COLLATE NOCASE;";
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));

            List<MangosDatabaseUpdateSource> sources = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sources.Add(ReadSource(reader));
            }

            return sources;
        }

        // Persist  in encrypted local application storage.
        public async Task SaveAsync(MangosDatabaseUpdateSource source, CancellationToken cancellationToken)
        {
            source.UpdatedAtUtc = DateTime.UtcNow;
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO database_update_sources(connection_id, database_name, source_directory, updated_at_utc)
VALUES ($connectionId, $databaseName, $sourceDirectory, $updatedAtUtc)
ON CONFLICT(connection_id, database_name) DO UPDATE SET
    source_directory = excluded.source_directory,
    updated_at_utc = excluded.updated_at_utc;";
            command.Parameters.AddWithValue("$connectionId", source.ConnectionId.ToString("D"));
            command.Parameters.AddWithValue("$databaseName", source.DatabaseName ?? string.Empty);
            command.Parameters.AddWithValue("$sourceDirectory", source.SourceDirectory ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAtUtc", source.UpdatedAtUtc.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Delete  in encrypted local application storage.
        public async Task DeleteAsync(Guid connectionId, string databaseName, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM database_update_sources WHERE connection_id = $connectionId AND database_name = $databaseName COLLATE NOCASE;";
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
            command.Parameters.AddWithValue("$databaseName", databaseName ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Read source in encrypted local application storage.
        private static MangosDatabaseUpdateSource ReadSource(SqliteDataReader reader)
        {
            DateTime updatedAt = DateTime.UtcNow;
            string rawDate = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                updatedAt = parsed.ToUniversalTime();
            }

            return new MangosDatabaseUpdateSource
            {
                ConnectionId = Guid.Parse(reader.GetString(0)),
                DatabaseName = reader.GetString(1),
                SourceDirectory = reader.GetString(2),
                UpdatedAtUtc = updatedAt
            };
        }
    }
}
