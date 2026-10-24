// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
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
    internal sealed class GenericTableService : IGenericTableService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the generic table service dependencies and state.
        public GenericTableService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Handle browse async within the generic table service workflow.
        public async Task<PagedResult<TableRow>> BrowseAsync(ConnectionProfile profile, TableMetadata table, int page, int pageSize, int displayColumnCount, string? search, string? sort, bool descending, CancellationToken cancellationToken)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            displayColumnCount = Math.Clamp(displayColumnCount, 1, 9);
            int offset = (page - 1) * pageSize;

            SearchDefinition searchDefinition = BuildSearch(table, search);

            ColumnMetadata? sortColumn = null;
            if (!string.IsNullOrEmpty(sort))
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    if (string.Equals(table.Columns[i].Name, sort, StringComparison.OrdinalIgnoreCase))
                    {
                        sortColumn = table.Columns[i];
                        break;
                    }
                }
            }

            sortColumn ??= table.PrimaryKeyColumns.Count > 0 ? table.PrimaryKeyColumns[0] : null;
            sortColumn ??= table.Columns.Count > 0 ? table.Columns[0] : null;

            List<ColumnMetadata> selectedColumns = SelectBrowseColumns(table, displayColumnCount).ToList();

            if (selectedColumns.Count == 0)
            {
                throw new InvalidOperationException("The selected table does not expose any columns.");
            }

            string qualifiedTable = MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
            string countSql = "SELECT COUNT(*) FROM " + qualifiedTable + searchDefinition.WhereClause + ";";
            string orderSql = sortColumn == null ? string.Empty : " ORDER BY " + MySqlIdentifier.Quote(sortColumn.Name) + (descending ? " DESC" : " ASC");
            string selection = string.Join(", ", selectedColumns.Select(column => MySqlIdentifier.Quote(column.Name)));
            string dataSql = "SELECT " + selection + " FROM " + qualifiedTable + searchDefinition.WhereClause + orderSql + " LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand countCommand = new(countSql, connection))
            {
                AddSearchParameters(countCommand, searchDefinition);
                object? value = await countCommand.ExecuteScalarAsync(cancellationToken);
                total = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            List<TableRow> rows = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                AddSearchParameters(command, searchDefinition);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        values[reader.GetName(i)] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
                    }

                    rows.Add(new TableRow
                    {
                        Values = values,
                        KeyToken = table.CanEdit ? RowKeyCodec.Encode(table, values) : string.Empty
                    });
                }
            }

            return new PagedResult<TableRow>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }


        // Select browse columns using schema-aware database operations.
        internal static IReadOnlyList<ColumnMetadata> SelectBrowseColumns(TableMetadata table, int displayColumnCount)
        {
            int safeCount = Math.Clamp(displayColumnCount, 1, 9);
            List<ColumnMetadata> selected = table.Columns
                .OrderBy(column => column.OrdinalPosition)
                .Take(safeCount)
                .ToList();

            foreach (ColumnMetadata key in table.PrimaryKeyColumns)
            {
                if (!selected.Any(column => string.Equals(column.Name, key.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    selected.Add(key);
                }
            }

            return selected;
        }

        // Read row using schema-aware database operations.
        public async Task<TableRow?> GetRowAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, CancellationToken cancellationToken)
        {
            EnsureEditable(table);
            string qualifiedTable = MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
            string where = BuildKeyWhere(table, keys);
            string sql = "SELECT * FROM " + qualifiedTable + " WHERE " + where + " LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddKeyParameters(command, table, keys);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                values[reader.GetName(i)] = reader.IsDBNull(i) ? null : NormalizeValue(reader.GetValue(i));
            }

            return new TableRow
            {
                Values = values,
                KeyToken = RowKeyCodec.Encode(table, values)
            };
        }

        // Insert  using schema-aware database operations.
        public async Task<long?> InsertAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> values, ISet<string> nullColumns, CancellationToken cancellationToken)
        {
            List<ColumnMetadata> columns = table.Columns
                .Where(column => !column.IsAutoIncrement && !column.IsGenerated && values.ContainsKey(column.Name))
                .ToList();
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("No writable columns were submitted.");
            }

            string names = string.Join(", ", columns.Select(column => MySqlIdentifier.Quote(column.Name)));
            string parameters = string.Join(", ", columns.Select((_, index) => "@v" + index));
            string sql = "INSERT INTO " + MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name) +
                         " (" + names + ") VALUES (" + parameters + ");";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValueParameters(command, columns, values, nullColumns);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The row was not created.");
            }

            bool hasAutoIncrementKey = table.PrimaryKeyColumns.Any(column => column.IsAutoIncrement);
            return hasAutoIncrementKey && command.LastInsertedId > 0 ? command.LastInsertedId : null;
        }

        // Update  using schema-aware database operations.
        public async Task UpdateAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, IReadOnlyDictionary<string, string?> values, ISet<string> nullColumns, CancellationToken cancellationToken)
        {
            EnsureEditable(table);
            List<ColumnMetadata> columns = table.Columns
                .Where(column =>
                    !column.IsGenerated &&
                    !ColumnEditPolicy.IsLockedIdentifier(table, column) &&
                    values.ContainsKey(column.Name))
                .ToList();
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("No writable columns were submitted.");
            }

            string set = string.Join(", ", columns.Select((column, index) => MySqlIdentifier.Quote(column.Name) + " = @v" + index));
            string where = BuildKeyWhere(table, keys);
            string sql = "UPDATE " + MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name) +
                         " SET " + set + " WHERE " + where + " LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValueParameters(command, columns, values, nullColumns);
            AddKeyParameters(command, table, keys);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The row was not updated. It may have been changed or removed by another user.");
            }
        }

        // Delete  using schema-aware database operations.
        public async Task DeleteAsync(ConnectionProfile profile, TableMetadata table, IReadOnlyDictionary<string, string?> keys, CancellationToken cancellationToken)
        {
            EnsureEditable(table);
            string where = BuildKeyWhere(table, keys);
            string sql = "DELETE FROM " + MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name) +
                         " WHERE " + where + " LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddKeyParameters(command, table, keys);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The row was not deleted. It may have been changed or removed by another user.");
            }
        }

        // Build search using schema-aware database operations.
        internal static SearchDefinition BuildSearch(TableMetadata table, string? search)
        {
            string term = search?.Trim() ?? string.Empty;
            if (term.Length == 0)
            {
                return SearchDefinition.Empty;
            }

            bool numeric = ulong.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            List<ColumnMetadata> identifiers = table.Columns
                .Where(IsIdentifierColumn)
                .Take(12)
                .ToList();
            List<ColumnMetadata> names = table.Columns
                .Where(column => column.IsTextual && IsNameColumn(column.Name))
                .Take(12)
                .ToList();

            if (names.Count == 0)
            {
                names = table.Columns.Where(column => column.IsTextual).Take(8).ToList();
            }

            List<string> conditions = new();
            foreach (ColumnMetadata identifier in identifiers)
            {
                if (identifier.IsNumeric && numeric)
                {
                    conditions.Add(MySqlIdentifier.Quote(identifier.Name) + " = @searchExact");
                }
                else if (identifier.IsTextual)
                {
                    conditions.Add(MySqlIdentifier.Quote(identifier.Name) + " = @searchExact");
                }
            }

            foreach (ColumnMetadata name in names)
            {
                conditions.Add(MySqlIdentifier.Quote(name.Name) + " LIKE @searchLike");
            }

            if (conditions.Count == 0)
            {
                return SearchDefinition.Empty;
            }

            return new SearchDefinition(
                " WHERE (" + string.Join(" OR ", conditions.Distinct(StringComparer.OrdinalIgnoreCase)) + ")",
                term,
                conditions.Any(condition => condition.Contains("@searchExact", StringComparison.Ordinal)),
                conditions.Any(condition => condition.Contains("@searchLike", StringComparison.Ordinal)));
        }

        // Determine whether identifier column using schema-aware database operations.
        private static bool IsIdentifierColumn(ColumnMetadata column)
        {
            if (column.IsPrimaryKey)
            {
                return true;
            }

            string normalized = column.Name.Replace("_", string.Empty).ToLowerInvariant();
            return normalized == "id" || normalized == "entry" || normalized == "guid" ||
                   normalized.EndsWith("entry", StringComparison.Ordinal) ||
                   normalized.EndsWith("guid", StringComparison.Ordinal) ||
                   (normalized.EndsWith("id", StringComparison.Ordinal) && normalized.Length <= 32);
        }

        // Determine whether name column using schema-aware database operations.
        private static bool IsNameColumn(string name)
        {
            string normalized = name.Replace("_", string.Empty).ToLowerInvariant();
            return normalized.Contains("name", StringComparison.Ordinal) ||
                   normalized.Contains("title", StringComparison.Ordinal) ||
                   normalized == "description" || normalized == "comment";
        }

        // Add search parameters using schema-aware database operations.
        private static void AddSearchParameters(MySqlCommand command, SearchDefinition search)
        {
            if (search.UseExact)
            {
                command.Parameters.AddWithValue("@searchExact", search.Term);
            }

            if (search.UseLike)
            {
                command.Parameters.AddWithValue("@searchLike", "%" + search.Term + "%");
            }
        }

        // Build key where using schema-aware database operations.
        private static string BuildKeyWhere(TableMetadata table, IReadOnlyDictionary<string, string?> keys)
        {
            EnsureEditable(table);
            List<string> conditions = new();
            int index = 0;
            foreach (ColumnMetadata keyColumn in table.PrimaryKeyColumns)
            {
                if (!keys.ContainsKey(keyColumn.Name))
                {
                    throw new InvalidOperationException("Primary key column '" + keyColumn.Name + "' is missing.");
                }

                conditions.Add(MySqlIdentifier.Quote(keyColumn.Name) + " = @k" + index);
                index++;
            }

            return string.Join(" AND ", conditions);
        }

        // Add key parameters using schema-aware database operations.
        private static void AddKeyParameters(MySqlCommand command, TableMetadata table, IReadOnlyDictionary<string, string?> keys)
        {
            int index = 0;
            foreach (ColumnMetadata keyColumn in table.PrimaryKeyColumns)
            {
                object? value = DatabaseValueConverter.Convert(keyColumn, keys[keyColumn.Name], false);
                command.Parameters.AddWithValue("@k" + index, value ?? DBNull.Value);
                index++;
            }
        }

        // Add value parameters using schema-aware database operations.
        private static void AddValueParameters(MySqlCommand command, List<ColumnMetadata> columns, IReadOnlyDictionary<string, string?> values, ISet<string> nullColumns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                ColumnMetadata column = columns[i];
                values.TryGetValue(column.Name, out string? rawValue);
                object? value = DatabaseValueConverter.Convert(column, rawValue, nullColumns.Contains(column.Name));
                command.Parameters.AddWithValue("@v" + i, value ?? DBNull.Value);
            }
        }

        // Normalize value using schema-aware database operations.
        private static object NormalizeValue(object value)
        {
            if (value is byte[] bytes)
            {
                return Convert.ToBase64String(bytes);
            }

            if (value is MySqlDateTime mySqlDateTime)
            {
                return FormatMySqlDateTime(mySqlDateTime);
            }

            if (value is DateTime dateTime)
            {
                return FormatDateTime(dateTime);
            }

            if (value is DateOnly dateOnly)
            {
                return dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (value is TimeOnly timeOnly)
            {
                return TrimFraction(timeOnly.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
            }

            if (value is TimeSpan timeSpan)
            {
                return timeSpan.ToString("c", CultureInfo.InvariantCulture);
            }

            return value;
        }

        // Format my sql date time using schema-aware database operations.
        private static string FormatMySqlDateTime(MySqlDateTime value)
        {
            string result = value.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                            value.Month.ToString("D2", CultureInfo.InvariantCulture) + "-" +
                            value.Day.ToString("D2", CultureInfo.InvariantCulture) + " " +
                            value.Hour.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                            value.Minute.ToString("D2", CultureInfo.InvariantCulture) + ":" +
                            value.Second.ToString("D2", CultureInfo.InvariantCulture);

            if (value.Microsecond > 0)
            {
                result += "." + value.Microsecond.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
            }

            return result;
        }

        // Format date time using schema-aware database operations.
        private static string FormatDateTime(DateTime value)
        {
            string result = value.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
            return TrimFraction(result);
        }

        // Handle trim fraction within the generic table service workflow.
        private static string TrimFraction(string value)
        {
            return value.Contains('.', StringComparison.Ordinal)
                ? value.TrimEnd('0').TrimEnd('.')
                : value;
        }

        // Ensure editable using schema-aware database operations.
        private static void EnsureEditable(TableMetadata table)
        {
            if (!table.CanEdit)
            {
                throw new InvalidOperationException("This table does not have a primary key and is read-only in the generic editor.");
            }
        }

        internal sealed class SearchDefinition
        {
            public static readonly SearchDefinition Empty = new(string.Empty, string.Empty, false, false);

            // Search for definition using schema-aware database operations.
            public SearchDefinition(string whereClause, string term, bool useExact, bool useLike)
            {
                WhereClause = whereClause;
                Term = term;
                UseExact = useExact;
                UseLike = useLike;
            }

            public string WhereClause
            {
                get;
            }

            public string Term
            {
                get;
            }

            public bool UseExact
            {
                get;
            }

            public bool UseLike
            {
                get;
            }
        }
    }
}
