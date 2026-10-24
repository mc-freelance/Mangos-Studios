// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Create MySQL connections from configured, protected connection profiles.

using System;
using MangosDatabaseStudio.Domain.Connections;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql.Internal
{
    internal sealed class MySqlConnectionFactory
    {
        private readonly MySqlOptions _options;

        // Initialize the my sql connection factory dependencies and state.
        public MySqlConnectionFactory(MySqlOptions options)
        {
            _options = options;
        }

        // Build a field definition from its seed metadata and expansion variants.
        public MySqlConnection Create(ConnectionProfile profile, string? database = null)
        {
            MySqlConnectionStringBuilder builder = new()
            {
                Server = profile.Host,
                Port = profile.Port,
                UserID = profile.Username,
                Password = profile.Password,
                Database = database ?? string.Empty,
                ConnectionTimeout = 10,
                DefaultCommandTimeout = (uint)Math.Max(1, _options.CommandTimeoutSeconds),
                AllowUserVariables = true,
                AllowLoadLocalInfile = false,
                TreatTinyAsBoolean = false,
                ConvertZeroDateTime = true,
                AllowZeroDateTime = true
            };

            if (Enum.TryParse(profile.SslMode, true, out MySqlSslMode sslMode))
            {
                builder.SslMode = sslMode;
            }

            return new MySqlConnection(builder.ConnectionString);
        }
    }
}
