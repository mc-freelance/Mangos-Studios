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

        // Construct a database connection with an explicitly validated TLS policy.
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

            // Never fall back to the connector's permissive TLS default.
            ConnectionTlsSecurity.ValidateProfile(profile);
            builder.SslMode = Enum.Parse<MySqlSslMode>(ConnectionTlsSecurity.NormalizeMode(profile.SslMode));
            builder.SslCa = ConnectionTlsSecurity.IsTlsDisabled(profile.SslMode)
                ? string.Empty
                : profile.SslCa?.Trim() ?? string.Empty;

            return new MySqlConnection(builder.ConnectionString);
        }
    }
}
