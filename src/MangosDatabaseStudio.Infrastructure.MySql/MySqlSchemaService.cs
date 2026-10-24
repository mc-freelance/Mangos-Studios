// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class MySqlSchemaService : IMySqlSchemaService
    {
        private readonly MySqlConnectionFactory _connectionFactory;

        // Initialize the my sql schema service dependencies and state.
        public MySqlSchemaService(MySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        // Read databases using schema-aware database operations.
        public async Task<IReadOnlyList<DatabaseMetadata>> GetDatabasesAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            const string sql = @"SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
                                FROM INFORMATION_SCHEMA.SCHEMATA
                                ORDER BY SCHEMA_NAME;";

            List<DatabaseMetadata> databases = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(new DatabaseMetadata
                {
                    Name = reader.GetString(0),
                    CharacterSet = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Collation = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }

            return databases;
        }

        // Read tables using schema-aware database operations.
        public async Task<IReadOnlyList<TableMetadata>> GetTablesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken)
        {
            const string sql = @"SELECT TABLE_NAME, COALESCE(ENGINE, ''), COALESCE(TABLE_COLLATION, ''),
                                COALESCE(TABLE_COMMENT, ''), COALESCE(TABLE_ROWS, 0), COALESCE(DATA_LENGTH, 0), COALESCE(INDEX_LENGTH, 0)
                                FROM INFORMATION_SCHEMA.TABLES
                                WHERE TABLE_SCHEMA = @database AND TABLE_TYPE = 'BASE TABLE'
                                ORDER BY TABLE_NAME;";

            List<TableMetadata> tables = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@database", database);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new TableMetadata
                {
                    DatabaseName = database,
                    Name = reader.GetString(0),
                    Engine = reader.GetString(1),
                    Collation = reader.GetString(2),
                    Comment = reader.GetString(3),
                    ApproximateRows = Convert.ToInt64(reader.GetValue(4)),
                    DataLength = Convert.ToInt64(reader.GetValue(5)),
                    IndexLength = Convert.ToInt64(reader.GetValue(6))
                });
            }

            return tables;
        }

        // Read table using schema-aware database operations.
        public async Task<TableMetadata> GetTableAsync(ConnectionProfile profile, string database, string table, CancellationToken cancellationToken)
        {
            const string tableSql = @"SELECT COALESCE(ENGINE, ''), COALESCE(TABLE_COLLATION, ''), COALESCE(TABLE_COMMENT, ''),
                                    COALESCE(TABLE_ROWS, 0), COALESCE(DATA_LENGTH, 0), COALESCE(INDEX_LENGTH, 0)
                                    FROM INFORMATION_SCHEMA.TABLES
                                    WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table AND TABLE_TYPE = 'BASE TABLE';";

            const string columnsSql = @"SELECT c.COLUMN_NAME, c.ORDINAL_POSITION, c.COLUMN_DEFAULT, c.IS_NULLABLE,
                                    c.DATA_TYPE, c.COLUMN_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
                                    c.EXTRA, c.COLUMN_COMMENT, c.CHARACTER_SET_NAME, c.COLLATION_NAME, COALESCE(s.SEQ_IN_INDEX, 0)
                                    FROM INFORMATION_SCHEMA.COLUMNS c
                                    LEFT JOIN INFORMATION_SCHEMA.STATISTICS s
                                    ON s.TABLE_SCHEMA = c.TABLE_SCHEMA
                                    AND s.TABLE_NAME = c.TABLE_NAME
                                    AND s.COLUMN_NAME = c.COLUMN_NAME
                                    AND s.INDEX_NAME = 'PRIMARY'
                                    WHERE c.TABLE_SCHEMA = @database AND c.TABLE_NAME = @table
                                    ORDER BY c.ORDINAL_POSITION;";

            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);

            TableMetadata metadata = new()
            {
                DatabaseName = database,
                Name = table
            };

            await using (MySqlCommand command = new(tableSql, connection))
            {
                command.Parameters.AddWithValue("@database", database);
                command.Parameters.AddWithValue("@table", table);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Table '" + database + "." + table + "' was not found.");
                }

                metadata.Engine = reader.GetString(0);
                metadata.Collation = reader.GetString(1);
                metadata.Comment = reader.GetString(2);
                metadata.ApproximateRows = Convert.ToInt64(reader.GetValue(3));
                metadata.DataLength = Convert.ToInt64(reader.GetValue(4));
                metadata.IndexLength = Convert.ToInt64(reader.GetValue(5));
            }

            List<ColumnMetadata> columns = new();
            await using (MySqlCommand command = new(columnsSql, connection))
            {
                command.Parameters.AddWithValue("@database", database);
                command.Parameters.AddWithValue("@table", table);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string extra = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
                    int primaryOrder = Convert.ToInt32(reader.GetValue(13));
                    columns.Add(new ColumnMetadata
                    {
                        Name = reader.GetString(0),
                        OrdinalPosition = reader.GetInt32(1),
                        DefaultValue = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2)),
                        IsNullable = string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase),
                        DataType = reader.GetString(4).ToLowerInvariant(),
                        ColumnType = reader.GetString(5),
                        CharacterMaximumLength = reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6)),
                        NumericPrecision = reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
                        NumericScale = reader.IsDBNull(8) ? null : Convert.ToInt32(reader.GetValue(8)),
                        IsAutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                        IsGenerated = extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase),
                        Extra = extra,
                        Comment = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                        CharacterSetName = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                        CollationName = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        IsPrimaryKey = primaryOrder > 0,
                        PrimaryKeyOrder = primaryOrder
                    });
                }
            }

            metadata.Columns = columns;

            const string indexesSql = @"SELECT INDEX_NAME, NON_UNIQUE, INDEX_TYPE, SEQ_IN_INDEX, COLUMN_NAME FROM INFORMATION_SCHEMA.STATISTICS
                                        WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table ORDER BY INDEX_NAME, SEQ_IN_INDEX;";

            Dictionary<string, (bool IsUnique, string IndexType, List<string> Columns)> indexLookup = new(StringComparer.OrdinalIgnoreCase);
            await using (MySqlCommand command = new(indexesSql, connection))
            {
                command.Parameters.AddWithValue("@database", database);
                command.Parameters.AddWithValue("@table", table);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string indexName = reader.GetString(0);
                    bool isUnique = Convert.ToInt32(reader.GetValue(1)) == 0;
                    string indexType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    string columnName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                    if (!indexLookup.TryGetValue(indexName, out var item))
                    {
                        item = (isUnique, indexType, new List<string>());
                        indexLookup[indexName] = item;
                    }

                    if (!string.IsNullOrWhiteSpace(columnName))
                    {
                        item.Columns.Add(columnName);
                    }
                }
            }

            metadata.Indexes = indexLookup
                .OrderBy(item => string.Equals(item.Key, "PRIMARY", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new TableIndexMetadata
                {
                    Name = item.Key,
                    IsUnique = item.Value.IsUnique,
                    IndexType = item.Value.IndexType,
                    Columns = item.Value.Columns
                })
                .ToList();

            return metadata;
        }

        // Read create table sql using schema-aware database operations.
        public async Task<string> GetCreateTableSqlAsync(
            ConnectionProfile profile,
            string database,
            string table,
            CancellationToken cancellationToken)
        {
            string sql = "SHOW CREATE TABLE " + MySqlIdentifier.Quote(database) + "." + MySqlIdentifier.Quote(table) + ";";
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken) || reader.FieldCount < 2 || reader.IsDBNull(1))
            {
                throw new InvalidOperationException("The server did not return the table definition for '" + database + "." + table + "'.");
            }

            return reader.GetString(1);
        }

        // Read engines using schema-aware database operations.
        public async Task<IReadOnlyList<string>> GetEnginesAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            const string sql = "SELECT ENGINE FROM INFORMATION_SCHEMA.ENGINES WHERE SUPPORT IN ('YES', 'DEFAULT') ORDER BY ENGINE;";

            List<string> engines = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    engines.Add(reader.GetString(0));
                }
            }

            return engines;
        }

        // Read collations using schema-aware database operations.
        public async Task<IReadOnlyList<string>> GetCollationsAsync(ConnectionProfile profile, CancellationToken cancellationToken)
        {
            const string sql = "SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLLATIONS ORDER BY COLLATION_NAME;";

            List<string> collations = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    collations.Add(reader.GetString(0));
                }
            }

            return collations;
        }

    }
}
