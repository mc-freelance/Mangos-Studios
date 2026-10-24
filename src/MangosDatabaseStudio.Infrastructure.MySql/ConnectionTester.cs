// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class ConnectionTester : IConnectionTester
    {
        private readonly MySqlConnectionFactory _connectionFactory;

        // Initialize the connection tester dependencies and state.
        public ConnectionTester(MySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Handle test async within the connection tester workflow.
        public async Task<string> TestAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new("SELECT VERSION();", connection);
            object? version = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToString(version) ?? "Connected";
        }
    }
}
