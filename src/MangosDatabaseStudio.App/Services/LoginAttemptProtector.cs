// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Persist authentication throttling and security audit events in encrypted SQLite storage.

using System;
using System.Globalization;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class LoginAttemptProtector(
        ApplicationDatabase database,
        IApplicationUserStore users,
        ILogger<LoginAttemptProtector> logger,
        TimeProvider clock)
    {
        private readonly ApplicationDatabase _database = database;
        private readonly IApplicationUserStore _users = users;
        private readonly ILogger<LoginAttemptProtector> _logger = logger;
        private readonly TimeProvider _clock = clock;
        private readonly SemaphoreSlim _gate = new(1, 1);

        // Authenticate within the rate-limit boundary to avoid concurrent request races.
        public async Task<LoginAttemptResult> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            string key = LoginAttemptPolicy.NormalizeUsername(username);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                DateTimeOffset now = _clock.GetUtcNow();
                await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
                (int failures, DateTimeOffset? lastFailure, DateTimeOffset? blockedUntil) =
                    await ReadStateAsync(connection, key, cancellationToken);

                if (blockedUntil > now)
                {
                    return new LoginAttemptResult(null, blockedUntil.Value - now);
                }

                // Expire an abandoned failure streak after 15 minutes without a failure.
                if (lastFailure.HasValue && now - lastFailure.Value > TimeSpan.FromMinutes(15))
                {
                    failures = 0;
                }

                ApplicationUser? user = await _users.ValidateAsync(username, password, cancellationToken);
                if (user is not null)
                {
                    // Update throttling state and the audit event as one SQLite transaction.
                    await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
                    await ClearStateAsync(connection, transaction, key, cancellationToken);
                    await WriteAuditAsync(connection, transaction, key, user.Id, "success", now, cancellationToken);
                    transaction.Commit();
                    _logger.LogInformation("Successful local sign-in for account {Account}", key);
                    return new LoginAttemptResult(user, TimeSpan.Zero);
                }

                int nextFailures = Math.Min(failures + 1, 30);
                TimeSpan delay = LoginAttemptPolicy.GetDelay(nextFailures);
                // A failed audit write must never result in a partially recorded attempt.
                await using SqliteTransaction failureTransaction = connection.BeginTransaction(IsolationLevel.Serializable);
                await SaveFailureAsync(connection, failureTransaction, key, nextFailures, now, now + delay, cancellationToken);
                await WriteAuditAsync(connection, failureTransaction, key, null, "failure", now, cancellationToken);
                failureTransaction.Commit();
                _logger.LogWarning("Failed local sign-in for account {Account}", key);
                return new LoginAttemptResult(null, delay);
            }
            finally
            {
                _gate.Release();
            }
        }

        // Read the persisted authentication state for the normalized account name.
        private static async Task<(int Failures, DateTimeOffset? LastFailure, DateTimeOffset? BlockedUntil)> ReadStateAsync(
            SqliteConnection connection,
            string username,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT failure_count, last_failure_utc, blocked_until_utc
FROM authentication_throttle
WHERE username = $username LIMIT 1;";
            command.Parameters.AddWithValue("$username", username);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return (0, null, null);
            }

            DateTimeOffset? lastFailure = DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset lastValue) ? lastValue : null;
            DateTimeOffset? blockedUntil = DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset blockedValue) ? blockedValue : null;
            return (reader.GetInt32(0), lastFailure, blockedUntil);
        }

        // Reset the account's failure streak after successful authentication.
        private static async Task ClearStateAsync(SqliteConnection connection, SqliteTransaction transaction, string username, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM authentication_throttle WHERE username = $username;";
            command.Parameters.AddWithValue("$username", username);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Upsert an account-specific cooldown using parameterized SQL.
        private static async Task SaveFailureAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string username,
            int failures,
            DateTimeOffset now,
            DateTimeOffset blockedUntil,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO authentication_throttle(username, failure_count, last_failure_utc, blocked_until_utc)
VALUES ($username, $failures, $now, $until)
ON CONFLICT(username) DO UPDATE SET
    failure_count = excluded.failure_count,
    last_failure_utc = excluded.last_failure_utc,
    blocked_until_utc = excluded.blocked_until_utc;";
            command.Parameters.AddWithValue("$username", username);
            command.Parameters.AddWithValue("$failures", failures);
            command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$until", blockedUntil.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Store bounded audit history without passwords, session tokens, or credentials.
        private static async Task WriteAuditAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string username,
            Guid? userId,
            string result,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO authentication_audit(username, user_id, outcome, occurred_at_utc)
VALUES ($username, $userId, $outcome, $now);
DELETE FROM authentication_audit WHERE id IN (
    SELECT id FROM authentication_audit ORDER BY id DESC LIMIT -1 OFFSET 5000
);
DELETE FROM authentication_audit WHERE occurred_at_utc < $cutoff;
DELETE FROM authentication_throttle WHERE last_failure_utc < $cutoff;";
            command.Parameters.AddWithValue("$username", username);
            command.Parameters.AddWithValue("$userId", userId.HasValue ? (object)userId.Value.ToString("D") : DBNull.Value);
            command.Parameters.AddWithValue("$outcome", result);
            command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$cutoff", (now - TimeSpan.FromDays(90)).ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public sealed record LoginAttemptResult(ApplicationUser? User, TimeSpan RetryAfter);

    public static class LoginAttemptPolicy
    {
        // Treat account names consistently and bound storage for unknown account guesses.
        public static string NormalizeUsername(string? username)
        {
            string normalized = (username ?? string.Empty).Trim();
            if (normalized.Length > 64)
            {
                return "[invalid username length]";
            }

            return normalized.Replace('\r', '_').Replace('\n', '_').ToUpperInvariant();
        }

        // Apply increasing cooldowns after repeated failures, capped at five minutes.
        public static TimeSpan GetDelay(int failures)
        {
            if (failures < 3)
            {
                return TimeSpan.Zero;
            }

            int seconds = (int)Math.Min(300, Math.Pow(2, Math.Min(failures - 2, 16)));
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
