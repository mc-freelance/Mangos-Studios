// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class ChangeRevertExecutionService : IChangeRevertExecutionService
    {
        private static readonly Regex AllowedStart = new(@"^\s*(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the change revert execution service dependencies and state.
        public ChangeRevertExecutionService(
            MySqlConnectionFactory connectionFactory,
            MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Execute  using schema-aware database operations.
        public async Task<int> ExecuteAsync(
            ConnectionProfile profile,
            string database,
            string sql,
            int expectedAffectedRows,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(sql);

            string statement = (sql ?? string.Empty).Trim();
            if (!AllowedStart.IsMatch(statement))
            {
                throw new InvalidOperationException("The stored revert statement is not an allowed INSERT, UPDATE, or DELETE operation.");
            }

            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using MySqlCommand command = new(statement, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds
                };

                int affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected != expectedAffectedRows)
                {
                    throw new InvalidOperationException(
                        "The database no longer matches the stored post-change state. " +
                        "Expected to revert " + expectedAffectedRows + " row(s), but the generated rollback matched " + affected + ". " +
                        "No rollback changes were committed.");
                }

                await transaction.CommitAsync(cancellationToken);
                return affected;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
