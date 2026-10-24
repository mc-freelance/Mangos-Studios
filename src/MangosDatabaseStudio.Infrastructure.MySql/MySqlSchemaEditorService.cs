// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class MySqlSchemaEditorService : IMySqlSchemaEditorService
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "bit", "tinyint", "smallint", "mediumint", "int", "integer", "bigint",
            "decimal", "numeric", "float", "double", "real",
            "char", "varchar", "binary", "varbinary",
            "tinytext", "text", "mediumtext", "longtext",
            "tinyblob", "blob", "mediumblob", "longblob",
            "date", "datetime", "timestamp", "time", "year",
            "enum", "set", "json"
        };

        private static readonly HashSet<string> IntegerTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "tinyint", "smallint", "mediumint", "int", "integer", "bigint"
        };

        private static readonly HashSet<string> TemporalTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "datetime", "timestamp"
        };

        private static readonly HashSet<string> CharacterTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "char", "varchar", "tinytext", "text", "mediumtext", "longtext", "enum", "set"
        };

        private static readonly Regex TypePattern = new(
            @"^(?<base>[a-z]+)(?<args>\([^\r\n;`#]*\))?(?<mods>(?:\s+(?:unsigned|zerofill|binary))*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex NumericArguments = new(@"^\(\s*\d+\s*(?:,\s*\d+\s*)?\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex LengthArguments = new(@"^\(\s*\d+\s*\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex EnumSetArguments = new(@"^\(\s*'(?:[^'\\]|\\.|'')*'(?:\s*,\s*'(?:[^'\\]|\\.|'')*')*\s*\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SafeServerToken = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly IMySqlSchemaService _schema;

        // Initialize the my sql schema editor service dependencies and state.
        public MySqlSchemaEditorService(MySqlConnectionFactory connectionFactory, IMySqlSchemaService schema)
        {
            _connectionFactory = connectionFactory;
            _schema = schema;
        }

        // Build add column plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildAddColumnPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            SchemaColumnDefinition definition,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            ValidateNewColumn(metadata, definition);
            string clause = BuildColumnDefinition(metadata, definition, isNew: true, currentColumn: null);
            bool requiresConfirmation = !definition.IsNullable &&
                                        string.Equals(NormalizeDefaultMode(definition.DefaultMode), "none", StringComparison.Ordinal);
            return new SchemaChangePlan
            {
                Operation = "ADD COLUMN",
                ObjectName = table + "." + definition.Name,
                Summary = "Added column " + definition.Name + " to " + table + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " ADD COLUMN " + clause + ";",
                IsDestructive = requiresConfirmation,
                ConfirmationValue = requiresConfirmation ? table : string.Empty,
                Warning = requiresConfirmation
                    ? "The new column is NOT NULL without an explicit default. If rows already exist, the server may rebuild the table, reject the change, or populate an implicit value depending on SQL mode."
                    : string.Empty
            };
        }

        // Build edit column plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildEditColumnPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            string originalColumn,
            SchemaColumnDefinition definition,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            ColumnMetadata current = FindColumn(metadata, originalColumn);
            if (current.IsGenerated)
            {
                throw new InvalidOperationException("Generated columns cannot be modified by the schema editor because their generation expression must be preserved exactly.");
            }

            string unsupportedExtra = (current.Extra ?? string.Empty)
                .Replace("auto_increment", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (!string.IsNullOrWhiteSpace(unsupportedExtra))
            {
                throw new InvalidOperationException("This column uses additional MySQL/MariaDB attributes ('" + unsupportedExtra + "') that the guarded editor cannot safely preserve yet.");
            }

            ValidateEditedColumn(metadata, current, definition);
            string clause = BuildColumnDefinition(metadata, definition, isNew: false, currentColumn: current);
            bool typeChanged = !EquivalentType(current.ColumnType, definition.ColumnType);
            bool nullableTightened = current.IsNullable && !definition.IsNullable;
            bool autoIncrementChanged = current.IsAutoIncrement != definition.IsAutoIncrement;
            bool renamed = !string.Equals(current.Name, definition.Name, StringComparison.Ordinal);
            bool positionChanged = IsColumnPositionChanged(metadata, current, definition);
            bool destructive = typeChanged || nullableTightened || autoIncrementChanged || renamed || positionChanged;

            string warning = typeChanged || nullableTightened || autoIncrementChanged
                ? "Changing a column type, nullability, or auto-increment behavior can reinterpret or reject existing data. Verify the generated SQL and keep a database backup before continuing."
                : renamed
                    ? "Renaming a column can break queries, scripts, and emulator code that reference the old name. Verify all consumers before continuing."
                    : positionChanged
                        ? "Changing column order can affect integrations that depend on SELECT * or positional schema assumptions. Verify dependent tools and scripts before continuing."
                        : string.Empty;

            return new SchemaChangePlan
            {
                Operation = "MODIFY COLUMN",
                ObjectName = table + "." + current.Name,
                Summary = string.Equals(current.Name, definition.Name, StringComparison.Ordinal)
                    ? "Modified column " + current.Name + " in " + table + "."
                    : "Renamed column " + current.Name + " to " + definition.Name + " in " + table + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " CHANGE COLUMN " + MySqlIdentifier.Quote(current.Name) + " " + clause + ";",
                IsDestructive = destructive,
                ConfirmationValue = destructive ? table : string.Empty,
                Warning = warning
            };
        }

        // Build drop column plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildDropColumnPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            string column,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            ColumnMetadata current = FindColumn(metadata, column);
            if (metadata.Columns.Count <= 1)
            {
                throw new InvalidOperationException("The final column in a table cannot be removed from Database Studio.");
            }

            return new SchemaChangePlan
            {
                Operation = "DROP COLUMN",
                ObjectName = table + "." + current.Name,
                Summary = "Dropped column " + current.Name + " from " + table + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " DROP COLUMN " + MySqlIdentifier.Quote(current.Name) + ";",
                IsDestructive = true,
                ConfirmationValue = table,
                Warning = "Dropping this column permanently removes all data stored in it. Change Storage will record the operation, but Database Studio cannot automatically restore the deleted column data."
            };
        }

        // Build add index plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildAddIndexPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            SchemaIndexDefinition definition,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            string kind = NormalizeIndexKind(definition.Kind);
            string[] columns = (definition.Columns ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (columns.Length == 0)
            {
                throw new InvalidOperationException("Choose at least one column for the index.");
            }

            ColumnMetadata[] selectedColumns = columns.Select(column => FindColumn(metadata, column)).ToArray();

            if (string.Equals(kind, "FULLTEXT", StringComparison.Ordinal) && selectedColumns.Any(column => !(string.Equals(column.DataType, "char", StringComparison.OrdinalIgnoreCase) || string.Equals(column.DataType, "varchar", StringComparison.OrdinalIgnoreCase) || column.DataType.Contains("text", StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidOperationException("FULLTEXT indexes can only be created on character/text columns in the guarded editor.");
            }

            if (string.Equals(kind, "PRIMARY", StringComparison.Ordinal) && selectedColumns.Any(column => column.IsNullable))
            {
                throw new InvalidOperationException("Primary-key columns must be NOT NULL before the key is created.");
            }

            if (!string.Equals(kind, "FULLTEXT", StringComparison.Ordinal) && selectedColumns.Any(column => column.DataType.Contains("text", StringComparison.OrdinalIgnoreCase) || column.DataType.Contains("blob", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Prefix indexes for TEXT/BLOB columns are not implemented yet. Use FULLTEXT where appropriate or create the specialized index outside the guarded editor.");
            }

            if (string.Equals(kind, "PRIMARY", StringComparison.Ordinal))
            {
                if (metadata.Indexes.Any(index => index.IsPrimary))
                {
                    throw new InvalidOperationException("This table already has a primary key.");
                }
            }
            else
            {
                ValidateEditableIdentifier(definition.Name, "index name");
                if (metadata.Indexes.Any(index => string.Equals(index.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("An index named '" + definition.Name + "' already exists.");
                }
            }

            string columnSql = string.Join(", ", columns.Select(MySqlIdentifier.Quote));
            string indexSql = kind switch
            {
                "PRIMARY" => "PRIMARY KEY (" + columnSql + ")",
                "UNIQUE" => "UNIQUE INDEX " + MySqlIdentifier.Quote(definition.Name) + " (" + columnSql + ")",
                "FULLTEXT" => "FULLTEXT INDEX " + MySqlIdentifier.Quote(definition.Name) + " (" + columnSql + ")",
                _ => "INDEX " + MySqlIdentifier.Quote(definition.Name) + " (" + columnSql + ")"
            };

            return new SchemaChangePlan
            {
                Operation = "ADD INDEX",
                ObjectName = table + "." + (kind == "PRIMARY" ? "PRIMARY" : definition.Name),
                Summary = "Added " + kind.ToLowerInvariant() + " index to " + table + " on " + string.Join(", ", columns) + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " ADD " + indexSql + ";"
            };
        }

        // Build drop index plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildDropIndexPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            string indexName,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            TableIndexMetadata? index = metadata.Indexes.FirstOrDefault(item => string.Equals(item.Name, indexName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Index '" + indexName + "' was not found.");
            string action = index.IsPrimary ? "DROP PRIMARY KEY" : "DROP INDEX " + MySqlIdentifier.Quote(index.Name);
            return new SchemaChangePlan
            {
                Operation = "DROP INDEX",
                ObjectName = table + "." + index.Name,
                Summary = "Dropped index " + index.Name + " from " + table + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " " + action + ";",
                IsDestructive = true,
                ConfirmationValue = table,
                Warning = index.IsPrimary
                    ? "Removing a primary key can break row editing, relationships, and application assumptions. Database Studio cannot automatically reconstruct the key after deletion."
                    : "Removing an index can affect uniqueness guarantees or query performance. Review dependent application and database behavior before continuing."
            };
        }

        // Build table properties plan using schema-aware database operations.
        public async Task<SchemaChangePlan> BuildTablePropertiesPlanAsync(
            ConnectionProfile profile,
            string database,
            string table,
            SchemaTablePropertiesDefinition definition,
            CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            string engine = (definition.Engine ?? string.Empty).Trim();
            string collation = (definition.Collation ?? string.Empty).Trim();
            string comment = (definition.Comment ?? string.Empty).Trim();
            if (comment.Length > 2048)
            {
                throw new InvalidOperationException("The table comment cannot exceed 2048 characters.");
            }

            if (!SafeServerToken.IsMatch(engine))
            {
                throw new InvalidOperationException("Choose a valid database engine.");
            }

            if (!SafeServerToken.IsMatch(collation))
            {
                throw new InvalidOperationException("Choose a valid collation.");
            }

            IReadOnlyList<string> engines = await _schema.GetEnginesAsync(profile, cancellationToken);
            IReadOnlyList<string> collations = await _schema.GetCollationsAsync(profile, cancellationToken);
            if (!engines.Contains(engine, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected storage engine is not supported by this server.");
            }

            if (!collations.Contains(collation, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected collation is not supported by this server.");
            }

            List<string> clauses = new();
            bool engineChanged = !string.Equals(metadata.Engine, engine, StringComparison.OrdinalIgnoreCase);
            bool collationChanged = !string.Equals(metadata.Collation, collation, StringComparison.OrdinalIgnoreCase);
            bool commentChanged = !string.Equals(metadata.Comment ?? string.Empty, comment, StringComparison.Ordinal);
            if (engineChanged)
            {
                clauses.Add("ENGINE = " + engine);
            }

            if (collationChanged)
            {
                string charset = await GetCharacterSetForCollationAsync(profile, collation, cancellationToken);
                clauses.Add("DEFAULT CHARACTER SET = " + charset + " COLLATE = " + collation);
            }

            if (commentChanged)
            {
                clauses.Add("COMMENT = " + QuoteLiteral(comment));
            }

            if (clauses.Count == 0)
            {
                throw new InvalidOperationException("No table property changes were detected.");
            }

            bool destructive = engineChanged || collationChanged;
            return new SchemaChangePlan
            {
                Operation = "ALTER TABLE",
                ObjectName = table,
                Summary = "Updated table properties for " + table + ".",
                Sql = "ALTER TABLE " + Qualified(database, table) + " " + string.Join(", ", clauses) + ";",
                IsDestructive = destructive,
                ConfirmationValue = destructive ? table : string.Empty,
                Warning = destructive
                    ? "Changing a storage engine can rebuild or lock the table. Changing the default collation affects the table default used by future or redefined character columns; existing column collations are not converted by this guarded operation. Back up the database and verify emulator compatibility first."
                    : string.Empty
            };
        }

        // Execute  using schema-aware database operations.
        public async Task ExecuteAsync(ConnectionProfile profile, SchemaChangePlan plan, CancellationToken cancellationToken)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.Sql) || !plan.Sql.TrimStart().StartsWith("ALTER TABLE ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only validated ALTER TABLE plans can be executed by the schema editor.");
            }

            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(plan.Sql, connection)
            {
                CommandTimeout = 300
            };

            await command.ExecuteNonQueryAsync(cancellationToken);
        }


        // Validate editable identifier using schema-aware database operations.
        private static void ValidateEditableIdentifier(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Enter a " + label + ".");
            }

            if (value.Trim().Length > 64)
            {
                throw new InvalidOperationException("The " + label + " cannot exceed 64 characters.");
            }

            try
            {
                MySqlIdentifier.Quote(value.Trim());
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("The " + label + " contains unsupported characters. Use letters, numbers, underscore, or dollar sign.");
            }
        }

        // Locate column using schema-aware database operations.
        private static ColumnMetadata FindColumn(TableMetadata metadata, string name)
        {
            ColumnMetadata? column = metadata.Columns.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return column ?? throw new InvalidOperationException("Column '" + name + "' was not found in table '" + metadata.Name + "'.");
        }

        // Validate column payload lengths using schema-aware database operations.
        private static void ValidateColumnPayloadLengths(SchemaColumnDefinition definition)
        {
            if ((definition.Comment ?? string.Empty).Length > 1024)
            {
                throw new InvalidOperationException("The column comment cannot exceed 1024 characters.");
            }

            if ((definition.DefaultValue ?? string.Empty).Length > 2048)
            {
                throw new InvalidOperationException("The default value cannot exceed 2048 characters.");
            }
        }

        // Validate new column using schema-aware database operations.
        private static void ValidateNewColumn(TableMetadata metadata, SchemaColumnDefinition definition)
        {
            ValidateEditableIdentifier(definition.Name, "column name");
            ValidateColumnPayloadLengths(definition);
            if (metadata.Columns.Any(column => string.Equals(column.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Column '" + definition.Name + "' already exists.");
            }

            if (definition.IsAutoIncrement)
            {
                throw new InvalidOperationException("Create the column first, add the required key/index, and then enable auto increment through Edit column.");
            }

            NormalizeColumnType(definition.ColumnType);
            ValidateDefault(definition);
            ValidatePosition(metadata, definition);
        }

        // Validate edited column using schema-aware database operations.
        private static void ValidateEditedColumn(TableMetadata metadata, ColumnMetadata current, SchemaColumnDefinition definition)
        {
            ValidateEditableIdentifier(definition.Name, "column name");
            ValidateColumnPayloadLengths(definition);
            if (metadata.Columns.Any(column => !string.Equals(column.Name, current.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(column.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Another column named '" + definition.Name + "' already exists.");
            }

            string normalizedType = NormalizeColumnType(definition.ColumnType);
            if (current.IsPrimaryKey && definition.IsNullable)
            {
                throw new InvalidOperationException("Primary-key columns must remain NOT NULL. Remove or replace the primary key before making this column nullable.");
            }

            if (definition.IsAutoIncrement)
            {
                string baseType = GetBaseType(normalizedType);
                if (!IntegerTypes.Contains(baseType))
                {
                    throw new InvalidOperationException("Auto increment is only supported for integer column types.");
                }

                bool indexed = metadata.Indexes.Any(index => index.Columns.Count > 0 && string.Equals(index.Columns[0], current.Name, StringComparison.OrdinalIgnoreCase));
                if (!indexed)
                {
                    throw new InvalidOperationException("An auto-increment column must be the first column of an index. Add the required key before enabling auto increment.");
                }
            }

            ValidateDefault(definition);
            ValidatePosition(metadata, definition, current.Name);
        }

        // Build column definition using schema-aware database operations.
        private static string BuildColumnDefinition(
            TableMetadata metadata,
            SchemaColumnDefinition definition,
            bool isNew,
            ColumnMetadata? currentColumn)
        {
            string type = NormalizeColumnType(definition.ColumnType);
            string baseType = GetBaseType(type);
            List<string> parts = new()
            {
                MySqlIdentifier.Quote(definition.Name),
                type
            };

            // CHANGE COLUMN rewrites the complete column definition. Preserve an
            // existing per-column character set/collation instead of silently
            // resetting it to the table default when only another attribute is
            // being edited.
            if (!isNew && currentColumn != null && CharacterTypes.Contains(baseType))
            {
                if (!string.IsNullOrWhiteSpace(currentColumn.CharacterSetName))
                {
                    if (!SafeServerToken.IsMatch(currentColumn.CharacterSetName))
                    {
                        throw new InvalidOperationException("The existing column character set cannot be safely represented by the guarded schema editor.");
                    }

                    parts.Add("CHARACTER SET " + currentColumn.CharacterSetName);
                }

                if (!string.IsNullOrWhiteSpace(currentColumn.CollationName))
                {
                    if (!SafeServerToken.IsMatch(currentColumn.CollationName))
                    {
                        throw new InvalidOperationException("The existing column collation cannot be safely represented by the guarded schema editor.");
                    }

                    parts.Add("COLLATE " + currentColumn.CollationName);
                }
            }

            parts.Add(definition.IsNullable ? "NULL" : "NOT NULL");

            string mode = NormalizeDefaultMode(definition.DefaultMode);
            if (mode == "null")
            {
                parts.Add("DEFAULT NULL");
            }
            else if (mode == "value")
            {
                parts.Add("DEFAULT " + QuoteLiteral(definition.DefaultValue ?? string.Empty));
            }
            else if (mode == "current-timestamp")
            {
                parts.Add("DEFAULT " + BuildCurrentTimestampLiteral(type));
            }
            else if (mode == "bit-literal")
            {
                parts.Add("DEFAULT b'" + (definition.DefaultValue ?? string.Empty).Trim() + "'");
            }

            if (definition.IsAutoIncrement)
            {
                parts.Add("AUTO_INCREMENT");
            }

            if (!string.IsNullOrWhiteSpace(definition.Comment))
            {
                parts.Add("COMMENT " + QuoteLiteral(definition.Comment.Trim()));
            }

            string position = (definition.Position ?? "LAST").Trim().ToUpperInvariant();
            if (position == "FIRST")
            {
                parts.Add("FIRST");
            }
            else if (position == "AFTER")
            {
                parts.Add("AFTER " + MySqlIdentifier.Quote(definition.AfterColumn));
            }
            else if (!isNew && currentColumn != null)
            {
                ColumnMetadata? lastOtherColumn = metadata.Columns
                    .Where(column => !string.Equals(column.Name, currentColumn.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(column => column.OrdinalPosition)
                    .FirstOrDefault();
                if (lastOtherColumn != null)
                {
                    parts.Add("AFTER " + MySqlIdentifier.Quote(lastOtherColumn.Name));
                }
            }

            return string.Join(" ", parts);
        }

        // Build current timestamp literal using schema-aware database operations.
        private static string BuildCurrentTimestampLiteral(string normalizedType)
        {
            Match match = TypePattern.Match(normalizedType);
            if (!match.Success)
            {
                return "CURRENT_TIMESTAMP";
            }

            string args = match.Groups["args"].Value;
            if (!string.IsNullOrWhiteSpace(args) && LengthArguments.IsMatch(args))
            {
                return "CURRENT_TIMESTAMP" + args;
            }

            return "CURRENT_TIMESTAMP";
        }

        // Determine whether column position changed using schema-aware database operations.
        private static bool IsColumnPositionChanged(TableMetadata metadata, ColumnMetadata current, SchemaColumnDefinition definition)
        {
            string position = (definition.Position ?? "LAST").Trim().ToUpperInvariant();
            if (position == "FIRST")
            {
                return current.OrdinalPosition != 1;
            }

            if (position == "LAST")
            {
                return current.OrdinalPosition != metadata.Columns.Count;
            }

            if (position != "AFTER")
            {
                return false;
            }

            ColumnMetadata? currentPredecessor = metadata.Columns
                .Where(column => column.OrdinalPosition < current.OrdinalPosition)
                .OrderByDescending(column => column.OrdinalPosition)
                .FirstOrDefault();

            return currentPredecessor == null || !string.Equals(currentPredecessor.Name, definition.AfterColumn, StringComparison.OrdinalIgnoreCase);
        }

        // Validate position using schema-aware database operations.
        private static void ValidatePosition(TableMetadata metadata, SchemaColumnDefinition definition, string? currentColumn = null)
        {
            string position = (definition.Position ?? "LAST").Trim().ToUpperInvariant();
            if (position != "LAST" && position != "FIRST" && position != "AFTER")
            {
                throw new InvalidOperationException("Choose a valid column position.");
            }

            if (position == "AFTER")
            {
                if (string.IsNullOrWhiteSpace(definition.AfterColumn))
                {
                    throw new InvalidOperationException("Choose the column that this field should follow.");
                }

                ColumnMetadata after = FindColumn(metadata, definition.AfterColumn);
                if (!string.IsNullOrWhiteSpace(currentColumn) && string.Equals(after.Name, currentColumn, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("A column cannot be positioned after itself.");
                }
            }
        }

        // Validate default using schema-aware database operations.
        private static void ValidateDefault(SchemaColumnDefinition definition)
        {
            string mode = NormalizeDefaultMode(definition.DefaultMode);
            if (mode == "null" && !definition.IsNullable)
            {
                throw new InvalidOperationException("A NOT NULL column cannot use NULL as its default value.");
            }

            string baseType = GetBaseType(NormalizeColumnType(definition.ColumnType));
            if (mode == "current-timestamp" && !TemporalTypes.Contains(baseType))
            {
                throw new InvalidOperationException("CURRENT_TIMESTAMP can only be selected for DATETIME or TIMESTAMP columns.");
            }

            if (mode == "bit-literal")
            {
                if (!string.Equals(baseType, "bit", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("BIT literal defaults can only be used with BIT columns.");
                }

                if (!Regex.IsMatch((definition.DefaultValue ?? string.Empty).Trim(), "^[01]+$", RegexOptions.CultureInvariant))
                {
                    throw new InvalidOperationException("A BIT literal default may contain only 0 and 1.");
                }
            }
        }

        // Normalize default mode using schema-aware database operations.
        private static string NormalizeDefaultMode(string? value)
        {
            string mode = (value ?? "none").Trim().ToLowerInvariant();
            return mode is "none" or "null" or "value" or "current-timestamp" or "bit-literal"
                ? mode
                : throw new InvalidOperationException("Choose a valid default-value mode.");
        }

        // Normalize column type using schema-aware database operations.
        private static string NormalizeColumnType(string input)
        {
            string value = Regex.Replace((input ?? string.Empty).Trim(), @"\s+", " ");
            if (value.Length > 2048)
            {
                throw new InvalidOperationException("The column type cannot exceed 2048 characters.");
            }

            if (string.IsNullOrWhiteSpace(value) || value.Contains("--", StringComparison.Ordinal) || value.Contains("/*", StringComparison.Ordinal) || value.Contains("*/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Enter a supported MySQL/MariaDB column type.");
            }

            Match match = TypePattern.Match(value);
            if (!match.Success)
            {
                throw new InvalidOperationException("The column type contains unsupported syntax.");
            }

            string baseType = match.Groups["base"].Value.ToLowerInvariant();
            string args = match.Groups["args"].Value;
            string mods = Regex.Replace(match.Groups["mods"].Value.Trim(), @"\s+", " ").ToLowerInvariant();
            if (!AllowedTypes.Contains(baseType))
            {
                throw new InvalidOperationException("Column type '" + baseType + "' is not enabled in the schema editor.");
            }

            if ((baseType is "char" or "varchar" or "binary" or "varbinary" or "enum" or "set") && string.IsNullOrWhiteSpace(args))
            {
                throw new InvalidOperationException("Column type '" + baseType + "' requires a size or value list.");
            }

            if (!string.IsNullOrWhiteSpace(args))
            {
                bool validArgs;
                if (baseType is "enum" or "set")
                {
                    validArgs = EnumSetArguments.IsMatch(args);
                }
                else if (baseType is "decimal" or "numeric" or "float" or "double" or "real")
                {
                    validArgs = NumericArguments.IsMatch(args);
                }
                else
                {
                    validArgs = LengthArguments.IsMatch(args);
                }

                if (!validArgs)
                {
                    throw new InvalidOperationException("The size/precision portion of the column type is invalid.");
                }
            }

            if ((mods.Contains("unsigned", StringComparison.OrdinalIgnoreCase) || mods.Contains("zerofill", StringComparison.OrdinalIgnoreCase)) &&
                !(IntegerTypes.Contains(baseType) || baseType is "decimal" or "numeric" or "float" or "double" or "real"))
            {
                throw new InvalidOperationException("UNSIGNED/ZEROFILL can only be used with numeric column types.");
            }

            if (mods.Contains("binary", StringComparison.OrdinalIgnoreCase) && !CharacterTypes.Contains(baseType))
            {
                throw new InvalidOperationException("The BINARY modifier can only be used with character-based column types.");
            }

            return string.IsNullOrWhiteSpace(mods) ? baseType + args : baseType + args + " " + mods;
        }

        // Read base type using schema-aware database operations.
        private static string GetBaseType(string normalizedType)
        {
            Match match = TypePattern.Match(normalizedType);
            return match.Success ? match.Groups["base"].Value : normalizedType;
        }

        // Handle equivalent type within the my sql schema editor service workflow.
        private static bool EquivalentType(string left, string right)
        {
            try
            {
                string normalizedLeft = NormalizeColumnType(left);
                string normalizedRight = NormalizeColumnType(right);
                string baseType = GetBaseType(normalizedLeft);
                StringComparison comparison = baseType is "enum" or "set"
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
                return string.Equals(normalizedLeft, normalizedRight, comparison);
            }
            catch
            {
                return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
            }
        }

        // Normalize index kind using schema-aware database operations.
        private static string NormalizeIndexKind(string? value)
        {
            string kind = (value ?? "INDEX").Trim().ToUpperInvariant();
            return kind is "INDEX" or "UNIQUE" or "FULLTEXT" or "PRIMARY"
                ? kind
                : throw new InvalidOperationException("Choose a supported index type.");
        }

        // Read character set for collation using schema-aware database operations.
        private async Task<string> GetCharacterSetForCollationAsync(ConnectionProfile profile, string collation, CancellationToken cancellationToken)
        {
            const string sql = "SELECT CHARACTER_SET_NAME FROM INFORMATION_SCHEMA.COLLATIONS WHERE COLLATION_NAME = @collation LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@collation", collation);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            string charset = Convert.ToString(value) ?? string.Empty;
            if (!SafeServerToken.IsMatch(charset))
            {
                throw new InvalidOperationException("The selected collation does not expose a valid character set.");
            }

            return charset;
        }

        // Handle qualified within the my sql schema editor service workflow.
        private static string Qualified(string database, string table)
        {
            return MySqlIdentifier.Quote(database) + "." + MySqlIdentifier.Quote(table);
        }

        // Escape and quote literal using schema-aware database operations.
        private static string QuoteLiteral(string value)
        {
            return "'" + (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\0", "\\0", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal)
                .Replace("\b", "\\b", StringComparison.Ordinal)
                .Replace(((char)26).ToString(), "\\Z", StringComparison.Ordinal)
                .Replace("'", "''", StringComparison.Ordinal) + "'";
        }
    }
}
