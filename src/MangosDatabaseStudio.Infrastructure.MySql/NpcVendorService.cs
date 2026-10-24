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
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class NpcVendorService : INpcVendorService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the npc vendor service dependencies and state.
        public NpcVendorService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Search for  using schema-aware database operations.
        public async Task<PagedResult<NpcVendorSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcVendor,
            ulong vendorFlagMask,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            VendorSchema schema = VendorSchema.Create(creatureTemplate, npcVendor, null);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;

            string creatureTable = Qualify(creatureTemplate);
            string vendorTable = Qualify(npcVendor);
            string entry = Column("ct", schema.CreatureEntry);
            string name = Column("ct", schema.CreatureName);
            string subName = schema.CreatureSubName == null
                ? "''"
                : "COALESCE(" + Column("ct", schema.CreatureSubName) + ", '')";
            string flags = Column("ct", schema.CreatureNpcFlags);
            string vendorEntry = Column("nv", schema.VendorEntry);

            string searchClause = string.IsNullOrEmpty(normalizedSearch)
                ? string.Empty
                : " AND (" + name + " LIKE @search OR CAST(" + entry + " AS CHAR) = @searchExact)";
            string where = " WHERE (CAST(" + flags + " AS UNSIGNED) & @vendorMask) <> 0" + searchClause;

            string countSql = "SELECT COUNT(*) FROM " + creatureTable + " AS ct" + where + ";";
            string dataSql =
                "SELECT " + entry + " AS VendorEntry, " +
                name + " AS VendorName, " +
                subName + " AS VendorSubName, " +
                flags + " AS VendorNpcFlags, " +
                "(SELECT COUNT(*) FROM " + vendorTable + " AS nv WHERE " + vendorEntry + " = " + entry + ") AS VendorItemCount " +
                "FROM " + creatureTable + " AS ct" + where +
                " ORDER BY " + name + " ASC, " + entry + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                AddSearchParameters(command, vendorFlagMask, normalizedSearch);
                object? value = await command.ExecuteScalarAsync(cancellationToken);
                total = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            List<NpcVendorSummary> vendors = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                AddSearchParameters(command, vendorFlagMask, normalizedSearch);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    vendors.Add(ReadVendor(reader));
                }
            }

            return new PagedResult<NpcVendorSummary>
            {
                Items = vendors,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Search for items using schema-aware database operations.
        public async Task<PagedResult<NpcVendorItemOption>> SearchItemsAsync(
            ConnectionProfile profile,
            TableMetadata itemTemplate,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ColumnMetadata itemEntry = Required(itemTemplate, "entry");
            ColumnMetadata itemName = Required(itemTemplate, "name");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 25, 200);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;

            string itemTable = Qualify(itemTemplate);
            string entry = MySqlIdentifier.Quote(itemEntry.Name);
            string name = MySqlIdentifier.Quote(itemName.Name);
            string where = string.IsNullOrEmpty(normalizedSearch)
                ? string.Empty
                : " WHERE " + name + " LIKE @search OR CAST(" + entry + " AS CHAR) LIKE @search";

            string countSql = "SELECT COUNT(*) FROM " + itemTable + where + ";";
            string dataSql =
                "SELECT " + entry + " AS ItemEntry, COALESCE(" + name + ", '') AS ItemName " +
                "FROM " + itemTable + where +
                " ORDER BY " + name + " ASC, " + entry + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, itemTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    command.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
                }

                object? value = await command.ExecuteScalarAsync(cancellationToken);
                total = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            List<NpcVendorItemOption> items = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    command.Parameters.AddWithValue("@search", "%" + normalizedSearch + "%");
                }

                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    long itemId = GetInt64(reader, "ItemEntry");
                    string itemNameValue = GetString(reader, "ItemName");
                    items.Add(new NpcVendorItemOption
                    {
                        ItemId = itemId,
                        ItemName = string.IsNullOrWhiteSpace(itemNameValue) ? "Unnamed item" : itemNameValue
                    });
                }
            }

            return new PagedResult<NpcVendorItemOption>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Read  using schema-aware database operations.
        public async Task<NpcVendorDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            ulong vendorFlagMask,
            long entry,
            int itemPage,
            int itemPageSize,
            CancellationToken cancellationToken)
        {
            VendorSchema schema = VendorSchema.Create(creatureTemplate, npcVendor, itemTemplate);
            itemPage = Math.Max(1, itemPage);
            itemPageSize = Math.Clamp(itemPageSize, 10, _options.MaximumBrowsePageSize);
            string creatureTable = Qualify(creatureTemplate);
            string vendorTable = Qualify(npcVendor);
            string itemTable = Qualify(itemTemplate);

            string creatureEntry = Column("ct", schema.CreatureEntry);
            string creatureName = Column("ct", schema.CreatureName);
            string creatureSubName = schema.CreatureSubName == null
                ? "''"
                : "COALESCE(" + Column("ct", schema.CreatureSubName) + ", '')";
            string creatureFlags = Column("ct", schema.CreatureNpcFlags);
            string vendorEntry = Column("nv", schema.VendorEntry);
            string vendorItem = Column("nv", schema.VendorItem);
            string itemEntry = Column("it", schema.ItemEntry!);
            string itemName = Column("it", schema.ItemName!);

            string vendorSql =
                "SELECT " + creatureEntry + " AS VendorEntry, " +
                creatureName + " AS VendorName, " +
                creatureSubName + " AS VendorSubName, " +
                creatureFlags + " AS VendorNpcFlags, " +
                "(SELECT COUNT(*) FROM " + vendorTable + " AS nv WHERE " + vendorEntry + " = " + creatureEntry + ") AS VendorItemCount " +
                "FROM " + creatureTable + " AS ct " +
                "WHERE " + creatureEntry + " = @entry AND (CAST(" + creatureFlags + " AS UNSIGNED) & @vendorMask) <> 0 LIMIT 1;";

            List<string> itemSelections = new()
            {
                vendorItem + " AS VendorItemId",
                "COALESCE(" + itemName + ", '') AS VendorItemName"
            };
            AddOptionalSelection(itemSelections, "nv", schema.VendorSlot, "VendorSlot");
            AddOptionalSelection(itemSelections, "nv", schema.VendorMaxCount, "VendorMaxCount");
            AddOptionalSelection(itemSelections, "nv", schema.VendorIncrementTime, "VendorIncrementTime");
            AddOptionalSelection(itemSelections, "nv", schema.VendorExtendedCost, "VendorExtendedCost");
            AddOptionalSelection(itemSelections, "nv", schema.VendorConditionId, "VendorConditionId");

            string orderColumn = schema.VendorSlot == null ? vendorItem : Column("nv", schema.VendorSlot);
            string itemsSql =
                "SELECT " + string.Join(", ", itemSelections) + " FROM " + vendorTable + " AS nv " +
                "LEFT JOIN " + itemTable + " AS it ON " + itemEntry + " = " + vendorItem + " " +
                "WHERE " + vendorEntry + " = @entry ORDER BY " + orderColumn + " ASC, " + vendorItem +
                " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            NpcVendorSummary? vendor;
            await using (MySqlCommand command = new(vendorSql, connection))
            {
                command.Parameters.AddWithValue("@entry", entry);
                command.Parameters.AddWithValue("@vendorMask", vendorFlagMask);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                vendor = await reader.ReadAsync(cancellationToken) ? ReadVendor(reader) : null;
            }

            if (vendor == null)
            {
                return null;
            }

            int totalItemPages = itemPageSize <= 0
                ? 0
                : (int)Math.Ceiling(vendor.ItemCount / (double)itemPageSize);
            if (totalItemPages == 0)
            {
                itemPage = 1;
            }
            else if (itemPage > totalItemPages)
            {
                itemPage = totalItemPages;
            }

            int itemOffset = checked((itemPage - 1) * itemPageSize);
            List<NpcVendorItem> items = new();
            await using (MySqlCommand command = new(itemsSql, connection))
            {
                command.Parameters.AddWithValue("@entry", entry);
                command.Parameters.AddWithValue("@limit", itemPageSize);
                command.Parameters.AddWithValue("@offset", itemOffset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    long itemId = GetInt64(reader, "VendorItemId");
                    string itemNameValue = GetString(reader, "VendorItemName");
                    items.Add(new NpcVendorItem
                    {
                        ItemId = itemId,
                        ItemName = string.IsNullOrWhiteSpace(itemNameValue) ? "Unknown item" : itemNameValue,
                        Slot = GetNullableInt64(reader, "VendorSlot"),
                        MaxCount = GetNullableInt64(reader, "VendorMaxCount"),
                        IncrementTime = GetNullableInt64(reader, "VendorIncrementTime"),
                        ExtendedCost = GetNullableInt64(reader, "VendorExtendedCost"),
                        ConditionId = GetNullableInt64(reader, "VendorConditionId")
                    });
                }
            }

            return new NpcVendorDetail
            {
                Vendor = vendor,
                Items = new PagedResult<NpcVendorItem>
                {
                    Items = items,
                    TotalCount = vendor.ItemCount,
                    Page = itemPage,
                    PageSize = itemPageSize
                }
            };
        }

        // Report which editor operations the detected database schema supports.
        public NpcVendorEditorCapabilities GetEditorCapabilities(TableMetadata npcVendor)
        {
            VendorEditSchema schema = VendorEditSchema.Create(npcVendor);
            IReadOnlyList<string> keyColumns;
            try
            {
                keyColumns = ResolveKeyColumns(npcVendor, schema).Select(column => column.Name).ToArray();
            }
            catch (InvalidOperationException)
            {
                // Keep the viewer available for unfamiliar future schemas. The live mutation
                // path will still reject an unsupported key safely instead of issuing a broad update.
                keyColumns = Array.Empty<string>();
            }

            return new NpcVendorEditorCapabilities
            {
                EntryColumn = schema.VendorEntry.Name,
                ItemColumn = schema.VendorItem.Name,
                SlotColumn = schema.VendorSlot?.Name,
                MaxCountColumn = schema.VendorMaxCount?.Name,
                IncrementTimeColumn = schema.VendorIncrementTime?.Name,
                ExtendedCostColumn = schema.VendorExtendedCost?.Name,
                ConditionIdColumn = schema.VendorConditionId?.Name,
                UpdateColumns = schema.GetUpdateColumns().Select(column => column.Name).ToArray(),
                KeyColumns = keyColumns
            };
        }

        // Add items using schema-aware database operations.
        public async Task<NpcVendorMutationResult> AddItemsAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            long entry,
            IReadOnlyList<NpcVendorItemValues> items,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC vendor entry is required.");
            }

            if (items == null || items.Count == 0)
            {
                throw new InvalidOperationException("Add at least one vendor item before saving.");
            }

            if (items.Count > 250)
            {
                throw new InvalidOperationException("A maximum of 250 vendor items can be added in one operation.");
            }

            foreach (NpcVendorItemValues item in items)
            {
                ValidateItemValues(item);
            }

            VendorEditSchema schema = VendorEditSchema.Create(npcVendor);
            ColumnMetadata itemEntryColumn = Required(itemTemplate, "entry");
            IReadOnlyList<ColumnMetadata> writableColumns = schema.GetInsertColumns();
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcVendor, schema);
            string insertSql = BuildParameterizedInsert(npcVendor, writableColumns);
            string preview = BuildInsertPreview(npcVendor, schema, entry, items);
            string revertPreview = BuildDeleteRevertPreview(npcVendor, schema, entry, items, keyColumns, writableColumns);

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcVendor.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await EnsureItemsExistAsync(connection, itemTemplate, itemEntryColumn, items.Select(item => item.ItemId), cancellationToken);

            await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            int affected = 0;
            try
            {
                foreach (NpcVendorItemValues item in items)
                {
                    await using MySqlCommand command = new(insertSql, connection, transaction);
                    AddMutationParameters(command, schema, writableColumns, entry, item, string.Empty);
                    affected += await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return new NpcVendorMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Update item using schema-aware database operations.
        public async Task<NpcVendorMutationResult> UpdateItemAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            long entry,
            NpcVendorItemValues original,
            NpcVendorItemValues replacement,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC vendor entry is required.");
            }

            ValidateItemValues(original);
            ValidateItemValues(replacement);

            VendorEditSchema schema = VendorEditSchema.Create(npcVendor);
            ColumnMetadata itemEntryColumn = Required(itemTemplate, "entry");
            IReadOnlyList<ColumnMetadata> updateColumns = schema.GetUpdateColumns();
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcVendor, schema);
            string qualified = Qualify(npcVendor);
            string setClause = string.Join(", ", updateColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " = @new_" + ParameterName(column.Name)));
            string whereClause = string.Join(" AND ", keyColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " <=> @old_" + ParameterName(column.Name)));
            string sql = "UPDATE " + qualified + " SET " + setClause + " WHERE " + whereClause + ";";
            string preview = BuildUpdatePreview(npcVendor, schema, entry, original, replacement, updateColumns, keyColumns);
            string revertPreview = BuildUpdateRevertPreview(npcVendor, schema, entry, original, replacement, updateColumns, keyColumns);

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcVendor.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await EnsureItemsExistAsync(connection, itemTemplate, itemEntryColumn, new[] { replacement.ItemId }, cancellationToken);

            await using MySqlCommand command = new(sql, connection);
            foreach (ColumnMetadata column in updateColumns)
            {
                command.Parameters.AddWithValue("@new_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, replacement));
            }

            foreach (ColumnMetadata column in keyColumns)
            {
                command.Parameters.AddWithValue("@old_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, original));
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The vendor item could not be updated because the original row was not found. Refresh the vendor and try again.");
            }

            return new NpcVendorMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Delete item using schema-aware database operations.
        public async Task<NpcVendorMutationResult> DeleteItemAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            long entry,
            NpcVendorItemValues original,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC vendor entry is required.");
            }

            ValidateItemValues(original);
            VendorEditSchema schema = VendorEditSchema.Create(npcVendor);
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcVendor, schema);
            string whereClause = string.Join(" AND ", keyColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " <=> @old_" + ParameterName(column.Name)));
            string sql = "DELETE FROM " + Qualify(npcVendor) + " WHERE " + whereClause + ";";
            string preview = "DELETE FROM " + MySqlIdentifier.Quote(npcVendor.Name) + " WHERE " +
                string.Join(" AND ", keyColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " <=> " + SqlLiteral(ResolveColumnValue(schema, column, entry, original)))) + ";";
            string revertPreview = BuildInsertPreview(npcVendor, schema, entry, new[] { original });

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcVendor.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            foreach (ColumnMetadata column in keyColumns)
            {
                command.Parameters.AddWithValue("@old_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, original));
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The vendor item could not be removed because the row was not found. Refresh the vendor and try again.");
            }

            return new NpcVendorMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Validate item values using schema-aware database operations.
        private static void ValidateItemValues(NpcVendorItemValues item)
        {
            if (item == null || item.ItemId <= 0)
            {
                throw new InvalidOperationException("Every vendor row must contain a valid item entry.");
            }

            ValidateNonNegative(item.Slot, "Slot");
            ValidateNonNegative(item.MaxCount, "Max stock");
            ValidateNonNegative(item.IncrementTime, "Restock time");
            ValidateNonNegative(item.ExtendedCost, "Extended cost");
            ValidateNonNegative(item.ConditionId, "Condition");
        }

        // Validate non negative using schema-aware database operations.
        private static void ValidateNonNegative(long? value, string label)
        {
            if (value < 0)
            {
                throw new InvalidOperationException(label + " cannot be negative.");
            }
        }

        // Ensure items exist using schema-aware database operations.
        private static async Task EnsureItemsExistAsync(
            MySqlConnection connection,
            TableMetadata itemTemplate,
            ColumnMetadata itemEntryColumn,
            IEnumerable<long> itemIds,
            CancellationToken cancellationToken)
        {
            long[] ids = itemIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return;
            }

            List<string> parameters = new();
            await using MySqlCommand command = connection.CreateCommand();
            for (int index = 0; index < ids.Length; index++)
            {
                string parameter = "@item" + index.ToString(CultureInfo.InvariantCulture);
                parameters.Add(parameter);
                command.Parameters.AddWithValue(parameter, ids[index]);
            }

            command.CommandText = "SELECT " + MySqlIdentifier.Quote(itemEntryColumn.Name) + " FROM " + Qualify(itemTemplate) +
                " WHERE " + MySqlIdentifier.Quote(itemEntryColumn.Name) + " IN (" + string.Join(", ", parameters) + ");";

            HashSet<long> found = new();
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                found.Add(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
            }

            long[] missing = ids.Where(id => !found.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException("Item template " + string.Join(", ", missing.Select(id => id.ToString(CultureInfo.InvariantCulture))) + " was not found.");
            }
        }

        // Build parameterized insert using schema-aware database operations.
        private static string BuildParameterizedInsert(TableMetadata npcVendor, IReadOnlyList<ColumnMetadata> columns)
        {
            return "INSERT INTO " + Qualify(npcVendor) + " (" +
                string.Join(", ", columns.Select(column => MySqlIdentifier.Quote(column.Name))) + ") VALUES (" +
                string.Join(", ", columns.Select(column => "@" + ParameterName(column.Name))) + ");";
        }

        // Add mutation parameters using schema-aware database operations.
        private static void AddMutationParameters(
            MySqlCommand command,
            VendorEditSchema schema,
            IReadOnlyList<ColumnMetadata> columns,
            long entry,
            NpcVendorItemValues item,
            string prefix)
        {
            foreach (ColumnMetadata column in columns)
            {
                command.Parameters.AddWithValue("@" + prefix + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, item));
            }
        }

        // Resolve column value using schema-aware database operations.
        private static object ResolveColumnValue(VendorEditSchema schema, ColumnMetadata column, long entry, NpcVendorItemValues item)
        {
            if (SameColumn(column, schema.VendorEntry))
            {
                return entry;
            }

            if (SameColumn(column, schema.VendorItem))
            {
                return item.ItemId;
            }

            if (SameColumn(column, schema.VendorSlot))
            {
                return NormalizeOptional(column, item.Slot);
            }

            if (SameColumn(column, schema.VendorMaxCount))
            {
                return NormalizeOptional(column, item.MaxCount);
            }

            if (SameColumn(column, schema.VendorIncrementTime))
            {
                return NormalizeOptional(column, item.IncrementTime);
            }

            if (SameColumn(column, schema.VendorExtendedCost))
            {
                return NormalizeOptional(column, item.ExtendedCost);
            }

            if (SameColumn(column, schema.VendorConditionId))
            {
                return NormalizeOptional(column, item.ConditionId);
            }

            throw new InvalidOperationException("Unsupported npc_vendor key column '" + column.Name + "'.");
        }

        // Normalize optional using schema-aware database operations.
        private static object NormalizeOptional(ColumnMetadata column, long? value)
        {
            if (value.HasValue)
            {
                return value.Value;
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultValue) && long.TryParse(column.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }

            return column.IsNullable ? DBNull.Value : 0L;
        }

        // Resolve key columns using schema-aware database operations.
        private static IReadOnlyList<ColumnMetadata> ResolveKeyColumns(TableMetadata npcVendor, VendorEditSchema schema)
        {
            IReadOnlyList<ColumnMetadata> primary = npcVendor.PrimaryKeyColumns;
            if (primary.Count > 0)
            {
                foreach (ColumnMetadata column in primary)
                {
                    // Resolve once so an unfamiliar future schema is rejected safely rather than deleting broadly.
                    ResolveColumnValue(schema, column, 1L, new NpcVendorItemValues { ItemId = 1L });
                }

                return primary;
            }

            List<ColumnMetadata> fallback = new() { schema.VendorEntry, schema.VendorItem };
            if (schema.VendorExtendedCost != null)
            {
                fallback.Add(schema.VendorExtendedCost);
            }

            return fallback;
        }

        // Build insert preview using schema-aware database operations.
        private static string BuildInsertPreview(
            TableMetadata npcVendor,
            VendorEditSchema schema,
            long entry,
            IReadOnlyList<NpcVendorItemValues> items)
        {
            IReadOnlyList<ColumnMetadata> columns = schema.GetInsertColumns();
            List<string> rows = new();
            foreach (NpcVendorItemValues item in items)
            {
                rows.Add("(" + string.Join(", ", columns.Select(column => SqlLiteral(ResolveColumnValue(schema, column, entry, item)))) + ")");
            }

            return "INSERT INTO " + MySqlIdentifier.Quote(npcVendor.Name) + " (" +
                string.Join(", ", columns.Select(column => MySqlIdentifier.Quote(column.Name))) + ") VALUES\n    " +
                string.Join(",\n    ", rows) + ";";
        }

        // Build update preview using schema-aware database operations.
        private static string BuildUpdatePreview(
            TableMetadata npcVendor,
            VendorEditSchema schema,
            long entry,
            NpcVendorItemValues original,
            NpcVendorItemValues replacement,
            IReadOnlyList<ColumnMetadata> updateColumns,
            IReadOnlyList<ColumnMetadata> keyColumns)
        {
            return "UPDATE " + MySqlIdentifier.Quote(npcVendor.Name) + " SET\n    " +
                string.Join(",\n    ", updateColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " = " + SqlLiteral(ResolveColumnValue(schema, column, entry, replacement)))) +
                "\nWHERE " + string.Join(" AND ", keyColumns.Select(column => MySqlIdentifier.Quote(column.Name) + " <=> " + SqlLiteral(ResolveColumnValue(schema, column, entry, original)))) + ";";
        }

        // Build delete revert preview using schema-aware database operations.
        private static string BuildDeleteRevertPreview(
            TableMetadata npcVendor,
            VendorEditSchema schema,
            long entry,
            IReadOnlyList<NpcVendorItemValues> items,
            IReadOnlyList<ColumnMetadata> keyColumns,
            IReadOnlyList<ColumnMetadata> insertedColumns)
        {
            IReadOnlyList<ColumnMetadata> guardColumns = keyColumns
                .Concat(insertedColumns)
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            List<string> rows = items.Select(item =>
                "(" + string.Join(" AND ", guardColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " <=> " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, item)))) + ")").ToList();

            return "DELETE FROM " + MySqlIdentifier.Quote(npcVendor.Name) +
                   " WHERE " + string.Join(" OR ", rows) +
                   " LIMIT " + items.Count.ToString(CultureInfo.InvariantCulture) + ";";
        }

        // Build update revert preview using schema-aware database operations.
        private static string BuildUpdateRevertPreview(
            TableMetadata npcVendor,
            VendorEditSchema schema,
            long entry,
            NpcVendorItemValues original,
            NpcVendorItemValues replacement,
            IReadOnlyList<ColumnMetadata> updateColumns,
            IReadOnlyList<ColumnMetadata> keyColumns)
        {
            IReadOnlyList<ColumnMetadata> guardColumns = keyColumns
                .Concat(updateColumns)
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            return "UPDATE " + MySqlIdentifier.Quote(npcVendor.Name) + " SET\n    " +
                string.Join(",\n    ", updateColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " = " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, original)))) +
                "\nWHERE " + string.Join(" AND ", guardColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " <=> " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, replacement)))) + ";";
        }

        // Format a value as a SQL literal for a generated SQL preview.
        private static string SqlLiteral(object value)
        {
            if (value == DBNull.Value || value == null)
            {
                return "NULL";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        }

        // Normalize a column name into a safe query parameter identifier.
        private static string ParameterName(string columnName)
        {
            return new string(columnName.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        }

        // Test whether the supplied schema metadata describes the same column.
        private static bool SameColumn(ColumnMetadata column, ColumnMetadata? other)
        {
            return other != null && string.Equals(column.Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Handle required within the npc vendor service workflow.
        private static ColumnMetadata Required(TableMetadata table, params string[] candidates)
        {
            return table.Columns.FirstOrDefault(column => candidates.Any(candidate => string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException("Table '" + table.Name + "' does not expose the required column " + string.Join("/", candidates) + ".");
        }

        private sealed class VendorEditSchema
        {
            public ColumnMetadata VendorEntry { get; private set; } = null!;
            public ColumnMetadata VendorItem { get; private set; } = null!;
            public ColumnMetadata? VendorSlot
            {
                get; private set;
            }

            public ColumnMetadata? VendorMaxCount
            {
                get; private set;
            }

            public ColumnMetadata? VendorIncrementTime
            {
                get; private set;
            }

            public ColumnMetadata? VendorExtendedCost
            {
                get; private set;
            }

            public ColumnMetadata? VendorConditionId
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static VendorEditSchema Create(TableMetadata npcVendor)
            {
                return new VendorEditSchema
                {
                    VendorEntry = Required(npcVendor, "entry"),
                    VendorItem = Required(npcVendor, "item"),
                    VendorSlot = Find(npcVendor, "slot"),
                    VendorMaxCount = Find(npcVendor, "maxcount", "max_count"),
                    VendorIncrementTime = Find(npcVendor, "incrtime", "incrementtime", "incr_time"),
                    VendorExtendedCost = Find(npcVendor, "extendedcost", "extended_cost"),
                    VendorConditionId = Find(npcVendor, "condition_id", "conditionid")
                };
            }

            // Read insert columns using schema-aware database operations.
            public IReadOnlyList<ColumnMetadata> GetInsertColumns()
            {
                List<ColumnMetadata> columns = new() { VendorEntry, VendorItem };
                Add(columns, VendorSlot);
                Add(columns, VendorMaxCount);
                Add(columns, VendorIncrementTime);
                Add(columns, VendorExtendedCost);
                Add(columns, VendorConditionId);
                return columns;
            }

            // Read update columns using schema-aware database operations.
            public IReadOnlyList<ColumnMetadata> GetUpdateColumns()
            {
                List<ColumnMetadata> columns = new() { VendorItem };
                Add(columns, VendorSlot);
                Add(columns, VendorMaxCount);
                Add(columns, VendorIncrementTime);
                Add(columns, VendorExtendedCost);
                Add(columns, VendorConditionId);
                return columns;
            }

            // Handle add within the npc vendor service workflow.
            private static void Add(List<ColumnMetadata> columns, ColumnMetadata? column)
            {
                if (column != null)
                {
                    columns.Add(column);
                }
            }

            // Locate a compatible column from the available schema metadata.
            private static ColumnMetadata? Find(TableMetadata table, params string[] candidates)
            {
                return table.Columns.FirstOrDefault(column => candidates.Any(candidate => string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)));
            }
        }

        // Add search parameters using schema-aware database operations.
        private static void AddSearchParameters(MySqlCommand command, ulong vendorFlagMask, string search)
        {
            command.Parameters.AddWithValue("@vendorMask", vendorFlagMask);
            if (!string.IsNullOrEmpty(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
                command.Parameters.AddWithValue("@searchExact", search);
            }
        }

        // Read vendor using schema-aware database operations.
        private static NpcVendorSummary ReadVendor(DbDataReader reader)
        {
            return new NpcVendorSummary
            {
                Entry = GetInt64(reader, "VendorEntry"),
                Name = GetString(reader, "VendorName"),
                SubName = GetString(reader, "VendorSubName"),
                NpcFlags = GetUInt64(reader, "VendorNpcFlags"),
                ItemCount = GetInt64(reader, "VendorItemCount")
            };
        }

        // Quote and combine the database and table names as a qualified SQL identifier.
        private static string Qualify(TableMetadata table)
        {
            return MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
        }

        // Build a table-alias-qualified, safely quoted column name.
        private static string Column(string alias, ColumnMetadata column)
        {
            return alias + "." + MySqlIdentifier.Quote(column.Name);
        }

        // Add optional selection using schema-aware database operations.
        private static void AddOptionalSelection(List<string> selections, string alias, ColumnMetadata? column, string resultAlias)
        {
            if (column != null)
            {
                selections.Add(Column(alias, column) + " AS " + resultAlias);
            }
        }

        // Read int64 using schema-aware database operations.
        private static long GetInt64(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0L : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read uint64 using schema-aware database operations.
        private static ulong GetUInt64(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0UL : Convert.ToUInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read string using schema-aware database operations.
        private static string GetString(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // Read nullable int64 using schema-aware database operations.
        private static long? GetNullableInt64(DbDataReader reader, string name)
        {
            int ordinal;
            try
            {
                ordinal = reader.GetOrdinal(name);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        internal sealed class VendorSchema
        {
            public ColumnMetadata CreatureEntry { get; private set; } = null!;
            public ColumnMetadata CreatureName { get; private set; } = null!;
            public ColumnMetadata? CreatureSubName
            {
                get; private set;
            }

            public ColumnMetadata CreatureNpcFlags { get; private set; } = null!;
            public ColumnMetadata VendorEntry { get; private set; } = null!;
            public ColumnMetadata VendorItem { get; private set; } = null!;
            public ColumnMetadata? VendorSlot
            {
                get; private set;
            }

            public ColumnMetadata? VendorMaxCount
            {
                get; private set;
            }

            public ColumnMetadata? VendorIncrementTime
            {
                get; private set;
            }

            public ColumnMetadata? VendorExtendedCost
            {
                get; private set;
            }

            public ColumnMetadata? VendorConditionId
            {
                get; private set;
            }

            public ColumnMetadata? ItemEntry
            {
                get; private set;
            }

            public ColumnMetadata? ItemName
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static VendorSchema Create(TableMetadata creatureTemplate, TableMetadata npcVendor, TableMetadata? itemTemplate)
            {
                VendorSchema schema = new()
                {
                    CreatureEntry = Required(creatureTemplate, "entry"),
                    CreatureName = Required(creatureTemplate, "name"),
                    CreatureSubName = Optional(creatureTemplate, "subname", "sub_name"),
                    CreatureNpcFlags = Required(creatureTemplate, "npcflag", "npcflags", "npc_flags"),
                    VendorEntry = Required(npcVendor, "entry"),
                    VendorItem = Required(npcVendor, "item"),
                    VendorSlot = Optional(npcVendor, "slot"),
                    VendorMaxCount = Optional(npcVendor, "maxcount", "max_count"),
                    VendorIncrementTime = Optional(npcVendor, "incrtime", "incrementtime", "incr_time"),
                    VendorExtendedCost = Optional(npcVendor, "extendedcost", "extended_cost"),
                    VendorConditionId = Optional(npcVendor, "condition_id", "conditionid")
                };

                if (itemTemplate != null)
                {
                    schema.ItemEntry = Required(itemTemplate, "entry");
                    schema.ItemName = Required(itemTemplate, "name");
                }

                return schema;
            }

            // Handle required within the npc vendor service workflow.
            private static ColumnMetadata Required(TableMetadata table, params string[] candidates)
            {
                return Optional(table, candidates) ?? throw new InvalidOperationException(
                    "Table '" + table.Name + "' does not expose the required column " + string.Join("/", candidates) + ".");
            }

            // Find an optional schema column among its known alternative names.
            private static ColumnMetadata? Optional(TableMetadata table, params string[] candidates)
            {
                return table.Columns.FirstOrDefault(column =>
                    candidates.Any(candidate => string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)));
            }
        }
    }
}
