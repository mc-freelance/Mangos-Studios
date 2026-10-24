// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Authorize and run SQL statements with guarded execution behavior.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Sql;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class SqlExecutionService : ISqlExecutionService
    {
        private static readonly Regex DestructivePattern = new(@"\b(DROP|TRUNCATE|DELETE\s+FROM|ALTER\s+TABLE|CREATE\s+USER|DROP\s+USER|GRANT|REVOKE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the sql execution service dependencies and state.
        public SqlExecutionService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Detect high-risk SQL statements so callers can require explicit confirmation.
        public bool IsDestructive(string sql)
        {
            return !string.IsNullOrWhiteSpace(sql) && DestructivePattern.IsMatch(RemoveComments(sql));
        }

        // Execute SQL on the selected connection and limit the number of rows returned to the UI.
        public async Task<SqlExecutionResult> ExecuteAsync(ConnectionProfile profile, string database, string sql, int maximumRows, IProgress<OperationProgressUpdate>? progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException("SQL text is required.", nameof(sql));
            }

            maximumRows = Math.Clamp(maximumRows, 1, _options.MaximumSqlRows);
            Stopwatch stopwatch = Stopwatch.StartNew();
            progress?.Report(new OperationProgressUpdate(
                "Connecting to database",
                "Opening the SQL Console connection to " + database + "."));
            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection)
            {
                CommandTimeout = _options.CommandTimeoutSeconds
            };

            List<string> columns = new();
            List<IReadOnlyDictionary<string, object?>> rows = new();
            long affectedRows = 0;

            progress?.Report(new OperationProgressUpdate(
                "Executing SQL statement",
                "The statement has been sent to MySQL/MariaDB; waiting for the first result."));
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                if (reader.FieldCount > 0 && columns.Count == 0)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        columns.Add(reader.GetName(i));
                    }
                }

                while (reader.FieldCount > 0 && rows.Count < maximumRows && await reader.ReadAsync(cancellationToken))
                {
                    Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = value is byte[] bytes ? Convert.ToBase64String(bytes) : value;
                    }

                    rows.Add(row);
                    if (rows.Count == 1 || rows.Count % 100 == 0)
                    {
                        progress?.Report(new OperationProgressUpdate(
                            "Reading query results",
                            rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " result row(s) loaded.",
                            rows.Count,
                            maximumRows,
                            "rows"));
                    }
                }

                affectedRows += reader.RecordsAffected > 0 ? reader.RecordsAffected : 0;
            }
            while (await reader.NextResultAsync(cancellationToken));

            stopwatch.Stop();
            progress?.Report(new OperationProgressUpdate(
                "Preparing SQL result",
                rows.Count > 0
                    ? rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " row(s) are ready to display."
                    : affectedRows.ToString("N0", CultureInfo.InvariantCulture) + " row(s) affected; preparing the result summary."));
            return new SqlExecutionResult
            {
                Succeeded = true,
                Message = rows.Count >= maximumRows ? "Query succeeded. The result was limited to " + maximumRows + " rows." : "Query succeeded.",
                AffectedRows = affectedRows,
                DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Columns = columns,
                Rows = rows
            };
        }

        // Remove comments using schema-aware database operations.
        private static string RemoveComments(string sql)
        {
            string withoutBlock = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(withoutBlock, "--.*?$", string.Empty, RegexOptions.Multiline);
        }
    }
}
