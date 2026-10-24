// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Record, review, and revert change history for database edits.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public static class StoredChangeStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Ignored = "ignored";

        // Determine whether valid according to the available model or service contract.
        public static bool IsValid(string? value)
        {
            return string.Equals(value, Pending, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, Approved, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, Ignored, StringComparison.OrdinalIgnoreCase);
        }

        // Normalize a stored value for stable comparisons and display.
        public static string Normalize(string? value)
        {
            if (string.Equals(value, Approved, StringComparison.OrdinalIgnoreCase))
            {
                return Approved;
            }

            if (string.Equals(value, Ignored, StringComparison.OrdinalIgnoreCase))
            {
                return Ignored;
            }

            return Pending;
        }
    }

    public static class StoredChangeSources
    {
        public const string WorldEditor = "World editor";
        public const string NpcVendorEditor = "NPC vendor editor";
        public const string NpcTrainerEditor = "NPC trainer editor";
        public const string PoolViewer = "Pool viewer";
        public const string LootViewer = "Loot viewer";
        public const string EventViewer = "Event viewer";
        public const string QuestViewer = "Quest viewer";
        public const string GossipMenus = "Gossip menus";
        public const string SqlConsole = "SQL Console";
        public const string SqlImport = "SQL Import";
        public const string DatabaseUpdates = "Database Updates";
        public const string SchemaEditor = "Schema editor";
    }

    public sealed class StoredDatabaseChange
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId
        {
            get; set;
        }

        public string Username { get; set; } = string.Empty;
        public Guid ConnectionId
        {
            get; set;
        }

        public string ConnectionName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SqlText { get; set; } = string.Empty;
        public string RevertSqlText { get; set; } = string.Empty;
        public int RevertExpectedRows
        {
            get; set;
        }

        public bool RevertAvailable => !string.IsNullOrWhiteSpace(RevertSqlText) && RevertExpectedRows > 0;
        public bool ContributionEligible
        {
            get; set;
        }

        public string ReviewStatus { get; set; } = StoredChangeStatuses.Pending;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAtUtc
        {
            get; set;
        }

        public Guid? ReviewedByUserId
        {
            get; set;
        }

        public string ReviewedByUsername { get; set; } = string.Empty;
    }

    public sealed class ChangeStorageCounts
    {
        public int Total
        {
            get; set;
        }

        public int Pending
        {
            get; set;
        }

        public int Approved
        {
            get; set;
        }

        public int Ignored
        {
            get; set;
        }

        public int ContributionReady
        {
            get; set;
        }
    }

    public sealed class ChangeStoragePage
    {
        public IReadOnlyList<StoredDatabaseChange> Items { get; set; } = Array.Empty<StoredDatabaseChange>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalCount
        {
            get; set;
        }

        public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed class ChangeStorageQuery
    {
        public Guid ConnectionId
        {
            get; set;
        }

        public string DatabaseName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public interface IChangeStorageStore
    {
        Task RecordAsync(StoredDatabaseChange change, CancellationToken cancellationToken);
        Task<ChangeStoragePage> SearchAsync(ChangeStorageQuery query, CancellationToken cancellationToken);
        Task<ChangeStorageCounts> GetCountsAsync(Guid connectionId, string? databaseName, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetDatabaseNamesAsync(Guid connectionId, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> GetSourcesAsync(Guid connectionId, CancellationToken cancellationToken);
        Task<IReadOnlyList<StoredDatabaseChange>> GetByIdsAsync(Guid connectionId, IEnumerable<Guid> ids, CancellationToken cancellationToken);
        Task DeleteAsync(Guid connectionId, Guid id, CancellationToken cancellationToken);
        Task UpdateStatusAsync(Guid connectionId, IEnumerable<Guid> ids, string status, Guid reviewerId, string reviewerName, CancellationToken cancellationToken);
    }

    public sealed class SqliteChangeStorageStore : IChangeStorageStore
    {
        private const int MaximumSqlLength = 1_000_000;
        private readonly ApplicationDatabase _database;

        // Prepare the sqlite change storage store statement with schema-aware column handling.
        public SqliteChangeStorageStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Handle record async within the change storage workflow.
        public async Task RecordAsync(StoredDatabaseChange change, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(change);

            if (change.ConnectionId == Guid.Empty)
            {
                throw new ArgumentException("A connection is required for stored database changes.", nameof(change));
            }

            if (string.IsNullOrWhiteSpace(change.DatabaseName))
            {
                throw new ArgumentException("A database name is required for stored database changes.", nameof(change));
            }

            change.Id = change.Id == Guid.Empty ? Guid.NewGuid() : change.Id;
            change.Username = Normalize(change.Username, 160);
            change.ConnectionName = Normalize(change.ConnectionName, 200);
            change.DatabaseName = Normalize(change.DatabaseName, 160);
            change.Source = Normalize(change.Source, 120);
            change.Operation = Normalize(change.Operation, 64);
            change.ObjectName = Normalize(change.ObjectName, 256);
            change.Summary = Normalize(change.Summary, 1000);
            change.ReviewStatus = StoredChangeStatuses.Normalize(change.ReviewStatus);
            change.ReviewedByUsername = Normalize(change.ReviewedByUsername, 160);

            string sql = (change.SqlText ?? string.Empty).Trim();
            if (sql.Length > MaximumSqlLength)
            {
                sql = sql[..MaximumSqlLength];
                change.ContributionEligible = false;
                change.Summary = Normalize(change.Summary + " SQL text exceeded the Change Storage limit and was truncated.", 1000);
            }

            change.SqlText = sql;
            if (string.IsNullOrWhiteSpace(sql))
            {
                change.ContributionEligible = false;
            }

            string revertSql = (change.RevertSqlText ?? string.Empty).Trim();
            if (revertSql.Length > MaximumSqlLength)
            {
                revertSql = string.Empty;
                change.RevertExpectedRows = 0;
                change.Summary = Normalize(change.Summary + " Automatic revert SQL exceeded the Change Storage limit and was not retained.", 1000);
            }

            change.RevertSqlText = revertSql;
            change.RevertExpectedRows = string.IsNullOrWhiteSpace(revertSql) ? 0 : Math.Max(0, change.RevertExpectedRows);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO database_changes (
    id, user_id, username, connection_id, connection_name, database_name,
    source, operation, object_name, summary, sql_text, revert_sql_text, revert_expected_rows, contribution_eligible,
    review_status, created_at_utc, reviewed_at_utc, reviewed_by_user_id, reviewed_by_username)
VALUES (
    $id, $userId, $username, $connectionId, $connectionName, $databaseName,
    $source, $operation, $objectName, $summary, $sqlText, $revertSqlText, $revertExpectedRows, $eligible,
    $status, $createdAt, $reviewedAt, $reviewedByUserId, $reviewedByUsername);";
            command.Parameters.AddWithValue("$id", change.Id.ToString("D"));
            command.Parameters.AddWithValue("$userId", change.UserId == Guid.Empty ? string.Empty : change.UserId.ToString("D"));
            command.Parameters.AddWithValue("$username", change.Username);
            command.Parameters.AddWithValue("$connectionId", change.ConnectionId.ToString("D"));
            command.Parameters.AddWithValue("$connectionName", change.ConnectionName);
            command.Parameters.AddWithValue("$databaseName", change.DatabaseName);
            command.Parameters.AddWithValue("$source", change.Source);
            command.Parameters.AddWithValue("$operation", change.Operation);
            command.Parameters.AddWithValue("$objectName", change.ObjectName);
            command.Parameters.AddWithValue("$summary", change.Summary);
            command.Parameters.AddWithValue("$sqlText", change.SqlText);
            command.Parameters.AddWithValue("$revertSqlText", change.RevertSqlText);
            command.Parameters.AddWithValue("$revertExpectedRows", change.RevertExpectedRows);
            command.Parameters.AddWithValue("$eligible", change.ContributionEligible ? 1 : 0);
            command.Parameters.AddWithValue("$status", change.ReviewStatus);
            command.Parameters.AddWithValue("$createdAt", change.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$reviewedAt", change.ReviewedAtUtc.HasValue ? change.ReviewedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
            command.Parameters.AddWithValue("$reviewedByUserId", change.ReviewedByUserId.HasValue ? change.ReviewedByUserId.Value.ToString("D") : (object)DBNull.Value);
            command.Parameters.AddWithValue("$reviewedByUsername", change.ReviewedByUsername);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Search for  according to the available model or service contract.
        public async Task<ChangeStoragePage> SearchAsync(ChangeStorageQuery query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            int page = Math.Max(1, query.Page);
            int pageSize = Math.Clamp(query.PageSize, 10, 100);
            (string where, List<SqliteParameter> parameters) = BuildWhere(query.ConnectionId, query.DatabaseName, query.Status, query.Source, query.Search);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);

            int totalCount;
            await using (SqliteCommand countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM database_changes " + where + ";";
                AddParameters(countCommand, parameters);
                totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            List<StoredDatabaseChange> items = new();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, user_id, username, connection_id, connection_name, database_name,
       source, operation, object_name, summary, sql_text, revert_sql_text, revert_expected_rows, contribution_eligible,
       review_status, created_at_utc, reviewed_at_utc, reviewed_by_user_id, reviewed_by_username
FROM database_changes " + where + @"
ORDER BY created_at_utc DESC, id DESC
LIMIT $limit OFFSET $offset;";
                AddParameters(command, parameters);
                command.Parameters.AddWithValue("$limit", pageSize);
                command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadChange(reader));
                }
            }

            return new ChangeStoragePage
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        // Read counts according to the available model or service contract.
        public async Task<ChangeStorageCounts> GetCountsAsync(Guid connectionId, string? databaseName, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    COUNT(*) AS total,
    SUM(CASE WHEN review_status = 'pending' THEN 1 ELSE 0 END) AS pending,
    SUM(CASE WHEN review_status = 'approved' THEN 1 ELSE 0 END) AS approved,
    SUM(CASE WHEN review_status = 'ignored' THEN 1 ELSE 0 END) AS ignored,
    SUM(CASE WHEN review_status = 'approved' AND contribution_eligible = 1 AND length(trim(sql_text)) > 0 THEN 1 ELSE 0 END) AS contribution_ready
FROM database_changes
WHERE connection_id = $connectionId
  AND ($databaseName = '' OR database_name = $databaseName COLLATE NOCASE);";
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
            command.Parameters.AddWithValue("$databaseName", (databaseName ?? string.Empty).Trim());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new ChangeStorageCounts();
            }

            return new ChangeStorageCounts
            {
                Total = ReadInt(reader, 0),
                Pending = ReadInt(reader, 1),
                Approved = ReadInt(reader, 2),
                Ignored = ReadInt(reader, 3),
                ContributionReady = ReadInt(reader, 4)
            };
        }

        // Read database names according to the available model or service contract.
        public Task<IReadOnlyList<string>> GetDatabaseNamesAsync(Guid connectionId, CancellationToken cancellationToken)
        {
            return GetDistinctAsync(connectionId, "database_name", cancellationToken);
        }

        // Read sources according to the available model or service contract.
        public Task<IReadOnlyList<string>> GetSourcesAsync(Guid connectionId, CancellationToken cancellationToken)
        {
            return GetDistinctAsync(connectionId, "source", cancellationToken);
        }

        // Read by ids according to the available model or service contract.
        public async Task<IReadOnlyList<StoredDatabaseChange>> GetByIdsAsync(
            Guid connectionId,
            IEnumerable<Guid> ids,
            CancellationToken cancellationToken)
        {
            Guid[] distinctIds = (ids ?? Array.Empty<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(250)
                .ToArray();
            if (distinctIds.Length == 0)
            {
                return Array.Empty<StoredDatabaseChange>();
            }

            List<StoredDatabaseChange> items = new();
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            foreach (Guid id in distinctIds)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, user_id, username, connection_id, connection_name, database_name,
       source, operation, object_name, summary, sql_text, revert_sql_text, revert_expected_rows, contribution_eligible,
       review_status, created_at_utc, reviewed_at_utc, reviewed_by_user_id, reviewed_by_username
FROM database_changes
WHERE id = $id AND connection_id = $connectionId
LIMIT 1;";
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadChange(reader));
                }
            }

            return items
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToArray();
        }

        // Delete  according to the available model or service contract.
        public async Task DeleteAsync(Guid connectionId, Guid id, CancellationToken cancellationToken)
        {
            if (connectionId == Guid.Empty || id == Guid.Empty)
            {
                return;
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM database_changes WHERE id = $id AND connection_id = $connectionId;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update status according to the available model or service contract.
        public async Task UpdateStatusAsync(Guid connectionId, IEnumerable<Guid> ids, string status, Guid reviewerId, string reviewerName, CancellationToken cancellationToken)
        {
            string normalizedStatus = StoredChangeStatuses.Normalize(status);
            Guid[] distinctIds = (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().Take(250).ToArray();
            if (distinctIds.Length == 0)
            {
                return;
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                foreach (Guid id in distinctIds)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE database_changes
SET review_status = $status,
    reviewed_at_utc = $reviewedAt,
    reviewed_by_user_id = $reviewerId,
    reviewed_by_username = $reviewerName
WHERE id = $id AND connection_id = $connectionId;";
                    command.Parameters.AddWithValue("$status", normalizedStatus);
                    command.Parameters.AddWithValue("$reviewedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$reviewerId", reviewerId == Guid.Empty ? string.Empty : reviewerId.ToString("D"));
                    command.Parameters.AddWithValue("$reviewerName", Normalize(reviewerName, 160));
                    command.Parameters.AddWithValue("$id", id.ToString("D"));
                    command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Read distinct according to the available model or service contract.
        private async Task<IReadOnlyList<string>> GetDistinctAsync(Guid connectionId, string columnName, CancellationToken cancellationToken)
        {
            if (columnName != "database_name" && columnName != "source")
            {
                throw new ArgumentOutOfRangeException(nameof(columnName));
            }

            List<string> values = new();
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT " + columnName + " FROM database_changes WHERE connection_id = $connectionId AND length(trim(" + columnName + ")) > 0 ORDER BY " + columnName + " COLLATE NOCASE;";
            command.Parameters.AddWithValue("$connectionId", connectionId.ToString("D"));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        // Build where according to the available model or service contract.
        private static (string Where, List<SqliteParameter> Parameters) BuildWhere(Guid connectionId, string? databaseName, string? status, string? source, string? search)
        {
            StringBuilder where = new("WHERE connection_id = $connectionId");
            SqliteParameter sqliteParameter = new("$connectionId", connectionId.ToString("D"));
            List<SqliteParameter> parameters = new()
            {
                sqliteParameter
            };

            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                where.Append(" AND database_name = $databaseName COLLATE NOCASE");
                parameters.Add(new SqliteParameter("$databaseName", databaseName.Trim()));
            }

            if (StoredChangeStatuses.IsValid(status))
            {
                where.Append(" AND review_status = $status");
                parameters.Add(new SqliteParameter("$status", StoredChangeStatuses.Normalize(status)));
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                where.Append(" AND source = $source COLLATE NOCASE");
                parameters.Add(new SqliteParameter("$source", source.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Append(" AND (summary LIKE $search ESCAPE '\\' OR object_name LIKE $search ESCAPE '\\' OR username LIKE $search ESCAPE '\\' OR sql_text LIKE $search ESCAPE '\\')");
                string escaped = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
                parameters.Add(new SqliteParameter("$search", "%" + escaped + "%"));
            }

            return (where.ToString(), parameters);
        }

        // Add parameters according to the available model or service contract.
        private static void AddParameters(SqliteCommand command, IEnumerable<SqliteParameter> parameters)
        {
            foreach (SqliteParameter parameter in parameters)
            {
                command.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
            }
        }

        // Read change according to the available model or service contract.
        private static StoredDatabaseChange ReadChange(SqliteDataReader reader)
        {
            return new StoredDatabaseChange
            {
                Id = ParseGuid(reader.GetString(0)),
                UserId = ParseGuid(reader.GetString(1)),
                Username = reader.GetString(2),
                ConnectionId = ParseGuid(reader.GetString(3)),
                ConnectionName = reader.GetString(4),
                DatabaseName = reader.GetString(5),
                Source = reader.GetString(6),
                Operation = reader.GetString(7),
                ObjectName = reader.GetString(8),
                Summary = reader.GetString(9),
                SqlText = reader.GetString(10),
                RevertSqlText = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                RevertExpectedRows = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                ContributionEligible = reader.GetInt32(13) == 1,
                ReviewStatus = reader.GetString(14),
                CreatedAtUtc = ParseDate(reader.GetString(15)),
                ReviewedAtUtc = reader.IsDBNull(16) ? null : ParseDate(reader.GetString(16)),
                ReviewedByUserId = reader.IsDBNull(17) ? null : ParseGuid(reader.GetString(17)),
                ReviewedByUsername = reader.IsDBNull(18) ? string.Empty : reader.GetString(18)
            };
        }

        // Normalize a stored value for stable comparisons and display.
        private static string Normalize(string? value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
        }

        // Parse guid according to the available model or service contract.
        private static Guid ParseGuid(string? value)
        {
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }

        // Parse date according to the available model or service contract.
        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date)
                ? date.ToUniversalTime()
                : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }

        // Read int according to the available model or service contract.
        private static int ReadInt(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }
    }
}
