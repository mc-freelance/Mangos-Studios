// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class LootViewerService : ILootViewerService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the loot viewer service dependencies and state.
        public LootViewerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Read loot tables using schema-aware database operations.
        public IReadOnlyList<LootTableDescriptor> GetLootTables(IReadOnlyList<TableMetadata> tables)
        {
            return tables
                .Where(table => table.Name.EndsWith("_loot_template", StringComparison.OrdinalIgnoreCase))
                .OrderBy(table => GetTableLabel(table.Name), StringComparer.OrdinalIgnoreCase)
                .Select(table => new LootTableDescriptor
                {
                    TableName = table.Name,
                    Label = GetTableLabel(table.Name),
                    ApproximateRows = table.ApproximateRows
                })
                .ToArray();
        }

        // Search for templates using schema-aware database operations.
        public async Task<PagedResult<LootTemplateSummary>> SearchTemplatesAsync(
            ConnectionProfile profile,
            IReadOnlyList<TableMetadata> lootTables,
            string? tableFilter,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            string normalizedTable = tableFilter?.Trim() ?? string.Empty;
            string normalizedSearch = search?.Trim() ?? string.Empty;

            List<(TableMetadata Table, LootSchema Schema)> usable = new();
            foreach (TableMetadata table in lootTables)
            {
                if (!string.IsNullOrWhiteSpace(normalizedTable) &&
                    !string.Equals(normalizedTable, "all", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(table.Name, normalizedTable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LootSchema.TryCreate(table, out LootSchema? schema))
                {
                    usable.Add((table, schema!));
                }
            }

            if (usable.Count == 0)
            {
                return new PagedResult<LootTemplateSummary> { Page = page, PageSize = pageSize };
            }

            string union = string.Join(" UNION ALL ", usable.Select(pair => BuildTemplateSummaryBranch(pair.Table, pair.Schema)));
            string where = string.IsNullOrWhiteSpace(normalizedSearch)
                ? string.Empty
                : " WHERE CAST(x.entry AS CHAR) LIKE @search OR x.table_name LIKE @search";
            string countSql = "SELECT COUNT(*) FROM (" + union + ") x" + where + ";";

            await using MySqlConnection connection = _connectionFactory.Create(profile, usable[0].Table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            long totalCount;
            await using (MySqlCommand count = new(countSql, connection))
            {
                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    count.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
                }

                object? scalar = await count.ExecuteScalarAsync(cancellationToken);
                totalCount = Convert.ToInt64(scalar ?? 0, CultureInfo.InvariantCulture);
            }

            int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            int offset = checked((page - 1) * pageSize);

            string sql = "SELECT x.table_name, x.entry, x.row_count, x.direct_item_count, x.reference_count FROM (" + union + ") x" +
                         where + " ORDER BY x.table_name, x.entry LIMIT @limit OFFSET @offset;";
            List<LootTemplateSummary> rows = new();
            await using MySqlCommand command = new(sql, connection);
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                command.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
            }

            command.Parameters.AddWithValue("@limit", pageSize);
            command.Parameters.AddWithValue("@offset", offset);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string tableName = reader.GetString(0);
                rows.Add(new LootTemplateSummary
                {
                    TableName = tableName,
                    TableLabel = GetTableLabel(tableName),
                    Entry = reader.GetInt64(1),
                    RowCount = reader.GetInt64(2),
                    DirectItemCount = reader.GetInt64(3),
                    ReferenceCount = reader.GetInt64(4)
                });
            }

            return new PagedResult<LootTemplateSummary>
            {
                Items = rows,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // Read template using schema-aware database operations.
        public async Task<LootTemplateDetail?> GetTemplateAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long entry,
            int rowPage,
            int rowPageSize,
            CancellationToken cancellationToken)
        {
            if (!LootSchema.TryCreate(lootTable, out LootSchema? schema))
            {
                return null;
            }

            rowPage = Math.Max(1, rowPage);
            rowPageSize = Math.Clamp(rowPageSize, 10, _options.MaximumBrowsePageSize);

            await using MySqlConnection connection = _connectionFactory.Create(profile, lootTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            string countSql = "SELECT COUNT(*) FROM " + Quote(lootTable.Name) + " WHERE " + Quote(schema!.Entry.Name) + " = @entry;";
            long totalRows;
            await using (MySqlCommand count = new(countSql, connection))
            {
                count.Parameters.AddWithValue("@entry", entry);
                totalRows = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            if (totalRows == 0)
            {
                return null;
            }

            int totalPages = (int)Math.Ceiling(totalRows / (double)rowPageSize);
            if (rowPage > totalPages)
            {
                rowPage = totalPages;
            }

            int offset = checked((rowPage - 1) * rowPageSize);

            ColumnMetadata? itemEntry = itemTemplate == null ? null : Optional(itemTemplate, "entry", "Entry");
            ColumnMetadata? itemName = itemTemplate == null ? null : Optional(itemTemplate, "name", "Name");
            bool canResolveItems = schema.Item != null && itemTemplate != null && itemEntry != null && itemName != null;
            string itemJoin = canResolveItems
                ? " LEFT JOIN " + Quote(itemTemplate!.Name) + " it ON it." + Quote(itemEntry!.Name) + " = l." + Quote(schema.Item!.Name)
                : string.Empty;
            string itemNameExpr = canResolveItems ? "it." + Quote(itemName!.Name) : "NULL";

            LootSchema? referenceSchema = null;
            bool canCheckReference = referenceLootTable != null && LootSchema.TryCreate(referenceLootTable, out referenceSchema) && schema.MinCountOrRef != null;
            string referenceExistsExpr = canCheckReference
                ? "CASE WHEN l." + Quote(schema.MinCountOrRef!.Name) + " < 0 AND EXISTS (SELECT 1 FROM " + Quote(referenceLootTable!.Name) + " r WHERE r." + Quote(referenceSchema!.Entry.Name) + " = ABS(l." + Quote(schema.MinCountOrRef.Name) + ")) THEN 1 ELSE 0 END"
                : "0";

            string sql = "SELECT " +
                         ColumnExpr("l", schema.Item) + ", " + itemNameExpr + ", " +
                         ColumnExpr("l", schema.Chance) + ", " + ColumnExpr("l", schema.GroupId) + ", " +
                         ColumnExpr("l", schema.MinCountOrRef) + ", " + ColumnExpr("l", schema.MaxCount) + ", " +
                         ColumnExpr("l", schema.ConditionId) + ", " + referenceExistsExpr +
                         " FROM " + Quote(lootTable.Name) + " l" + itemJoin +
                         " WHERE l." + Quote(schema.Entry.Name) + " = @entry" +
                         " ORDER BY " + (schema.GroupId != null ? "l." + Quote(schema.GroupId.Name) + ", " : string.Empty) +
                         (schema.Item != null ? "l." + Quote(schema.Item.Name) : "l." + Quote(schema.Entry.Name)) +
                         " LIMIT @limit OFFSET @offset;";

            List<LootItemRow> rows = new();
            await using (MySqlCommand command = new(sql, connection))
            {
                command.Parameters.AddWithValue("@entry", entry);
                command.Parameters.AddWithValue("@limit", rowPageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    long itemId = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                    string itemResolvedName = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
                    rows.Add(new LootItemRow
                    {
                        Entry = entry,
                        ItemId = itemId,
                        ItemName = itemResolvedName,
                        IsItemResolved = !string.IsNullOrWhiteSpace(itemResolvedName),
                        Chance = ReadNullableDouble(reader, 2),
                        GroupId = ReadNullableLong(reader, 3),
                        MinCountOrReference = ReadNullableLong(reader, 4),
                        MaxCount = ReadNullableLong(reader, 5),
                        ConditionId = ReadNullableLong(reader, 6),
                        ReferenceExists = !reader.IsDBNull(7) && Convert.ToInt32(reader.GetValue(7),
                        CultureInfo.InvariantCulture) != 0
                    });
                }
            }

            long referenceCount = 0;
            if (schema.MinCountOrRef != null)
            {
                string refCountSql = "SELECT COUNT(*) FROM " + Quote(lootTable.Name) + " WHERE " + Quote(schema.Entry.Name) + " = @entry AND " + Quote(schema.MinCountOrRef.Name) + " < 0;";
                await using MySqlCommand refCount = new(refCountSql, connection);
                refCount.Parameters.AddWithValue("@entry", entry);
                referenceCount = Convert.ToInt64(await refCount.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            (IReadOnlyList<LootSourceSummary> sources, long sourceCount) = await LoadSourcesForTemplateAsync(
                profile, creatureTemplate, gameObjectTemplate, lootTable.Name, entry, cancellationToken);

            return new LootTemplateDetail
            {
                Template = new LootTemplateSummary
                {
                    TableName = lootTable.Name,
                    TableLabel = GetTableLabel(lootTable.Name),
                    Entry = entry,
                    RowCount = totalRows,
                    DirectItemCount = Math.Max(0, totalRows - referenceCount),
                    ReferenceCount = referenceCount
                },

                Rows = new PagedResult<LootItemRow>
                {
                    Items = rows,
                    TotalCount = totalRows,
                    Page = rowPage,
                    PageSize = rowPageSize
                },

                Sources = sources,
                TotalSourceCount = sourceCount
            };
        }

        // Report which editor operations the detected database schema supports.
        public LootEditorCapabilities GetEditorCapabilities(TableMetadata lootTable)
        {
            if (!LootSchema.TryCreate(lootTable, out LootSchema? schema))
            {
                return new LootEditorCapabilities
                {
                    TableName = lootTable.Name,
                    TableLabel = GetTableLabel(lootTable.Name),
                    CanMutate = false
                };
            }

            HashSet<string> supportedColumns = GetSupportedColumnNames(schema!);
            bool writableColumnsSupported = lootTable.Columns
                .Where(column => !column.IsGenerated && !column.IsAutoIncrement)
                .All(column => supportedColumns.Contains(column.Name));
            bool primaryKeySupported = lootTable.PrimaryKeyColumns.Count > 0 &&
                                       lootTable.PrimaryKeyColumns.All(column => supportedColumns.Contains(column.Name));

            return new LootEditorCapabilities
            {
                TableName = lootTable.Name,
                TableLabel = GetTableLabel(lootTable.Name),
                CanMutate = schema!.Item != null && writableColumnsSupported && primaryKeySupported,
                HasItem = schema.Item != null,
                HasChance = schema.Chance != null,
                HasGroupId = schema.GroupId != null,
                HasMinCountOrReference = schema.MinCountOrRef != null,
                SupportsReferences = schema.MinCountOrRef?.Name.IndexOf("ref", StringComparison.OrdinalIgnoreCase) >= 0,
                HasMaxCount = schema.MaxCount != null,
                HasConditionId = schema.ConditionId != null
            };
        }

        // Add row using schema-aware database operations.
        public async Task<LootMutationResult> AddRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            LootRowValues values,
            CancellationToken cancellationToken)
        {
            LootSchema schema = RequireMutableSchema(lootTable);
            ValidateRowValues(schema, values);
            await ValidateResolvedTargetAsync(profile, lootTable, schema, itemTemplate, referenceLootTable, values, cancellationToken);

            if (await GetExistingRowAsync(profile, lootTable, schema, values, cancellationToken) != null)
            {
                throw new InvalidOperationException("A loot row with that entry and row item already exists in " + lootTable.Name + ".");
            }

            List<ColumnMetadata> columns = GetWritableColumns(schema);
            string sql = "INSERT INTO " + Quote(lootTable.Name) + " (" + string.Join(", ", columns.Select(column => Quote(column.Name))) +
                         ") VALUES (" + string.Join(", ", columns.Select((_, index) => "@v" + index.ToString(CultureInfo.InvariantCulture))) + ");";

            await using MySqlConnection connection = _connectionFactory.Create(profile, lootTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValueParameters(command, columns, schema, values, "@v");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new LootMutationResult
            {
                AffectedRows = affected,
                ObjectName = lootTable.Name,
                SqlPreview = BuildInsertPreview(lootTable, schema, values),
                RevertSqlPreview = BuildDeletePreview(lootTable, schema, values),
                RemainingRowsInDefinition = await CountDefinitionRowsAsync(profile, lootTable, schema, values.Entry, cancellationToken)
            };
        }

        // Update row using schema-aware database operations.
        public async Task<LootMutationResult> UpdateRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            LootRowValues original,
            LootRowValues replacement,
            CancellationToken cancellationToken)
        {
            LootSchema schema = RequireMutableSchema(lootTable);
            ValidateRowValues(schema, replacement);
            LootRowValues? persisted = await GetExistingRowAsync(profile, lootTable, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected loot row no longer exists.");
            if (replacement.Entry != persisted.Entry)
            {
                throw new InvalidOperationException("A loot row cannot be moved to another definition from the row editor.");
            }

            await ValidateResolvedTargetAsync(profile, lootTable, schema, itemTemplate, referenceLootTable, replacement, cancellationToken);
            if (replacement.ItemId != persisted.ItemId && await GetExistingRowAsync(profile, lootTable, schema, replacement, cancellationToken) != null)
            {
                throw new InvalidOperationException("Another loot row already uses item/row key " + replacement.ItemId.ToString(CultureInfo.InvariantCulture) + " in this definition.");
            }

            List<ColumnMetadata> setters = GetWritableColumns(schema).Where(column => !string.Equals(column.Name, schema.Entry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            string setSql = string.Join(", ", setters.Select((column, index) => Quote(column.Name) + " = @v" + index.ToString(CultureInfo.InvariantCulture)));
            string whereSql = BuildPrimaryKeyWhere(lootTable, schema, persisted, "@k");
            string sql = "UPDATE " + Quote(lootTable.Name) + " SET " + setSql + " WHERE " + whereSql + " LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, lootTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValueParameters(command, setters, schema, replacement, "@v");
            AddPrimaryKeyParameters(command, lootTable, schema, persisted, "@k");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new LootMutationResult
            {
                AffectedRows = affected,
                ObjectName = lootTable.Name,
                SqlPreview = BuildUpdatePreview(lootTable, schema, persisted, replacement),
                RevertSqlPreview = BuildUpdatePreview(lootTable, schema, replacement, persisted),
                RemainingRowsInDefinition = await CountDefinitionRowsAsync(profile, lootTable, schema, replacement.Entry, cancellationToken)
            };
        }

        // Delete row using schema-aware database operations.
        public async Task<LootMutationResult> DeleteRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            LootRowValues original,
            CancellationToken cancellationToken)
        {
            LootSchema schema = RequireMutableSchema(lootTable);
            LootRowValues? persisted = await GetExistingRowAsync(profile, lootTable, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected loot row no longer exists.");
            string whereSql = BuildPrimaryKeyWhere(lootTable, schema, persisted, "@k");
            string sql = "DELETE FROM " + Quote(lootTable.Name) + " WHERE " + whereSql + " LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, lootTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddPrimaryKeyParameters(command, lootTable, schema, persisted, "@k");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            long remaining = await CountDefinitionRowsAsync(profile, lootTable, schema, persisted.Entry, cancellationToken);

            return new LootMutationResult
            {
                AffectedRows = affected,
                ObjectName = lootTable.Name,
                SqlPreview = BuildDeletePreview(lootTable, schema, persisted),
                RevertSqlPreview = BuildInsertPreview(lootTable, schema, persisted),
                RemainingRowsInDefinition = remaining
            };
        }

        // Search creature and game-object sources referencing gossip menus.
        public async Task<PagedResult<LootSourceSummary>> SearchSourcesAsync(
            ConnectionProfile profile,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            IReadOnlyDictionary<string, TableMetadata> lootTables,
            string? sourceFilter,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            string normalizedSource = LootSourceFilters.Normalize(sourceFilter);
            string normalizedSearch = search?.Trim() ?? string.Empty;

            List<string> branches = BuildSourceBranches(creatureTemplate, gameObjectTemplate, normalizedSource);
            if (branches.Count == 0)
            {
                return new PagedResult<LootSourceSummary> { Page = page, PageSize = pageSize };
            }

            string union = string.Join(" UNION ALL ", branches);
            string where = string.IsNullOrWhiteSpace(normalizedSearch)
                ? string.Empty
                : " WHERE x.entity_name LIKE @search OR CAST(x.entity_entry AS CHAR) LIKE @search OR CAST(x.loot_entry AS CHAR) LIKE @search OR x.loot_kind LIKE @search";
            string countSql = "SELECT COUNT(*) FROM (" + union + ") x" + where + ";";

            string database = creatureTemplate?.DatabaseName ?? gameObjectTemplate?.DatabaseName ?? lootTables.Values.FirstOrDefault()?.DatabaseName ?? profile.DefaultDatabase;
            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            long totalCount;
            await using (MySqlCommand count = new(countSql, connection))
            {
                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    count.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
                }

                totalCount = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            int offset = checked((page - 1) * pageSize);
            string sql = "SELECT x.entity_type, x.entity_entry, x.entity_name, x.loot_kind, x.loot_table_name, x.loot_entry FROM (" + union + ") x" +
                         where + " ORDER BY x.entity_type, x.entity_name, x.entity_entry, x.loot_kind LIMIT @limit OFFSET @offset;";

            List<LootSourceSummary> rows = new();
            await using (MySqlCommand command = new(sql, connection))
            {
                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    command.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
                }

                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string tableName = reader.GetString(4);
                    rows.Add(new LootSourceSummary
                    {
                        EntityType = reader.GetString(0),
                        EntityEntry = reader.GetInt64(1),
                        EntityName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LootKind = reader.GetString(3),
                        LootTableName = tableName,
                        LootTableLabel = GetTableLabel(tableName),
                        LootEntry = reader.GetInt64(5),
                        TargetTableAvailable = lootTables.ContainsKey(tableName)
                    });
                }
            }

            await PopulateTargetStatusAsync(connection, rows, lootTables, cancellationToken);
            return new PagedResult<LootSourceSummary>
            {
                Items = rows,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // Load sources for template using schema-aware database operations.
        private async Task<(IReadOnlyList<LootSourceSummary> Items, long TotalCount)> LoadSourcesForTemplateAsync(
            ConnectionProfile profile,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            string lootTableName,
            long lootEntry,
            CancellationToken cancellationToken)
        {
            List<string> branches = BuildSourceBranches(creatureTemplate, gameObjectTemplate, LootSourceFilters.All);
            if (branches.Count == 0)
            {
                return (Array.Empty<LootSourceSummary>(), 0);
            }

            string union = string.Join(" UNION ALL ", branches);
            string database = creatureTemplate?.DatabaseName ?? gameObjectTemplate?.DatabaseName ?? profile.DefaultDatabase;
            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);

            const string where = " WHERE x.loot_table_name = @tableName AND x.loot_entry = @lootEntry";
            string countSql = "SELECT COUNT(*) FROM (" + union + ") x" + where + ";";
            long total;
            await using (MySqlCommand count = new(countSql, connection))
            {
                count.Parameters.AddWithValue("@tableName", lootTableName);
                count.Parameters.AddWithValue("@lootEntry", lootEntry);
                total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            string sql = "SELECT x.entity_type, x.entity_entry, x.entity_name, x.loot_kind, x.loot_table_name, x.loot_entry FROM (" + union + ") x" + where +
                         " ORDER BY x.entity_type, x.entity_name, x.entity_entry LIMIT 24;";
            List<LootSourceSummary> rows = new();
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@tableName", lootTableName);
            command.Parameters.AddWithValue("@lootEntry", lootEntry);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LootSourceSummary
                {
                    EntityType = reader.GetString(0),
                    EntityEntry = reader.GetInt64(1),
                    EntityName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    LootKind = reader.GetString(3),
                    LootTableName = reader.GetString(4),
                    LootTableLabel = GetTableLabel(reader.GetString(4)),
                    LootEntry = reader.GetInt64(5),
                    TargetTableAvailable = true,
                    TargetExists = true,
                    LootRowCount = 0
                });
            }

            return (rows, total);
        }

        // Build source branches using schema-aware database operations.
        private static List<string> BuildSourceBranches(TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, string sourceFilter)
        {
            List<string> branches = new();
            if (creatureTemplate != null && sourceFilter != LootSourceFilters.GameObjects)
            {
                ColumnMetadata? entry = Optional(creatureTemplate, "Entry", "entry");
                ColumnMetadata? name = Optional(creatureTemplate, "Name", "name");
                if (entry != null)
                {
                    AddCreatureBranch(branches, creatureTemplate, entry, name, Optional(creatureTemplate, "LootId", "lootid", "loot_id"), "Corpse loot", "creature_loot_template");
                    AddCreatureBranch(branches, creatureTemplate, entry, name, Optional(creatureTemplate, "PickpocketLootId", "pickpocketlootid", "pickpocketloot"), "Pickpocket loot", "pickpocketing_loot_template");
                    AddCreatureBranch(branches, creatureTemplate, entry, name, Optional(creatureTemplate, "SkinningLootId", "skinninglootid", "skinloot", "skinningloot"), "Skinning loot", "skinning_loot_template");
                }
            }

            if (gameObjectTemplate != null && sourceFilter != LootSourceFilters.Creatures)
            {
                ColumnMetadata? entry = Optional(gameObjectTemplate, "entry", "Entry");
                ColumnMetadata? name = Optional(gameObjectTemplate, "name", "Name");
                ColumnMetadata? type = Optional(gameObjectTemplate, "type", "Type");
                ColumnMetadata? data1 = Optional(gameObjectTemplate, "data1", "Data1");
                if (entry != null && type != null && data1 != null)
                {
                    string nameExpr = name == null ? "''" : "COALESCE(" + Quote(name.Name) + ", '')";
                    branches.Add(
                        "SELECT 'GameObject' AS entity_type, CAST(" + Quote(entry.Name) + " AS SIGNED) AS entity_entry, " + nameExpr + " AS entity_name, " +
                        "CASE WHEN " + Quote(type.Name) + " = 25 THEN 'Fishing-hole loot' ELSE 'Chest loot' END AS loot_kind, " +
                        "'gameobject_loot_template' AS loot_table_name, CAST(" + Quote(data1.Name) + " AS SIGNED) AS loot_entry " +
                        "FROM " + Quote(gameObjectTemplate.Name) + " WHERE " + Quote(type.Name) + " IN (3,25) AND " + Quote(data1.Name) + " > 0");
                }
            }

            return branches;
        }

        // Add creature branch using schema-aware database operations.
        private static void AddCreatureBranch(List<string> branches, TableMetadata table, ColumnMetadata entry, ColumnMetadata? name, ColumnMetadata? lootColumn, string kind, string lootTable)
        {
            if (lootColumn == null)
            {
                return;
            }

            string nameExpr = name == null ? "''" : "COALESCE(" + Quote(name.Name) + ", '')";
            branches.Add(
                "SELECT 'Creature' AS entity_type, CAST(" + Quote(entry.Name) + " AS SIGNED) AS entity_entry, " + nameExpr + " AS entity_name, '" + kind.Replace("'", "''") + "' AS loot_kind, " +
                "'" + lootTable + "' AS loot_table_name, CAST(" + Quote(lootColumn.Name) + " AS SIGNED) AS loot_entry FROM " + Quote(table.Name) +
                " WHERE " + Quote(lootColumn.Name) + " > 0");
        }

        // Handle populate target status async within the loot viewer service workflow.
        private static async Task PopulateTargetStatusAsync(MySqlConnection connection, List<LootSourceSummary> rows, IReadOnlyDictionary<string, TableMetadata> lootTables, CancellationToken cancellationToken)
        {
            foreach (IGrouping<string, LootSourceSummary> group in rows.Where(row => row.TargetTableAvailable).GroupBy(row => row.LootTableName, StringComparer.OrdinalIgnoreCase))
            {
                if (!lootTables.TryGetValue(group.Key, out TableMetadata? table) || !LootSchema.TryCreate(table, out LootSchema? schema))
                {
                    continue;
                }

                long[] entries = group.Select(row => row.LootEntry).Distinct().ToArray();
                if (entries.Length == 0)
                {
                    continue;
                }

                List<string> parameterNames = new();
                await using MySqlCommand command = connection.CreateCommand();
                for (int i = 0; i < entries.Length; i++)
                {
                    string name = "@entry" + i.ToString(CultureInfo.InvariantCulture);
                    parameterNames.Add(name);
                    command.Parameters.AddWithValue(name, entries[i]);
                }

                command.CommandText = "SELECT CAST(" + Quote(schema!.Entry.Name) + " AS SIGNED), COUNT(*) FROM " + Quote(table.Name) +
                                      " WHERE " + Quote(schema.Entry.Name) + " IN (" + string.Join(",", parameterNames) + ") GROUP BY " + Quote(schema.Entry.Name) + ";";
                Dictionary<long, long> counts = new();
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    counts[reader.GetInt64(0)] = reader.GetInt64(1);
                }

                foreach (LootSourceSummary row in group)
                {
                    row.LootRowCount = counts.TryGetValue(row.LootEntry, out long count) ? count : 0;
                    row.TargetExists = row.LootRowCount > 0;
                }
            }
        }

        // Require mutable schema using schema-aware database operations.
        private LootSchema RequireMutableSchema(TableMetadata lootTable)
        {
            if (!LootSchema.TryCreate(lootTable, out LootSchema? schema) || schema!.Item == null)
            {
                throw new InvalidOperationException("This loot table does not expose the standard entry/item row structure required by the Loot Viewer editor.");
            }

            LootEditorCapabilities capabilities = GetEditorCapabilities(lootTable);
            if (!capabilities.CanMutate)
            {
                throw new InvalidOperationException("This loot table contains columns or primary-key fields that the Loot Viewer does not recognize safely. Use the advanced table editor for this schema.");
            }

            return schema;
        }

        // Read supported column names using schema-aware database operations.
        private static HashSet<string> GetSupportedColumnNames(LootSchema schema)
        {
            return new HashSet<string>(GetWritableColumns(schema).Select(column => column.Name), StringComparer.OrdinalIgnoreCase);
        }

        // Read writable columns using schema-aware database operations.
        private static List<ColumnMetadata> GetWritableColumns(LootSchema schema)
        {
            return new[] { schema.Entry, schema.Item, schema.Chance, schema.GroupId, schema.MinCountOrRef, schema.MaxCount, schema.ConditionId }
                .Where(column => column != null)
                .Cast<ColumnMetadata>()
                .OrderBy(column => column.OrdinalPosition)
                .ToList();
        }

        // Validate row values using schema-aware database operations.
        private static void ValidateRowValues(LootSchema schema, LootRowValues values)
        {
            if (values.Entry <= 0)
            {
                throw new InvalidOperationException("Loot entry must be greater than zero.");
            }

            bool isReference = schema.MinCountOrRef != null && values.MinCountOrReference.HasValue && values.MinCountOrReference.Value < 0;
            if (schema.Item != null && ((!isReference && values.ItemId <= 0) || (isReference && values.ItemId < 0)))
            {
                throw new InvalidOperationException(isReference ? "Reference row key cannot be negative." : "Item entry must be greater than zero.");
            }

            if (schema.Chance != null && values.Chance.HasValue && (values.Chance.Value < -100d || values.Chance.Value > 100d))
            {
                throw new InvalidOperationException("Loot chance must be between -100 and 100 percent.");
            }

            if (schema.GroupId != null && values.GroupId.HasValue && values.GroupId.Value < 0)
            {
                throw new InvalidOperationException("Loot group cannot be negative.");
            }

            if (schema.MinCountOrRef != null && (!values.MinCountOrReference.HasValue || values.MinCountOrReference.Value == 0))
            {
                throw new InvalidOperationException("Minimum count / reference cannot be zero.");
            }

            if (schema.MaxCount != null && (!values.MaxCount.HasValue || values.MaxCount.Value < 1))
            {
                throw new InvalidOperationException("Maximum count / reference repetitions must be at least 1.");
            }

            if (schema.ConditionId != null && values.ConditionId.HasValue && values.ConditionId.Value < 0)
            {
                throw new InvalidOperationException("Condition ID cannot be negative.");
            }
        }

        // Validate resolved target using schema-aware database operations.
        private async Task ValidateResolvedTargetAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            LootSchema schema,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            LootRowValues values,
            CancellationToken cancellationToken)
        {
            bool isReference = schema.MinCountOrRef != null && values.MinCountOrReference.HasValue && values.MinCountOrReference.Value < 0;
            if (isReference)
            {
                long referenceEntry = Math.Abs(values.MinCountOrReference!.Value);
                if (referenceEntry <= 0)
                {
                    throw new InvalidOperationException("Reference loot entry must be greater than zero.");
                }

                if (referenceLootTable == null || !LootSchema.TryCreate(referenceLootTable, out LootSchema? referenceSchema))
                {
                    throw new InvalidOperationException("reference_loot_template is not available, so reference rows cannot be validated safely.");
                }

                bool editingReferenceTable = string.Equals(lootTable.Name, referenceLootTable.Name, StringComparison.OrdinalIgnoreCase);
                if (editingReferenceTable && values.Entry == referenceEntry)
                {
                    throw new InvalidOperationException("A reference loot definition cannot reference itself.");
                }

                if (!await DefinitionExistsAsync(profile, referenceLootTable, referenceSchema!, referenceEntry, cancellationToken))
                {
                    throw new InvalidOperationException("Reference loot entry " + referenceEntry.ToString(CultureInfo.InvariantCulture) + " does not exist in reference_loot_template.");
                }

                if (editingReferenceTable && await WouldCreateReferenceCycleAsync(profile, referenceLootTable, referenceSchema!, values.Entry, referenceEntry, cancellationToken))
                {
                    throw new InvalidOperationException("That reference-loot relationship would create a circular reference chain.");
                }

                return;
            }

            if (itemTemplate == null)
            {
                return;
            }

            ColumnMetadata? itemEntry = Optional(itemTemplate, "entry", "Entry");
            if (itemEntry == null)
            {
                return;
            }

            await using MySqlConnection connection = _connectionFactory.Create(profile, itemTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            string sql = "SELECT 1 FROM " + Quote(itemTemplate.Name) + " WHERE " + Quote(itemEntry.Name) + " = @entry LIMIT 1;";
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", values.ItemId);
            if (await command.ExecuteScalarAsync(cancellationToken) == null)
            {
                throw new InvalidOperationException("Item entry " + values.ItemId.ToString(CultureInfo.InvariantCulture) + " does not exist in item_template.");
            }
        }

        // Handle would create reference cycle async within the loot viewer service workflow.
        private async Task<bool> WouldCreateReferenceCycleAsync(
            ConnectionProfile profile,
            TableMetadata referenceLootTable,
            LootSchema schema,
            long sourceEntry,
            long targetEntry,
            CancellationToken cancellationToken)
        {
            if (schema.MinCountOrRef == null)
            {
                return false;
            }

            HashSet<long> visited = new();
            Queue<long> pending = new();
            pending.Enqueue(targetEntry);

            await using MySqlConnection connection = _connectionFactory.Create(profile, referenceLootTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            while (pending.Count > 0 && visited.Count < 4096)
            {
                long current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (current == sourceEntry)
                {
                    return true;
                }

                string sql = "SELECT DISTINCT ABS(" + Quote(schema.MinCountOrRef.Name) + ") FROM " + Quote(referenceLootTable.Name) +
                             " WHERE " + Quote(schema.Entry.Name) + " = @entry AND " + Quote(schema.MinCountOrRef.Name) + " < 0;";
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@entry", current);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        pending.Enqueue(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                    }
                }
            }

            return false;
        }

        // Handle definition exists async within the loot viewer service workflow.
        private async Task<bool> DefinitionExistsAsync(ConnectionProfile profile, TableMetadata table, LootSchema schema, long entry, CancellationToken cancellationToken)
        {
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            string sql = "SELECT 1 FROM " + Quote(table.Name) + " WHERE " + Quote(schema.Entry.Name) + " = @entry LIMIT 1;";
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            return await command.ExecuteScalarAsync(cancellationToken) != null;
        }

        // Read existing row using schema-aware database operations.
        private async Task<LootRowValues?> GetExistingRowAsync(
            ConnectionProfile profile,
            TableMetadata table,
            LootSchema schema,
            LootRowValues key,
            CancellationToken cancellationToken)
        {
            string sql = "SELECT " +
                         ColumnExpr("l", schema.Entry) + ", " + ColumnExpr("l", schema.Item) + ", " +
                         ColumnExpr("l", schema.Chance) + ", " + ColumnExpr("l", schema.GroupId) + ", " +
                         ColumnExpr("l", schema.MinCountOrRef) + ", " + ColumnExpr("l", schema.MaxCount) + ", " +
                         ColumnExpr("l", schema.ConditionId) +
                         " FROM " + Quote(table.Name) + " l WHERE " + BuildPrimaryKeyWhere(table, schema, key, "@k") + " LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddPrimaryKeyParameters(command, table, schema, key, "@k");
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new LootRowValues
            {
                Entry = reader.IsDBNull(0) ? key.Entry : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                ItemId = reader.IsDBNull(1) ? key.ItemId : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                Chance = ReadNullableDouble(reader, 2),
                GroupId = ReadNullableLong(reader, 3),
                MinCountOrReference = ReadNullableLong(reader, 4),
                MaxCount = ReadNullableLong(reader, 5),
                ConditionId = ReadNullableLong(reader, 6)
            };
        }

        // Count definition rows using schema-aware database operations.
        private async Task<long> CountDefinitionRowsAsync(ConnectionProfile profile, TableMetadata table, LootSchema schema, long entry, CancellationToken cancellationToken)
        {
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            string sql = "SELECT COUNT(*) FROM " + Quote(table.Name) + " WHERE " + Quote(schema.Entry.Name) + " = @entry;";
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
        }

        // Build primary key where using schema-aware database operations.
        private static string BuildPrimaryKeyWhere(TableMetadata table, LootSchema schema, LootRowValues values, string parameterPrefix)
        {
            List<string> predicates = new();
            for (int i = 0; i < table.PrimaryKeyColumns.Count; i++)
            {
                ColumnMetadata column = table.PrimaryKeyColumns[i];
                if (!TryGetColumnValue(schema, values, column.Name, out _))
                {
                    throw new InvalidOperationException("Loot Viewer cannot safely identify primary-key column " + column.Name + ".");
                }

                predicates.Add(Quote(column.Name) + " = " + parameterPrefix + i.ToString(CultureInfo.InvariantCulture));
            }

            if (predicates.Count == 0)
            {
                throw new InvalidOperationException("Loot table does not expose a primary key.");
            }

            return string.Join(" AND ", predicates);
        }

        // Add primary key parameters using schema-aware database operations.
        private static void AddPrimaryKeyParameters(MySqlCommand command, TableMetadata table, LootSchema schema, LootRowValues values, string parameterPrefix)
        {
            for (int i = 0; i < table.PrimaryKeyColumns.Count; i++)
            {
                ColumnMetadata column = table.PrimaryKeyColumns[i];
                if (!TryGetColumnValue(schema, values, column.Name, out object? value))
                {
                    throw new InvalidOperationException("Loot Viewer cannot safely identify primary-key column " + column.Name + ".");
                }

                command.Parameters.AddWithValue(parameterPrefix + i.ToString(CultureInfo.InvariantCulture), value ?? DBNull.Value);
            }
        }

        // Add value parameters using schema-aware database operations.
        private static void AddValueParameters(MySqlCommand command, List<ColumnMetadata> columns, LootSchema schema, LootRowValues values, string parameterPrefix)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (!TryGetColumnValue(schema, values, columns[i].Name, out object? value))
                {
                    continue;
                }

                command.Parameters.AddWithValue(parameterPrefix + i.ToString(CultureInfo.InvariantCulture), value ?? DBNull.Value);
            }
        }

        // Attempt to get column value using schema-aware database operations.
        private static bool TryGetColumnValue(LootSchema schema, LootRowValues values, string columnName, out object? value)
        {
            if (string.Equals(schema.Entry.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.Entry;
                return true;
            }

            if (schema.Item != null && string.Equals(schema.Item.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.ItemId;
                return true;
            }

            if (schema.Chance != null && string.Equals(schema.Chance.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.Chance ?? 100d;
                return true;
            }

            if (schema.GroupId != null && string.Equals(schema.GroupId.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.GroupId ?? 0L;
                return true;
            }

            if (schema.MinCountOrRef != null && string.Equals(schema.MinCountOrRef.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.MinCountOrReference ?? 1L;
                return true;
            }

            if (schema.MaxCount != null && string.Equals(schema.MaxCount.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.MaxCount ?? 1L;
                return true;
            }

            if (schema.ConditionId != null && string.Equals(schema.ConditionId.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = values.ConditionId ?? 0L;
                return true;
            }

            value = null;
            return false;
        }

        // Build insert preview using schema-aware database operations.
        private static string BuildInsertPreview(TableMetadata table, LootSchema schema, LootRowValues values)
        {
            List<ColumnMetadata> columns = GetWritableColumns(schema);
            return "INSERT INTO " + Quote(table.Name) + " (" + string.Join(", ", columns.Select(column => Quote(column.Name))) + ") VALUES (" +
                   string.Join(", ", columns.Select(column => { TryGetColumnValue(schema, values, column.Name, out object? value); return SqlLiteral(value); })) + ");";
        }

        // Build delete preview using schema-aware database operations.
        private static string BuildDeletePreview(TableMetadata table, LootSchema schema, LootRowValues values)
        {
            List<string> predicates = new();
            foreach (ColumnMetadata column in table.PrimaryKeyColumns)
            {
                TryGetColumnValue(schema, values, column.Name, out object? value);
                predicates.Add(Quote(column.Name) + " = " + SqlLiteral(value));
            }

            return "DELETE FROM " + Quote(table.Name) + " WHERE " + string.Join(" AND ", predicates) + " LIMIT 1;";
        }

        // Build update preview using schema-aware database operations.
        private static string BuildUpdatePreview(TableMetadata table, LootSchema schema, LootRowValues original, LootRowValues replacement)
        {
            List<ColumnMetadata> setters = GetWritableColumns(schema).Where(column => !string.Equals(column.Name, schema.Entry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            string assignments = string.Join(", ", setters.Select(column =>
            {
                TryGetColumnValue(schema, replacement, column.Name, out object? value);
                return Quote(column.Name) + " = " + SqlLiteral(value);
            }));

            List<string> predicates = new();
            foreach (ColumnMetadata column in table.PrimaryKeyColumns)
            {
                TryGetColumnValue(schema, original, column.Name, out object? value);
                predicates.Add(Quote(column.Name) + " = " + SqlLiteral(value));
            }

            return "UPDATE " + Quote(table.Name) + " SET " + assignments + " WHERE " + string.Join(" AND ", predicates) + " LIMIT 1;";
        }

        // Format a value as a SQL literal for a generated SQL preview.
        private static string SqlLiteral(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "NULL";
            }

            return value switch
            {
                double d => d.ToString("0.################", CultureInfo.InvariantCulture),
                float f => f.ToString("0.################", CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
                _ => "'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''") + "'"
            };
        }


        // Build template summary branch using schema-aware database operations.
        private static string BuildTemplateSummaryBranch(TableMetadata table, LootSchema schema)
        {
            string referenceCount = schema.MinCountOrRef == null
                ? "0"
                : "SUM(CASE WHEN " + Quote(schema.MinCountOrRef.Name) + " < 0 THEN 1 ELSE 0 END)";
            string directCount = schema.MinCountOrRef == null
                ? "COUNT(*)"
                : "SUM(CASE WHEN " + Quote(schema.MinCountOrRef.Name) + " >= 0 THEN 1 ELSE 0 END)";
            return "SELECT '" + table.Name.Replace("'", "''") + "' AS table_name, CAST(" + Quote(schema.Entry.Name) + " AS SIGNED) AS entry, COUNT(*) AS row_count, " +
                   directCount + " AS direct_item_count, " + referenceCount + " AS reference_count FROM " + Quote(table.Name) + " GROUP BY " + Quote(schema.Entry.Name);
        }

        // Handle column expr within the loot viewer service workflow.
        private static string ColumnExpr(string alias, ColumnMetadata? column) => column == null ? "NULL" : alias + "." + Quote(column.Name);
        // Read an optional integer column without losing SQL NULL.
        private static long? ReadNullableLong(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        // Read an optional floating-point column without losing SQL NULL.
        private static double? ReadNullableDouble(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        // Quote a database identifier through the shared quoting helper.
        private static string Quote(string value) => MySqlIdentifier.Quote(value);

        // Read table label using schema-aware database operations.
        private static string GetTableLabel(string tableName)
        {
            string name = tableName.EndsWith("_loot_template", StringComparison.OrdinalIgnoreCase)
                ? tableName[..^"_loot_template".Length]
                : tableName;
            return name.ToLowerInvariant() switch
            {
                "creature" => "Creature loot",
                "gameobject" => "GameObject loot",
                "pickpocketing" => "Pickpocket loot",
                "skinning" => "Skinning loot",
                "disenchant" => "Disenchant loot",
                "fishing" => "Fishing loot",
                "item" => "Item-contained loot",
                "mail" => "Mail loot",
                "reference" => "Reference loot",
                "prospecting" => "Prospecting loot",
                "milling" => "Milling loot",
                "spell" => "Spell loot",
                _ => string.Join(" ", name.Split('_').Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + " loot"
            };
        }

        // Find an optional schema column among its known alternative names.
        private static ColumnMetadata? Optional(TableMetadata table, params string[] names) =>
            table.Columns.FirstOrDefault(column => names.Any(name => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)));

        private sealed class LootSchema
        {
            public ColumnMetadata Entry { get; private set; } = null!;
            public ColumnMetadata? Item
            {
                get; private set;
            }

            public ColumnMetadata? Chance
            {
                get; private set;
            }

            public ColumnMetadata? GroupId
            {
                get; private set;
            }

            public ColumnMetadata? MinCountOrRef
            {
                get; private set;
            }

            public ColumnMetadata? MaxCount
            {
                get; private set;
            }

            public ColumnMetadata? ConditionId
            {
                get; private set;
            }

            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static bool TryCreate(TableMetadata table, out LootSchema? schema)
            {
                ColumnMetadata? entry = Optional(table, "entry", "Entry");
                if (entry == null)
                {
                    schema = null;
                    return false;
                }

                schema = new LootSchema
                {
                    Entry = entry,
                    Item = Optional(table, "item", "Item"),
                    Chance = Optional(table, "ChanceOrQuestChance", "chance", "Chance"),
                    GroupId = Optional(table, "groupid", "groupId", "group_id"),
                    MinCountOrRef = Optional(table, "mincountOrRef", "mincountorref", "mincount_or_ref", "mincount"),
                    MaxCount = Optional(table, "maxcount", "maxCount", "max_count"),
                    ConditionId = Optional(table, "condition_id", "conditionId", "ConditionId")
                };

                return true;
            }
        }
    }
}
