// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Query and edit gossip menus, options, and source relationships.

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
    internal sealed class GossipViewerService : IGossipViewerService
    {
        private const int RelationshipPreviewLimit = 100;
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the gossip viewer service dependencies and state.
        public GossipViewerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Find menus by identifier or option text and return a page of matching definitions.
        public async Task<PagedResult<GossipMenuSummary>> SearchMenusAsync(
            ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption,
            TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, string? search,
            int page, int pageSize, CancellationToken cancellationToken)
        {
            MenuSchema? menu = MenuSchema.TryCreate(gossipMenu);
            OptionSchema? options = OptionSchema.TryCreate(gossipMenuOption);
            page = Math.Max(1, page);
            pageSize = NormalizePageSize(pageSize);
            if (menu == null && options == null)
            {
                return Empty<GossipMenuSummary>(page, pageSize);
            }

            string database = gossipMenu?.DatabaseName ?? gossipMenuOption!.DatabaseName;
            string allIds = BuildMenuIdUnion(gossipMenu, menu, gossipMenuOption, options);
            string normalized = search?.Trim() ?? string.Empty;
            string ids = string.IsNullOrWhiteSpace(normalized)
                ? allIds
                : BuildMenuSearchIdSet(allIds, gossipMenuOption, options);

            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);

            // Search and page the compact menu-ID set first. Counts are loaded in batches only
            // for the current page so the large creature/gameobject tables are not rescanned for
            // every menu candidate while MySQL evaluates the directory search.
            long total = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM " + ids + " m;", normalized, cancellationToken);
            ClampPage(ref page, pageSize, total);
            int offset = checked((page - 1) * pageSize);

            string sql = "SELECT m.menu_id FROM " + ids + " m ORDER BY m.menu_id LIMIT @limit OFFSET @offset;";
            List<GossipMenuSummary> rows = new();
            await using (MySqlCommand command = new(sql, connection))
            {
                AddSearch(command, normalized);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new GossipMenuSummary
                    {
                        Entry = ToLong(reader, 0)
                    });
                }
            }

            await PopulateMenuSummaryCountsAsync(
                connection,
                gossipMenu,
                menu,
                gossipMenuOption,
                options,
                creatureTemplate,
                gameObjectTemplate,
                rows,
                cancellationToken);

            return Page(rows, total, page, pageSize);
        }

        // Search creature and game-object sources referencing gossip menus.
        public async Task<PagedResult<GossipSourceSummary>> SearchSourcesAsync(
            ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption,
            TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, string? sourceFilter,
            string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            page = Math.Max(1, page);
            pageSize = NormalizePageSize(pageSize);
            List<string> branches = BuildSourceBranches(creatureTemplate, gameObjectTemplate, GossipSourceFilters.Normalize(sourceFilter));
            if (branches.Count == 0)
            {
                return Empty<GossipSourceSummary>(page, pageSize);
            }

            MenuSchema? menu = MenuSchema.TryCreate(gossipMenu);
            OptionSchema? options = OptionSchema.TryCreate(gossipMenuOption);
            string normalized = search?.Trim() ?? string.Empty;
            string union = "(" + string.Join(" UNION ALL ", branches) + ")";
            string where = string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : " WHERE s.entity_name LIKE @search OR CAST(s.entity_entry AS CHAR) LIKE @search OR CAST(s.menu_id AS CHAR) LIKE @search OR s.link_kind LIKE @search";
            string database = creatureTemplate?.DatabaseName ?? gameObjectTemplate!.DatabaseName;

            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            long total = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM " + union + " s" + where + ";", normalized, cancellationToken);
            ClampPage(ref page, pageSize, total);
            int offset = checked((page - 1) * pageSize);

            // Page source rows first and resolve definition counts for the distinct menu IDs on
            // that page in batches. This avoids correlated gossip_menu/gossip_menu_option lookups
            // for every source row.
            string sql = "SELECT s.entity_type, s.entity_entry, s.entity_name, s.menu_id, s.link_kind" +
                         " FROM " + union + " s" + where + " ORDER BY s.entity_type, s.entity_name, s.entity_entry LIMIT @limit OFFSET @offset;";

            List<GossipSourceSummary> rows = new();
            MySqlCommand mySqlCommand = new(sql, connection);
            await using (MySqlCommand command = mySqlCommand)
            {
                AddSearch(command, normalized);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new GossipSourceSummary
                    {
                        EntityType = reader.GetString(0),
                        EntityEntry = ToLong(reader, 1),
                        EntityName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        MenuId = ToLong(reader, 3),
                        LinkKind = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                    });
                }
            }

            await PopulateSourceDefinitionCountsAsync(connection, gossipMenu, menu, gossipMenuOption, options, rows, cancellationToken);
            return Page(rows, total, page, pageSize);
        }

        // Search for eligible creatures using schema-aware database operations.
        public async Task<PagedResult<GossipCreatureOption>> SearchEligibleCreaturesAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            ulong gossipFlagMask,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            CreatureSourceSchema? schema = CreatureSourceSchema.TryCreate(creatureTemplate);
            page = Math.Max(1, page);
            pageSize = NormalizePageSize(pageSize);
            if (schema?.NpcFlags == null)
            {
                return Empty<GossipCreatureOption>(page, pageSize);
            }

            string normalized = search?.Trim() ?? string.Empty;
            string nameExpr = schema.Name == null ? "''" : "COALESCE(c." + Q(schema.Name.Name) + ", '')";
            string baseWhere = "(CAST(COALESCE(c." + Q(schema.NpcFlags.Name) + ", 0) AS UNSIGNED) & @gossipFlag) = 0";
            string searchWhere = string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : " AND (" + nameExpr + " LIKE @search OR CAST(c." + Q(schema.Entry.Name) + " AS CHAR) LIKE @search)";

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            string countSql = "SELECT COUNT(*) FROM " + Q(creatureTemplate.Name) + " c WHERE " + baseWhere + searchWhere + ";";
            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                command.Parameters.AddWithValue("@gossipFlag", gossipFlagMask);
                AddSearch(command, normalized);
                total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            ClampPage(ref page, pageSize, total);
            int offset = checked((page - 1) * pageSize);
            string sql =
                "SELECT CAST(c." + Q(schema.Entry.Name) + " AS SIGNED), " + nameExpr + ", " +
                "CAST(COALESCE(c." + Q(schema.MenuId.Name) + ", 0) AS SIGNED), " +
                "CAST(COALESCE(c." + Q(schema.NpcFlags.Name) + ", 0) AS UNSIGNED) " +
                "FROM " + Q(creatureTemplate.Name) + " c WHERE " + baseWhere + searchWhere +
                " ORDER BY " + nameExpr + ", c." + Q(schema.Entry.Name) + " LIMIT @limit OFFSET @offset;";

            List<GossipCreatureOption> rows = new();
            await using (MySqlCommand command = new(sql, connection))
            {
                command.Parameters.AddWithValue("@gossipFlag", gossipFlagMask);
                AddSearch(command, normalized);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new GossipCreatureOption
                    {
                        Entry = ToLong(reader, 0),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        GossipMenuId = ToLong(reader, 2),
                        NpcFlags = reader.IsDBNull(3) ? 0UL : Convert.ToUInt64(reader.GetValue(3), CultureInfo.InvariantCulture)
                    });
                }
            }

            return Page(rows, total, page, pageSize);
        }

        // Load the selected menu with its associated texts, options, and sources.
        public async Task<GossipMenuDetail?> GetMenuAsync(
            ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption,
            TableMetadata? npcText, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate,
            long entry, int textPage, int textPageSize, int optionPage, int optionPageSize, CancellationToken cancellationToken)
        {
            MenuSchema? menu = MenuSchema.TryCreate(gossipMenu);
            OptionSchema? options = OptionSchema.TryCreate(gossipMenuOption);
            if (menu == null && options == null)
            {
                return null;
            }

            string database = gossipMenu?.DatabaseName ?? gossipMenuOption!.DatabaseName;
            textPageSize = NormalizePageSize(textPageSize);
            optionPageSize = NormalizePageSize(optionPageSize);
            textPage = Math.Max(1, textPage);
            optionPage = Math.Max(1, optionPage);

            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);
            if (!await DefinitionExistsAsync(connection, gossipMenu, menu, gossipMenuOption, options, entry, cancellationToken))
            {
                return null;
            }

            long textCount = await CountByMenuAsync(connection, gossipMenu, menu?.Entry, entry, cancellationToken);
            long optionCount = await CountByMenuAsync(connection, gossipMenuOption, options?.MenuId, entry, cancellationToken);
            long sourceCount = await CountSourcesAsync(connection, creatureTemplate, gameObjectTemplate, entry, cancellationToken);
            long parentCount = options?.ActionMenuId == null || gossipMenuOption == null
                ? 0
                : await CountByMenuAsync(connection, gossipMenuOption, options.ActionMenuId, entry, cancellationToken);

            ClampPage(ref textPage, textPageSize, textCount);
            ClampPage(ref optionPage, optionPageSize, optionCount);
            IReadOnlyList<GossipMenuTextRow> textRows = await LoadTextRowsAsync(connection, gossipMenu, menu, npcText, entry, textPage, textPageSize, cancellationToken);
            IReadOnlyList<GossipMenuOptionRow> optionRows = await LoadOptionsAsync(connection, gossipMenu, menu, gossipMenuOption, options, entry, optionPage, optionPageSize, cancellationToken);
            IReadOnlyList<GossipSourceSummary> sources = await LoadSourcesAsync(connection, creatureTemplate, gameObjectTemplate, gossipMenu, menu, gossipMenuOption, options, entry, cancellationToken);
            IReadOnlyList<GossipParentMenuLink> parents = await LoadParentsAsync(connection, gossipMenuOption, options, entry, cancellationToken);

            return new GossipMenuDetail
            {
                Menu = new GossipMenuSummary { Entry = entry, TextRowCount = textCount, OptionCount = optionCount, SourceCount = sourceCount, ParentMenuCount = parentCount },
                TextRows = Page(textRows, textCount, textPage, textPageSize),
                Options = Page(optionRows, optionCount, optionPage, optionPageSize),
                Sources = sources,
                TotalSourceCount = sourceCount,
                ParentMenus = parents,
                TotalParentMenuCount = parentCount
            };
        }

        // Report which editor operations the detected database schema supports.
        public GossipEditorCapabilities GetEditorCapabilities(TableMetadata? gossipMenu, TableMetadata? gossipMenuOption)
        {
            MenuSchema? menu = MenuSchema.TryCreate(gossipMenu);
            OptionSchema? option = OptionSchema.TryCreate(gossipMenuOption);
            return new GossipEditorCapabilities
            {
                CanMutateMenuRows = gossipMenu != null && menu != null && CanMutate(gossipMenu, MenuColumns(menu)),
                CanMutateOptions = gossipMenuOption != null && option != null && CanMutate(gossipMenuOption, OptionColumns(option)),
                HasMenuScriptId = menu?.ScriptId != null,
                HasMenuConditionId = menu?.ConditionId != null,
                HasOptionIcon = option?.OptionIcon != null,
                HasOptionText = option?.OptionText != null,
                HasOptionBroadcastTextId = option?.OptionBroadcastTextId != null,
                HasOptionId = option?.OptionId != null,
                HasNpcOptionNpcFlag = option?.NpcOptionNpcFlag != null,
                HasActionMenuId = option?.ActionMenuId != null,
                HasActionPoiId = option?.ActionPoiId != null,
                HasActionScriptId = option?.ActionScriptId != null,
                HasBoxCoded = option?.BoxCoded != null,
                HasBoxMoney = option?.BoxMoney != null,
                HasBoxText = option?.BoxText != null,
                HasBoxBroadcastTextId = option?.BoxBroadcastTextId != null,
                HasOptionConditionId = option?.ConditionId != null
            };
        }

        // Add menu row using schema-aware database operations.
        public async Task<GossipMutationResult> AddMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? npcText, GossipMenuRowValues values, CancellationToken cancellationToken)
        {
            MenuSchema schema = RequireMenu(gossipMenu);
            ValidateMenu(values);
            await ValidateNpcTextAsync(profile, npcText, values.TextId, cancellationToken);
            if (await FindMenuRowAsync(profile, gossipMenu, schema, values, cancellationToken) != null)
            {
                throw new InvalidOperationException("That gossip menu/text row already exists.");
            }

            List<ColumnMetadata> columns = MenuColumns(schema);
            string sql = "INSERT INTO " + Q(gossipMenu.Name) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" + string.Join(", ", columns.Select((_, i) => "@v" + i)) + ");";
            int affected = await ExecuteMenuMutationAsync(profile, gossipMenu, schema, values, columns, sql, cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, schema, null, null, values.Entry, affected, gossipMenu.Name, MenuInsertPreview(gossipMenu, schema, values), MenuDeletePreview(gossipMenu, schema, values), cancellationToken);
        }

        // Update menu row using schema-aware database operations.
        public async Task<GossipMutationResult> UpdateMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? npcText, GossipMenuRowValues original, GossipMenuRowValues replacement, CancellationToken cancellationToken)
        {
            MenuSchema schema = RequireMenu(gossipMenu);
            GossipMenuRowValues? persisted = await FindMenuRowAsync(profile, gossipMenu, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected gossip text row no longer exists.");
            if (replacement.Entry != persisted.Entry)
            {
                throw new InvalidOperationException("A gossip text row cannot be moved to another menu here.");
            }

            ValidateMenu(replacement);
            await ValidateNpcTextAsync(profile, npcText, replacement.TextId, cancellationToken);
            if (PrimaryKeyChanged(gossipMenu, c => MenuValue(schema, persisted, c), c => MenuValue(schema, replacement, c)) &&
                await FindMenuRowAsync(profile, gossipMenu, schema, replacement, cancellationToken) != null)
            {
                throw new InvalidOperationException("Another gossip text row already uses that key in this menu.");
            }

            List<ColumnMetadata> setters = MenuColumns(schema).Where(c => !Is(c, schema.Entry)).ToList();
            string sql = "UPDATE " + Q(gossipMenu.Name) + " SET " + string.Join(", ", setters.Select((c, i) => Q(c.Name) + "=@v" + i)) + " WHERE " + KeyWhere(gossipMenu, "@k") + " LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gossipMenu.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValues(command, setters, c => MenuValue(schema, replacement, c), "@v");
            AddKeyValues(command, gossipMenu, c => MenuValue(schema, persisted, c), "@k");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, schema, null, null, replacement.Entry, affected, gossipMenu.Name, MenuUpdatePreview(gossipMenu, schema, persisted, replacement), MenuUpdatePreview(gossipMenu, schema, replacement, persisted), cancellationToken);
        }

        // Delete menu row using schema-aware database operations.
        public async Task<GossipMutationResult> DeleteMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? gossipMenuOption, GossipMenuRowValues original, CancellationToken cancellationToken)
        {
            MenuSchema schema = RequireMenu(gossipMenu);
            OptionSchema? option = OptionSchema.TryCreate(gossipMenuOption);
            GossipMenuRowValues? persisted = await FindMenuRowAsync(profile, gossipMenu, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected gossip text row no longer exists.");
            string sql = "DELETE FROM " + Q(gossipMenu.Name) + " WHERE " + KeyWhere(gossipMenu, "@k") + " LIMIT 1;";
            int affected = await ExecuteDeleteAsync(profile, gossipMenu, sql, c => MenuValue(schema, persisted, c), cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, schema, gossipMenuOption, option, persisted.Entry, affected, gossipMenu.Name, MenuDeletePreview(gossipMenu, schema, persisted), MenuInsertPreview(gossipMenu, schema, persisted), cancellationToken);
        }

        // Add option using schema-aware database operations.
        public async Task<GossipMutationResult> AddOptionAsync(ConnectionProfile profile, TableMetadata gossipMenuOption, TableMetadata? gossipMenu, GossipOptionValues values, CancellationToken cancellationToken)
        {
            OptionSchema schema = RequireOption(gossipMenuOption);
            ValidateOption(values);
            await ValidateActionMenuAsync(profile, gossipMenu, gossipMenuOption, values.ActionMenuId, cancellationToken);
            if (await FindOptionAsync(profile, gossipMenuOption, schema, values, cancellationToken) != null)
            {
                throw new InvalidOperationException("That gossip option row already exists.");
            }

            List<ColumnMetadata> columns = OptionColumns(schema);
            string sql = "INSERT INTO " + Q(gossipMenuOption.Name) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" + string.Join(", ", columns.Select((_, i) => "@v" + i)) + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gossipMenuOption.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValues(command, columns, c => OptionValue(schema, values, c), "@v");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, MenuSchema.TryCreate(gossipMenu), gossipMenuOption, schema, values.MenuId, affected, gossipMenuOption.Name, OptionInsertPreview(gossipMenuOption, schema, values), OptionDeletePreview(gossipMenuOption, schema, values), cancellationToken);
        }

        // Update option using schema-aware database operations.
        public async Task<GossipMutationResult> UpdateOptionAsync(ConnectionProfile profile, TableMetadata gossipMenuOption, TableMetadata? gossipMenu, GossipOptionValues original, GossipOptionValues replacement, CancellationToken cancellationToken)
        {
            OptionSchema schema = RequireOption(gossipMenuOption);
            GossipOptionValues? persisted = await FindOptionAsync(profile, gossipMenuOption, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected gossip option no longer exists.");
            if (replacement.MenuId != persisted.MenuId)
            {
                throw new InvalidOperationException("A gossip option cannot be moved to another menu here.");
            }

            ValidateOption(replacement);
            await ValidateActionMenuAsync(profile, gossipMenu, gossipMenuOption, replacement.ActionMenuId, cancellationToken);
            if (PrimaryKeyChanged(gossipMenuOption, c => OptionValue(schema, persisted, c), c => OptionValue(schema, replacement, c)) &&
                await FindOptionAsync(profile, gossipMenuOption, schema, replacement, cancellationToken) != null)
            {
                throw new InvalidOperationException("Another gossip option already uses that key in this menu.");
            }

            List<ColumnMetadata> setters = OptionColumns(schema).Where(c => !Is(c, schema.MenuId)).ToList();
            string sql = "UPDATE " + Q(gossipMenuOption.Name) + " SET " + string.Join(", ", setters.Select((c, i) => Q(c.Name) + "=@v" + i)) + " WHERE " + KeyWhere(gossipMenuOption, "@k") + " LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gossipMenuOption.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddValues(command, setters, c => OptionValue(schema, replacement, c), "@v");
            AddKeyValues(command, gossipMenuOption, c => OptionValue(schema, persisted, c), "@k");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, MenuSchema.TryCreate(gossipMenu), gossipMenuOption, schema, replacement.MenuId, affected, gossipMenuOption.Name, OptionUpdatePreview(gossipMenuOption, schema, persisted, replacement), OptionUpdatePreview(gossipMenuOption, schema, replacement, persisted), cancellationToken);
        }

        // Delete option using schema-aware database operations.
        public async Task<GossipMutationResult> DeleteOptionAsync(ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata gossipMenuOption, GossipOptionValues original, CancellationToken cancellationToken)
        {
            OptionSchema schema = RequireOption(gossipMenuOption);
            GossipOptionValues? persisted = await FindOptionAsync(profile, gossipMenuOption, schema, original, cancellationToken) ?? throw new InvalidOperationException("The selected gossip option no longer exists.");
            string sql = "DELETE FROM " + Q(gossipMenuOption.Name) + " WHERE " + KeyWhere(gossipMenuOption, "@k") + " LIMIT 1;";
            int affected = await ExecuteDeleteAsync(profile, gossipMenuOption, sql, c => OptionValue(schema, persisted, c), cancellationToken);
            return await MutationResultAsync(profile, gossipMenu, MenuSchema.TryCreate(gossipMenu), gossipMenuOption, schema, persisted.MenuId, affected, gossipMenuOption.Name, OptionDeletePreview(gossipMenuOption, schema, persisted), OptionInsertPreview(gossipMenuOption, schema, persisted), cancellationToken);
        }

        // Add creature source using schema-aware database operations.
        public async Task<GossipMutationResult> AddCreatureSourceAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            long creatureEntry,
            long menuId,
            ulong gossipFlagMask,
            CancellationToken cancellationToken)
        {
            if (creatureEntry <= 0)
            {
                throw new InvalidOperationException("Select a creature before adding a gossip source.");
            }

            if (menuId <= 0)
            {
                throw new InvalidOperationException("Gossip menu ID must be greater than zero.");
            }

            CreatureSourceSchema schema = CreatureSourceSchema.TryCreate(creatureTemplate)
                ?? throw new InvalidOperationException("creature_template does not expose a supported gossip menu field.");
            if (schema.NpcFlags == null)
            {
                throw new InvalidOperationException("creature_template does not expose a supported NPC flags field.");
            }

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            long currentMenu;
            ulong currentFlags;
            string selectSql =
                "SELECT CAST(COALESCE(" + Q(schema.MenuId.Name) + ", 0) AS SIGNED), " +
                "CAST(COALESCE(" + Q(schema.NpcFlags.Name) + ", 0) AS UNSIGNED) FROM " + Q(creatureTemplate.Name) +
                " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1 FOR UPDATE;";
            await using (MySqlCommand select = new(selectSql, connection, transaction))
            {
                select.Parameters.AddWithValue("@entry", creatureEntry);
                await using MySqlDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The selected creature no longer exists in creature_template.");
                }

                currentMenu = ToLong(reader, 0);
                currentFlags = reader.IsDBNull(1) ? 0UL : Convert.ToUInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            }

            if ((currentFlags & gossipFlagMask) != 0)
            {
                throw new InvalidOperationException("The selected creature already has the Gossip NPC flag.");
            }

            ulong updatedFlags = currentFlags | gossipFlagMask;
            string updateSql =
                "UPDATE " + Q(creatureTemplate.Name) + " SET " + Q(schema.MenuId.Name) + "=@menu, " +
                Q(schema.NpcFlags.Name) + "=@flags WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            int affected;
            await using (MySqlCommand update = new(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("@menu", menuId);
                update.Parameters.AddWithValue("@flags", updatedFlags);
                update.Parameters.AddWithValue("@entry", creatureEntry);
                affected = await update.ExecuteNonQueryAsync(cancellationToken);
            }

            if (affected != 1)
            {
                throw new InvalidOperationException("The creature gossip source could not be updated safely.");
            }

            await transaction.CommitAsync(cancellationToken);

            string preview =
                "UPDATE " + Q(creatureTemplate.Name) + " SET " + Q(schema.MenuId.Name) + "=" + menuId.ToString(CultureInfo.InvariantCulture) + ", " +
                Q(schema.NpcFlags.Name) + "=" + updatedFlags.ToString(CultureInfo.InvariantCulture) + " WHERE " + Q(schema.Entry.Name) + "=" +
                creatureEntry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
            string revert =
                "UPDATE " + Q(creatureTemplate.Name) + " SET " + Q(schema.MenuId.Name) + "=" + currentMenu.ToString(CultureInfo.InvariantCulture) + ", " +
                Q(schema.NpcFlags.Name) + "=" + currentFlags.ToString(CultureInfo.InvariantCulture) + " WHERE " + Q(schema.Entry.Name) + "=" +
                creatureEntry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";

            return new GossipMutationResult
            {
                AffectedRows = affected,
                ObjectName = creatureTemplate.Name,
                SqlPreview = preview,
                RevertSqlPreview = revert
            };
        }

        // Clamp requested page sizes to the configured browsing limit.
        private int NormalizePageSize(int value) => Math.Clamp(value, 10, _options.MaximumBrowsePageSize);
        private static PagedResult<T> Empty<T>(int page, int size) => new()
        {
            Page = page,
            PageSize = size
        };

        private static PagedResult<T> Page<T>(IReadOnlyList<T> rows, long total, int page, int size) => new()
        {
            Items = rows,
            TotalCount = total,
            Page = page,
            PageSize = size
        };

        // Keep the current page within the available result range.
        private static void ClampPage(ref int page, int size, long total)
        {
            int pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)size);
            if (pages > 0 && page > pages)
            {
                page = pages;
            }
        }

        // Read a numeric column as a non-null 64-bit value using invariant culture.
        private static long ToLong(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        // Read nullable numeric fields without converting NULL to zero.
        private static long? ToNullableLong(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        // Quote a database identifier through the validated quoting helper.
        private static string Q(string value) => MySqlIdentifier.Quote(value);
        // Compare schema column names without regard to letter case.
        private static bool Is(ColumnMetadata a, ColumnMetadata b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        // Locate the first matching schema column from the supplied names.
        private static ColumnMetadata? Col(TableMetadata table, params string[] names) => table.Columns.FirstOrDefault(c => names.Any(n => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)));
        // Produce a qualified SQL column expression, falling back when the column is absent.
        private static string Expr(string alias, ColumnMetadata? column) => column == null ? "NULL" : alias + "." + Q(column.Name);

        // Execute a scalar COUNT query and read its result as a 64-bit integer.
        private static async Task<long> ScalarLongAsync(MySqlConnection connection, string sql, string search, CancellationToken cancellationToken)
        {
            await using MySqlCommand command = new(sql, connection);
            AddSearch(command, search);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
        }

        // Bind search parameters rather than interpolating user input into SQL.
        private static void AddSearch(MySqlCommand command, string search)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
            }
        }

        // Combine available gossip-definition identifiers across supported schema tables.
        private static string BuildMenuIdUnion(TableMetadata? menuTable, MenuSchema? menu, TableMetadata? optionTable, OptionSchema? option)
        {
            List<string> parts = new();
            if (menuTable != null && menu != null)
            {
                parts.Add("SELECT CAST(" + Q(menu.Entry.Name) + " AS SIGNED) menu_id FROM " + Q(menuTable.Name));
            }

            if (optionTable != null && option != null)
            {
                parts.Add("SELECT CAST(" + Q(option.MenuId.Name) + " AS SIGNED) menu_id FROM " + Q(optionTable.Name));
            }

            return "(SELECT DISTINCT x.menu_id FROM (" + string.Join(" UNION ALL ", parts) + ") x)";
        }

        // Construct a bounded menu identifier query for the requested search text.
        private static string BuildMenuSearchIdSet(string allIds, TableMetadata? optionTable, OptionSchema? option)
        {
            List<string> parts = new()
            {
                "SELECT m.menu_id FROM " + allIds + " m WHERE CAST(m.menu_id AS CHAR) LIKE @search"
            };

            if (optionTable != null && option?.OptionText != null)
            {
                parts.Add(
                    "SELECT DISTINCT CAST(o." + Q(option.MenuId.Name) + " AS SIGNED) menu_id FROM " + Q(optionTable.Name) +
                    " o WHERE o." + Q(option.OptionText.Name) + " LIKE @search");
            }

            return "(SELECT DISTINCT f.menu_id FROM (" + string.Join(" UNION ALL ", parts) + ") f)";
        }

        // Handle menu count expr within the gossip viewer service workflow.
        private static string MenuCountExpr(string menuExpr, TableMetadata? table, MenuSchema? schema) => table == null || schema == null ? "0" : "(SELECT COUNT(*) FROM " + Q(table.Name) + " mc WHERE mc." + Q(schema.Entry.Name) + "=" + menuExpr + ")";
        // Construct the expression used to count options belonging to a menu.
        private static string OptionCountExpr(string menuExpr, TableMetadata? table, OptionSchema? schema) => table == null || schema == null ? "0" : "(SELECT COUNT(*) FROM " + Q(table.Name) + " oc WHERE oc." + Q(schema.MenuId.Name) + "=" + menuExpr + ")";
        // Check whether either menu definitions or options reference an identifier.
        private static string DefinitionExistsExpr(string menuExpr, TableMetadata? menuTable, MenuSchema? menu, TableMetadata? optionTable, OptionSchema? option)
        {
            List<string> p = new();
            if (menuTable != null && menu != null)
            {
                p.Add("EXISTS (SELECT 1 FROM " + Q(menuTable.Name) + " dx WHERE dx." + Q(menu.Entry.Name) + "=" + menuExpr + ")");
            }

            if (optionTable != null && option != null)
            {
                p.Add("EXISTS (SELECT 1 FROM " + Q(optionTable.Name) + " dy WHERE dy." + Q(option.MenuId.Name) + "=" + menuExpr + ")");
            }

            return p.Count == 0 ? "0" : "CASE WHEN " + string.Join(" OR ", p) + " THEN 1 ELSE 0 END";
        }

        // Handle populate menu summary counts async within the gossip viewer service workflow.
        private static async Task PopulateMenuSummaryCountsAsync(
            MySqlConnection connection,
            TableMetadata? menuTable,
            MenuSchema? menu,
            TableMetadata? optionTable,
            OptionSchema? option,
            TableMetadata? creatureTable,
            TableMetadata? gameObjectTable,
            List<GossipMenuSummary> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return;
            }

            List<long> ids = rows.Select(row => row.Entry).Distinct().ToList();
            Dictionary<long, long> menuCounts = await LoadCountsByIdAsync(connection, menuTable, menu?.Entry, ids, cancellationToken);
            Dictionary<long, long> optionCounts = await LoadCountsByIdAsync(connection, optionTable, option?.MenuId, ids, cancellationToken);
            Dictionary<long, long> parentCounts = await LoadCountsByIdAsync(connection, optionTable, option?.ActionMenuId, ids, cancellationToken);
            Dictionary<long, long> sourceCounts = await LoadSourceCountsByMenuAsync(connection, creatureTable, gameObjectTable, ids, cancellationToken);

            foreach (GossipMenuSummary row in rows)
            {
                row.TextRowCount = GetCount(menuCounts, row.Entry);
                row.OptionCount = GetCount(optionCounts, row.Entry);
                row.ParentMenuCount = GetCount(parentCounts, row.Entry);
                row.SourceCount = GetCount(sourceCounts, row.Entry);
            }
        }

        // Handle populate source definition counts async within the gossip viewer service workflow.
        private static async Task PopulateSourceDefinitionCountsAsync(
            MySqlConnection connection,
            TableMetadata? menuTable,
            MenuSchema? menu,
            TableMetadata? optionTable,
            OptionSchema? option,
            List<GossipSourceSummary> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return;
            }

            List<long> ids = rows.Select(row => row.MenuId).Distinct().ToList();
            Dictionary<long, long> menuCounts = await LoadCountsByIdAsync(connection, menuTable, menu?.Entry, ids, cancellationToken);
            Dictionary<long, long> optionCounts = await LoadCountsByIdAsync(connection, optionTable, option?.MenuId, ids, cancellationToken);

            foreach (GossipSourceSummary row in rows)
            {
                row.TextRowCount = GetCount(menuCounts, row.MenuId);
                row.OptionCount = GetCount(optionCounts, row.MenuId);
                row.MenuExists = row.TextRowCount + row.OptionCount > 0;
            }
        }

        // Fetch related-record counts in a batch to avoid per-row database queries.
        private static async Task<Dictionary<long, long>> LoadCountsByIdAsync(
            MySqlConnection connection,
            TableMetadata? table,
            ColumnMetadata? column,
            List<long> ids,
            CancellationToken cancellationToken)
        {
            Dictionary<long, long> result = new();
            if (table == null || column == null || ids.Count == 0)
            {
                return result;
            }

            await using MySqlCommand command = connection.CreateCommand();
            string inClause = AddIdParameters(command, ids, "@id");
            command.CommandText =
                "SELECT CAST(" + Q(column.Name) + " AS SIGNED) menu_id, COUNT(*) row_count FROM " + Q(table.Name) +
                " WHERE " + Q(column.Name) + " IN (" + inClause + ") GROUP BY " + Q(column.Name) + ";";

            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[ToLong(reader, 0)] = ToLong(reader, 1);
            }

            return result;
        }

        // Count menu sources in a single query for a set of selected menus.
        private static async Task<Dictionary<long, long>> LoadSourceCountsByMenuAsync(
            MySqlConnection connection,
            TableMetadata? creatureTable,
            TableMetadata? gameObjectTable,
            List<long> ids,
            CancellationToken cancellationToken)
        {
            Dictionary<long, long> result = new();
            if (ids.Count == 0)
            {
                return result;
            }

            await using MySqlCommand command = connection.CreateCommand();
            string inClause = AddIdParameters(command, ids, "@sourceId");
            List<string> branches = BuildSourceCountBranches(creatureTable, gameObjectTable, inClause);
            if (branches.Count == 0)
            {
                return result;
            }

            command.CommandText =
                "SELECT s.menu_id, COUNT(*) source_count FROM (" + string.Join(" UNION ALL ", branches) +
                ") s GROUP BY s.menu_id;";

            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[ToLong(reader, 0)] = ToLong(reader, 1);
            }

            return result;
        }

        // Add id parameters using schema-aware database operations.
        private static string AddIdParameters(MySqlCommand command, List<long> ids, string prefix)
        {
            List<string> names = new(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                string name = prefix + i.ToString(CultureInfo.InvariantCulture);
                names.Add(name);
                command.Parameters.AddWithValue(name, ids[i]);
            }

            return string.Join(", ", names);
        }

        // Read count using schema-aware database operations.
        private static long GetCount(Dictionary<long, long> counts, long id) => counts.TryGetValue(id, out long count) ? count : 0;


        // Compose count queries for each supported source-table variant.
        private static List<string> BuildSourceCountBranches(TableMetadata? creatureTable, TableMetadata? goTable, string inClause)
        {
            List<string> rows = new();
            CreatureSourceSchema? creature = CreatureSourceSchema.TryCreate(creatureTable);
            if (creatureTable != null && creature != null)
            {
                rows.Add(
                    "SELECT CAST(c." + Q(creature.MenuId.Name) + " AS SIGNED) menu_id FROM " + Q(creatureTable.Name) +
                    " c WHERE c." + Q(creature.MenuId.Name) + ">0 AND c." + Q(creature.MenuId.Name) + " IN (" + inClause + ")");
            }

            GoSourceSchema? go = GoSourceSchema.TryCreate(goTable);
            if (goTable != null && go != null)
            {
                if (go.Data3 != null)
                {
                    rows.Add(
                        "SELECT CAST(g." + Q(go.Data3.Name) + " AS SIGNED) menu_id FROM " + Q(goTable.Name) +
                        " g WHERE g." + Q(go.Type.Name) + "=2 AND g." + Q(go.Data3.Name) + ">0 AND g." + Q(go.Data3.Name) + " IN (" + inClause + ")");
                }

                if (go.Data19 != null)
                {
                    rows.Add(
                        "SELECT CAST(g." + Q(go.Data19.Name) + " AS SIGNED) menu_id FROM " + Q(goTable.Name) +
                        " g WHERE g." + Q(go.Type.Name) + "=10 AND g." + Q(go.Data19.Name) + ">0 AND g." + Q(go.Data19.Name) + " IN (" + inClause + ")");
                }
            }

            return rows;
        }

        // Build source branches using schema-aware database operations.
        private static List<string> BuildSourceBranches(TableMetadata? creatureTable, TableMetadata? goTable, string filter)
        {
            List<string> rows = new();
            if (filter != GossipSourceFilters.GameObjects)
            {
                CreatureSourceSchema? c = CreatureSourceSchema.TryCreate(creatureTable);
                if (creatureTable != null && c != null)
                {
                    rows.Add("SELECT 'Creature' entity_type, CAST(c." + Q(c.Entry.Name) + " AS SIGNED) entity_entry, " + (c.Name == null ? "''" : "COALESCE(c." + Q(c.Name.Name) + ",'')") + " entity_name, CAST(c." + Q(c.MenuId.Name) + " AS SIGNED) menu_id, 'Creature template' link_kind FROM " + Q(creatureTable.Name) + " c WHERE c." + Q(c.MenuId.Name) + ">0");
                }
            }

            if (filter != GossipSourceFilters.Creatures)
            {
                GoSourceSchema? g = GoSourceSchema.TryCreate(goTable);
                if (goTable != null && g != null)
                {
                    string name = g.Name == null ? "''" : "COALESCE(g." + Q(g.Name.Name) + ",'')";
                    if (g.Data3 != null)
                    {
                        rows.Add("SELECT 'GameObject' entity_type, CAST(g." + Q(g.Entry.Name) + " AS SIGNED) entity_entry, " + name + " entity_name, CAST(g." + Q(g.Data3.Name) + " AS SIGNED) menu_id, 'Questgiver (data3)' link_kind FROM " + Q(goTable.Name) + " g WHERE g." + Q(g.Type.Name) + "=2 AND g." + Q(g.Data3.Name) + ">0");
                    }

                    if (g.Data19 != null)
                    {
                        rows.Add("SELECT 'GameObject' entity_type, CAST(g." + Q(g.Entry.Name) + " AS SIGNED) entity_entry, " + name + " entity_name, CAST(g." + Q(g.Data19.Name) + " AS SIGNED) menu_id, 'Goober (data19)' link_kind FROM " + Q(goTable.Name) + " g WHERE g." + Q(g.Type.Name) + "=10 AND g." + Q(g.Data19.Name) + ">0");
                    }
                }
            }

            return rows;
        }

        // Handle definition exists async within the gossip viewer service workflow.
        private static async Task<bool> DefinitionExistsAsync(MySqlConnection connection, TableMetadata? menuTable, MenuSchema? menu, TableMetadata? optionTable, OptionSchema? option, long entry, CancellationToken token)
        {
            string expr = DefinitionExistsExpr("@entry", menuTable, menu, optionTable, option);
            await using MySqlCommand cmd = new("SELECT " + expr + ";", connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture) != 0;
        }

        // Count by menu using schema-aware database operations.
        private static async Task<long> CountByMenuAsync(MySqlConnection connection, TableMetadata? table, ColumnMetadata? column, long entry, CancellationToken token)
        {
            if (table == null || column == null)
            {
                return 0;
            }

            await using MySqlCommand cmd = new("SELECT COUNT(*) FROM " + Q(table.Name) + " WHERE " + Q(column.Name) + "=@entry;", connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture);
        }

        // Count sources using schema-aware database operations.
        private static async Task<long> CountSourcesAsync(MySqlConnection connection, TableMetadata? creature, TableMetadata? go, long entry, CancellationToken token)
        {
            List<string> branches = BuildSourceBranches(creature, go, GossipSourceFilters.All);
            if (branches.Count == 0)
            {
                return 0;
            }

            await using MySqlCommand cmd = new("SELECT COUNT(*) FROM (" + string.Join(" UNION ALL ", branches) + ") s WHERE s.menu_id=@entry;", connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(token) ?? 0, CultureInfo.InvariantCulture);
        }

        // Load text rows using schema-aware database operations.
        private static async Task<IReadOnlyList<GossipMenuTextRow>> LoadTextRowsAsync(MySqlConnection connection, TableMetadata? table, MenuSchema? schema, TableMetadata? npcText, long entry, int page, int size, CancellationToken token)
        {
            if (table == null || schema == null)
            {
                return Array.Empty<GossipMenuTextRow>();
            }

            NpcTextSchema? nt = NpcTextSchema.TryCreate(npcText);
            string join = nt == null || npcText == null ? string.Empty : " LEFT JOIN " + Q(npcText.Name) + " nt ON nt." + Q(nt.Id.Name) + "=gm." + Q(schema.TextId.Name);
            string preview = nt?.PreviewText == null ? "NULL" : "nt." + Q(nt.PreviewText.Name);
            string sql = "SELECT gm." + Q(schema.Entry.Name) + ", gm." + Q(schema.TextId.Name) + ", " + preview + ", " + Expr("gm", schema.ScriptId) + ", " + Expr("gm", schema.ConditionId) + " FROM " + Q(table.Name) + " gm" + join + " WHERE gm." + Q(schema.Entry.Name) + "=@entry ORDER BY gm." + Q(schema.TextId.Name) + " LIMIT @limit OFFSET @offset;";
            List<GossipMenuTextRow> rows = new();
            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            cmd.Parameters.AddWithValue("@limit", size);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * size);
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                rows.Add(new GossipMenuTextRow
                {
                    Entry = ToLong(r, 0),
                    TextId = ToLong(r, 1),
                    TextPreview = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    IsTextResolved = !r.IsDBNull(2),
                    ScriptId = ToNullableLong(r, 3),
                    ConditionId = ToNullableLong(r, 4)
                });
            }

            return rows;
        }

        // Load options using schema-aware database operations.
        private static async Task<IReadOnlyList<GossipMenuOptionRow>> LoadOptionsAsync(MySqlConnection connection, TableMetadata? menuTable, MenuSchema? menu, TableMetadata? table, OptionSchema? schema, long entry, int page, int size, CancellationToken token)
        {
            if (table == null || schema == null)
            {
                return Array.Empty<GossipMenuOptionRow>();
            }

            string actionExists = schema.ActionMenuId == null ? "0" : DefinitionExistsExpr("o." + Q(schema.ActionMenuId.Name), menuTable, menu, table, schema);
            string sql = "SELECT o." + Q(schema.MenuId.Name) + ", o." + Q(schema.Id.Name) + ", " + Expr("o", schema.OptionIcon) + ", " + Expr("o", schema.OptionText) + ", " + Expr("o", schema.OptionBroadcastTextId) + ", " + Expr("o", schema.OptionId) + ", " + Expr("o", schema.NpcOptionNpcFlag) + ", " + Expr("o", schema.ActionMenuId) + ", " + actionExists + ", " + Expr("o", schema.ActionPoiId) + ", " + Expr("o", schema.ActionScriptId) + ", " + Expr("o", schema.BoxCoded) + ", " + Expr("o", schema.BoxMoney) + ", " + Expr("o", schema.BoxText) + ", " + Expr("o", schema.BoxBroadcastTextId) + ", " + Expr("o", schema.ConditionId) + " FROM " + Q(table.Name) + " o WHERE o." + Q(schema.MenuId.Name) + "=@entry ORDER BY o." + Q(schema.Id.Name) + " LIMIT @limit OFFSET @offset;";
            List<GossipMenuOptionRow> rows = new();
            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            cmd.Parameters.AddWithValue("@limit", size);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * size);
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                rows.Add(new GossipMenuOptionRow
                {
                    MenuId = ToLong(r, 0),
                    Id = ToLong(r, 1),
                    OptionIcon = ToNullableLong(r, 2),
                    OptionText = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                    OptionBroadcastTextId = ToNullableLong(r, 4),
                    OptionId = ToNullableLong(r, 5),
                    NpcOptionNpcFlag = ToNullableLong(r, 6),
                    ActionMenuId = ToNullableLong(r, 7),
                    ActionMenuExists = !r.IsDBNull(8) && Convert.ToInt32(r.GetValue(8), CultureInfo.InvariantCulture) != 0,
                    ActionPoiId = ToNullableLong(r, 9),
                    ActionScriptId = ToNullableLong(r, 10),
                    BoxCoded = r.IsDBNull(11) ? null : Convert.ToInt32(r.GetValue(11), CultureInfo.InvariantCulture) != 0,
                    BoxMoney = ToNullableLong(r, 12),
                    BoxText = r.IsDBNull(13) ? string.Empty : r.GetString(13),
                    BoxBroadcastTextId = ToNullableLong(r, 14),
                    ConditionId = ToNullableLong(r, 15)
                });
            }

            return rows;
        }

        // Load sources using schema-aware database operations.
        private static async Task<IReadOnlyList<GossipSourceSummary>> LoadSourcesAsync(MySqlConnection connection, TableMetadata? creature, TableMetadata? go, TableMetadata? menuTable, MenuSchema? menu, TableMetadata? optionTable, OptionSchema? option, long entry, CancellationToken token)
        {
            List<string> branches = BuildSourceBranches(creature, go, GossipSourceFilters.All);
            if (branches.Count == 0)
            {
                return Array.Empty<GossipSourceSummary>();
            }

            string sql = "SELECT s.entity_type,s.entity_entry,s.entity_name,s.menu_id,s.link_kind," + DefinitionExistsExpr("s.menu_id", menuTable, menu, optionTable, option) + "," + MenuCountExpr("s.menu_id", menuTable, menu) + "," + OptionCountExpr("s.menu_id", optionTable, option) + " FROM (" + string.Join(" UNION ALL ", branches) + ") s WHERE s.menu_id=@entry ORDER BY s.entity_type,s.entity_name LIMIT " + RelationshipPreviewLimit + ";";
            List<GossipSourceSummary> rows = new();
            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                rows.Add(new GossipSourceSummary
                {
                    EntityType = r.GetString(0),
                    EntityEntry = ToLong(r, 1),
                    EntityName = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    MenuId = ToLong(r, 3),
                    LinkKind = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                    MenuExists = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture) != 0,
                    TextRowCount = ToLong(r, 6),
                    OptionCount = ToLong(r, 7)
                });
            }

            return rows;
        }

        // Load parents using schema-aware database operations.
        private static async Task<IReadOnlyList<GossipParentMenuLink>> LoadParentsAsync(MySqlConnection connection, TableMetadata? table, OptionSchema? schema, long entry, CancellationToken token)
        {
            if (table == null || schema?.ActionMenuId == null)
            {
                return Array.Empty<GossipParentMenuLink>();
            }

            string sql = "SELECT " + Q(schema.MenuId.Name) + "," + Q(schema.Id.Name) + "," + (schema.OptionText == null ? "''" : "COALESCE(" + Q(schema.OptionText.Name) + ",'')") + " FROM " + Q(table.Name) + " WHERE " + Q(schema.ActionMenuId.Name) + "=@entry ORDER BY " + Q(schema.MenuId.Name) + "," + Q(schema.Id.Name) + " LIMIT " + RelationshipPreviewLimit + ";";
            List<GossipParentMenuLink> rows = new();
            await using MySqlCommand cmd = new(sql, connection);
            cmd.Parameters.AddWithValue("@entry", entry);
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            while (await r.ReadAsync(token))
            {
                rows.Add(new GossipParentMenuLink
                {
                    MenuId = ToLong(r, 0),
                    OptionId = ToLong(r, 1),
                    OptionText = r.IsDBNull(2) ? string.Empty : r.GetString(2)
                });
            }

            return rows;
        }

        // Require menu using schema-aware database operations.
        private static MenuSchema RequireMenu(TableMetadata table)
        {
            MenuSchema? s = MenuSchema.TryCreate(table);
            if (s == null || !CanMutate(table, MenuColumns(s)))
            {
                throw new InvalidOperationException("This gossip_menu schema cannot be edited safely by the structured viewer.");
            }

            return s;
        }

        // Require option using schema-aware database operations.
        private static OptionSchema RequireOption(TableMetadata table)
        {
            OptionSchema? s = OptionSchema.TryCreate(table);
            if (s == null || !CanMutate(table, OptionColumns(s)))
            {
                throw new InvalidOperationException("This gossip_menu_option schema cannot be edited safely by the structured viewer.");
            }

            return s;
        }

        // Determine whether mutate using schema-aware database operations.
        private static bool CanMutate(TableMetadata table, IReadOnlyList<ColumnMetadata> supported)
        {
            HashSet<string> names = supported.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (table.PrimaryKeyColumns.Count == 0 || table.PrimaryKeyColumns.Any(c => !names.Contains(c.Name)))
            {
                return false;
            }

            return table.Columns.All(c => names.Contains(c.Name) || c.IsGenerated || c.IsAutoIncrement || c.IsNullable || c.DefaultValue != null);
        }

        // Handle menu columns within the gossip viewer service workflow.
        private static List<ColumnMetadata> MenuColumns(MenuSchema s) => Distinct(s.Entry, s.TextId, s.ScriptId, s.ConditionId);
        // Collect the columns needed to read or update gossip options.
        private static List<ColumnMetadata> OptionColumns(OptionSchema s) => Distinct(s.MenuId, s.Id, s.OptionIcon, s.OptionText, s.OptionBroadcastTextId, s.OptionId, s.NpcOptionNpcFlag, s.ActionMenuId, s.ActionPoiId, s.ActionScriptId, s.BoxCoded, s.BoxMoney, s.BoxText, s.BoxBroadcastTextId, s.ConditionId);
        // Filter out absent columns and remove duplicate schema metadata references.
        private static List<ColumnMetadata> Distinct(params ColumnMetadata?[] columns) => columns.Where(c => c != null).Cast<ColumnMetadata>().GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(c => c.OrdinalPosition).ToList();
        // Reject invalid gossip-menu values before issuing an update.
        private static void ValidateMenu(GossipMenuRowValues v)
        {
            if (v.Entry < 0 || v.TextId < 0)
            {
                throw new InvalidOperationException("Menu and text IDs cannot be negative.");
            }

            if ((v.ScriptId < 0) || (v.ConditionId < 0))
            {
                throw new InvalidOperationException("Script and condition IDs cannot be negative.");
            }
        }

        // Validate option using schema-aware database operations.
        private static void ValidateOption(GossipOptionValues v)
        {
            if (v.MenuId < 0 || v.Id < 0)
            {
                throw new InvalidOperationException("Menu and option IDs cannot be negative.");
            }

            long?[] nonNegative = { v.OptionIcon, v.OptionBroadcastTextId, v.OptionId, v.NpcOptionNpcFlag, v.ActionPoiId, v.ActionScriptId, v.BoxMoney, v.BoxBroadcastTextId, v.ConditionId };
            if (nonNegative.Any(value => value < 0))
            {
                throw new InvalidOperationException("Option numeric values cannot be negative (action menu may use a negative close-menu sentinel).");
            }
        }

        // Validate npc text using schema-aware database operations.
        private async Task ValidateNpcTextAsync(ConnectionProfile profile, TableMetadata? table, long id, CancellationToken token)
        {
            NpcTextSchema? s = NpcTextSchema.TryCreate(table);
            if (table == null || s == null || id <= 0)
            {
                return;
            }

            await using MySqlConnection c = _connectionFactory.Create(profile, table.DatabaseName);
            await c.OpenAsync(token);
            await using MySqlCommand cmd = new("SELECT 1 FROM " + Q(table.Name) + " WHERE " + Q(s.Id.Name) + "=@id LIMIT 1;", c);
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteScalarAsync(token) == null)
            {
                throw new InvalidOperationException("NPC text ID " + id.ToString(CultureInfo.InvariantCulture) + " was not found in " + table.Name + ".");
            }
        }

        // Validate action menu using schema-aware database operations.
        private async Task ValidateActionMenuAsync(ConnectionProfile profile, TableMetadata? menuTable, TableMetadata optionTable, long? actionId, CancellationToken token)
        {
            if (!actionId.HasValue || actionId.Value <= 0)
            {
                return;
            }

            MenuSchema? m = MenuSchema.TryCreate(menuTable);
            OptionSchema? o = OptionSchema.TryCreate(optionTable);
            await using MySqlConnection c = _connectionFactory.Create(profile, optionTable.DatabaseName);
            await c.OpenAsync(token);
            if (!await DefinitionExistsAsync(c, menuTable, m, optionTable, o, actionId.Value, token))
            {
                throw new InvalidOperationException("Action menu " + actionId.Value.ToString(CultureInfo.InvariantCulture) + " does not exist.");
            }
        }

        // Locate menu row using schema-aware database operations.
        private async Task<GossipMenuRowValues?> FindMenuRowAsync(ConnectionProfile profile, TableMetadata table, MenuSchema s, GossipMenuRowValues key, CancellationToken token)
        {
            string sql = "SELECT " + Q(s.Entry.Name) + "," + Q(s.TextId.Name) + "," + Expr("m", s.ScriptId) + "," + Expr("m", s.ConditionId) +
                         " FROM " + Q(table.Name) + " m WHERE " + KeyWhere(table, "@k") + " LIMIT 1;";
            await using MySqlConnection c = _connectionFactory.Create(profile, table.DatabaseName);
            await c.OpenAsync(token);
            await using MySqlCommand cmd = new(sql, c);
            AddKeyValues(cmd, table, column => MenuValue(s, key, column), "@k");
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            if (!await r.ReadAsync(token))
            {
                return null;
            }

            return new GossipMenuRowValues { Entry = ToLong(r, 0), TextId = ToLong(r, 1), ScriptId = ToNullableLong(r, 2), ConditionId = ToNullableLong(r, 3) };
        }

        // Locate option using schema-aware database operations.
        private async Task<GossipOptionValues?> FindOptionAsync(ConnectionProfile profile, TableMetadata table, OptionSchema s, GossipOptionValues key, CancellationToken token)
        {
            string sql = "SELECT " + Q(s.MenuId.Name) + "," + Q(s.Id.Name) + "," + Expr("o", s.OptionIcon) + "," + Expr("o", s.OptionText) + "," + Expr("o", s.OptionBroadcastTextId) + "," + Expr("o", s.OptionId) + "," + Expr("o", s.NpcOptionNpcFlag) + "," + Expr("o", s.ActionMenuId) + "," + Expr("o", s.ActionPoiId) + "," + Expr("o", s.ActionScriptId) + "," + Expr("o", s.BoxCoded) + "," + Expr("o", s.BoxMoney) + "," + Expr("o", s.BoxText) + "," + Expr("o", s.BoxBroadcastTextId) + "," + Expr("o", s.ConditionId) +
                         " FROM " + Q(table.Name) + " o WHERE " + KeyWhere(table, "@k") + " LIMIT 1;";
            await using MySqlConnection c = _connectionFactory.Create(profile, table.DatabaseName);
            await c.OpenAsync(token);
            await using MySqlCommand cmd = new(sql, c);
            AddKeyValues(cmd, table, column => OptionValue(s, key, column), "@k");
            await using MySqlDataReader r = await cmd.ExecuteReaderAsync(token);
            if (!await r.ReadAsync(token))
            {
                return null;
            }

            return new GossipOptionValues
            {
                MenuId = ToLong(r, 0),
                Id = ToLong(r, 1),
                OptionIcon = ToNullableLong(r, 2),
                OptionText = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                OptionBroadcastTextId = ToNullableLong(r, 4),
                OptionId = ToNullableLong(r, 5),
                NpcOptionNpcFlag = ToNullableLong(r, 6),
                ActionMenuId = ToNullableLong(r, 7),
                ActionPoiId = ToNullableLong(r, 8),
                ActionScriptId = ToNullableLong(r, 9),
                BoxCoded = r.IsDBNull(10) ? null : Convert.ToInt32(r.GetValue(10), CultureInfo.InvariantCulture) != 0,
                BoxMoney = ToNullableLong(r, 11),
                BoxText = r.IsDBNull(12) ? string.Empty : r.GetString(12),
                BoxBroadcastTextId = ToNullableLong(r, 13),
                ConditionId = ToNullableLong(r, 14)
            };
        }

        // Execute menu mutation using schema-aware database operations.
        private async Task<int> ExecuteMenuMutationAsync(ConnectionProfile profile, TableMetadata table, MenuSchema schema, GossipMenuRowValues values, IReadOnlyList<ColumnMetadata> columns, string sql, CancellationToken token)
        {
            await using MySqlConnection c = _connectionFactory.Create(profile, table.DatabaseName);
            await c.OpenAsync(token);
            await using MySqlCommand cmd = new(sql, c);
            AddValues(cmd, columns, col => MenuValue(schema, values, col), "@v");
            return await cmd.ExecuteNonQueryAsync(token);
        }

        // Execute delete using schema-aware database operations.
        private async Task<int> ExecuteDeleteAsync(ConnectionProfile profile, TableMetadata table, string sql, Func<ColumnMetadata, object?> value, CancellationToken token)
        {
            await using MySqlConnection c = _connectionFactory.Create(profile, table.DatabaseName);
            await c.OpenAsync(token);
            await using MySqlCommand cmd = new(sql, c);
            AddKeyValues(cmd, table, value, "@k");
            return await cmd.ExecuteNonQueryAsync(token);
        }

        // Handle mutation result async within the gossip viewer service workflow.
        private async Task<GossipMutationResult> MutationResultAsync(ConnectionProfile profile, TableMetadata? menuTable, MenuSchema? menu, TableMetadata? optionTable, OptionSchema? option, long entry, int affected, string objectName, string sql, string revert, CancellationToken token)
        {
            string database = menuTable?.DatabaseName ?? optionTable!.DatabaseName;
            await using MySqlConnection c = _connectionFactory.Create(profile, database);
            await c.OpenAsync(token);
            return new GossipMutationResult
            {
                AffectedRows = affected,
                ObjectName = objectName,
                SqlPreview = sql,
                RevertSqlPreview = revert,
                RemainingMenuRows = await CountByMenuAsync(c, menuTable, menu?.Entry, entry, token),
                RemainingOptions = await CountByMenuAsync(c, optionTable, option?.MenuId, entry, token)
            };
        }

        // Handle key where within the gossip viewer service workflow.
        private static string KeyWhere(TableMetadata table, string prefix) => string.Join(" AND ", table.PrimaryKeyColumns.Select((c, i) => Q(c.Name) + "=" + prefix + i));
        // Bind the identifying columns required for the requested update.
        private static void AddKeyValues(MySqlCommand cmd, TableMetadata table, Func<ColumnMetadata, object?> value, string prefix)
        {
            for (int i = 0; i < table.PrimaryKeyColumns.Count; i++)
            {
                ColumnMetadata column = table.PrimaryKeyColumns[i];
                cmd.Parameters.AddWithValue(prefix + i, DbValue(column, value(column)) ?? DBNull.Value);
            }
        }

        // Handle primary key changed within the gossip viewer service workflow.
        private static bool PrimaryKeyChanged(TableMetadata table, Func<ColumnMetadata, object?> oldValue, Func<ColumnMetadata, object?> newValue)
        {
            return table.PrimaryKeyColumns.Any(column => !DbValuesEqual(DbValue(column, oldValue(column)), DbValue(column, newValue(column))));
        }

        // Handle db values equal within the gossip viewer service workflow.
        private static bool DbValuesEqual(object? left, object? right)
        {
            if (left == null || left == DBNull.Value)
            {
                return right == null || right == DBNull.Value;
            }

            if (right == null || right == DBNull.Value)
            {
                return false;
            }

            return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        // Add values using schema-aware database operations.
        private static void AddValues(MySqlCommand cmd, IReadOnlyList<ColumnMetadata> columns, Func<ColumnMetadata, object?> value, string prefix)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                ColumnMetadata column = columns[i];
                cmd.Parameters.AddWithValue(prefix + i, DbValue(column, value(column)) ?? DBNull.Value);
            }
        }

        // Handle menu value within the gossip viewer service workflow.
        private static object? MenuValue(MenuSchema s, GossipMenuRowValues v, ColumnMetadata c)
        {
            if (Is(c, s.Entry))
            {
                return v.Entry;
            }

            if (Is(c, s.TextId))
            {
                return v.TextId;
            }

            if (s.ScriptId != null && Is(c, s.ScriptId))
            {
                return v.ScriptId;
            }

            if (s.ConditionId != null && Is(c, s.ConditionId))
            {
                return v.ConditionId;
            }

            return null;
        }

        // Handle option value within the gossip viewer service workflow.
        private static object? OptionValue(OptionSchema s, GossipOptionValues v, ColumnMetadata c)
        {
            if (Is(c, s.MenuId))
            {
                return v.MenuId;
            }

            if (Is(c, s.Id))
            {
                return v.Id;
            }

            if (s.OptionIcon != null && Is(c, s.OptionIcon))
            {
                return v.OptionIcon;
            }

            if (s.OptionText != null && Is(c, s.OptionText))
            {
                return v.OptionText;
            }

            if (s.OptionBroadcastTextId != null && Is(c, s.OptionBroadcastTextId))
            {
                return v.OptionBroadcastTextId;
            }

            if (s.OptionId != null && Is(c, s.OptionId))
            {
                return v.OptionId;
            }

            if (s.NpcOptionNpcFlag != null && Is(c, s.NpcOptionNpcFlag))
            {
                return v.NpcOptionNpcFlag;
            }

            if (s.ActionMenuId != null && Is(c, s.ActionMenuId))
            {
                return v.ActionMenuId;
            }

            if (s.ActionPoiId != null && Is(c, s.ActionPoiId))
            {
                return v.ActionPoiId;
            }

            if (s.ActionScriptId != null && Is(c, s.ActionScriptId))
            {
                return v.ActionScriptId;
            }

            if (s.BoxCoded != null && Is(c, s.BoxCoded))
            {
                return v.BoxCoded;
            }

            if (s.BoxMoney != null && Is(c, s.BoxMoney))
            {
                return v.BoxMoney;
            }

            if (s.BoxText != null && Is(c, s.BoxText))
            {
                return v.BoxText;
            }

            if (s.BoxBroadcastTextId != null && Is(c, s.BoxBroadcastTextId))
            {
                return v.BoxBroadcastTextId;
            }

            if (s.ConditionId != null && Is(c, s.ConditionId))
            {
                return v.ConditionId;
            }

            return null;
        }

        // Insert preview using schema-aware database operations.
        private static string InsertPreview(TableMetadata table, IReadOnlyList<ColumnMetadata> cols, Func<ColumnMetadata, object?> value) => "INSERT INTO " + Q(table.Name) + " (" + string.Join(", ", cols.Select(c => Q(c.Name))) + ") VALUES (" + string.Join(", ", cols.Select(c => Literal(DbValue(c, value(c))))) + ");";
        // Delete preview using schema-aware database operations.
        private static string DeletePreview(TableMetadata table, Func<ColumnMetadata, object?> value) => "DELETE FROM " + Q(table.Name) + " WHERE " + string.Join(" AND ", table.PrimaryKeyColumns.Select(c => Q(c.Name) + "=" + Literal(DbValue(c, value(c))))) + " LIMIT 1;";
        // Build the SQL preview showing the pending update.
        private static string UpdatePreview(TableMetadata table, IReadOnlyList<ColumnMetadata> setters, Func<ColumnMetadata, object?> oldValue, Func<ColumnMetadata, object?> newValue) => "UPDATE " + Q(table.Name) + " SET " + string.Join(", ", setters.Select(c => Q(c.Name) + "=" + Literal(DbValue(c, newValue(c))))) + " WHERE " + string.Join(" AND ", table.PrimaryKeyColumns.Select(c => Q(c.Name) + "=" + Literal(DbValue(c, oldValue(c))))) + " LIMIT 1;";
        // Build the SQL preview for inserting a menu definition.
        private static string MenuInsertPreview(TableMetadata t, MenuSchema s, GossipMenuRowValues v) => InsertPreview(t, MenuColumns(s), c => MenuValue(s, v, c));
        // Build the SQL preview for deleting a menu definition.
        private static string MenuDeletePreview(TableMetadata t, MenuSchema s, GossipMenuRowValues v) => DeletePreview(t, c => MenuValue(s, v, c));
        // Handle menu update preview within the gossip viewer service workflow.
        private static string MenuUpdatePreview(TableMetadata t, MenuSchema s, GossipMenuRowValues a, GossipMenuRowValues b) => UpdatePreview(t, MenuColumns(s).Where(c => !Is(c, s.Entry)).ToList(), c => MenuValue(s, a, c), c => MenuValue(s, b, c));
        // Build the SQL preview for inserting a gossip option.
        private static string OptionInsertPreview(TableMetadata t, OptionSchema s, GossipOptionValues v) => InsertPreview(t, OptionColumns(s), c => OptionValue(s, v, c));
        // Build the SQL preview for deleting a gossip option.
        private static string OptionDeletePreview(TableMetadata t, OptionSchema s, GossipOptionValues v) => DeletePreview(t, c => OptionValue(s, v, c));
        // Build the SQL preview for updating a gossip option.
        private static string OptionUpdatePreview(TableMetadata t, OptionSchema s, GossipOptionValues a, GossipOptionValues b) => UpdatePreview(t, OptionColumns(s).Where(c => !Is(c, s.MenuId)).ToList(), c => OptionValue(s, a, c), c => OptionValue(s, b, c));
        // Handle db value within the gossip viewer service workflow.
        private static object? DbValue(ColumnMetadata column, object? value)
        {
            if (value != null && value != DBNull.Value)
            {
                return value;
            }

            if (column.IsNullable)
            {
                return null;
            }

            if (column.DefaultValue != null)
            {
                return column.DefaultValue;
            }

            if (column.IsNumeric)
            {
                return 0L;
            }

            if (column.IsTextual)
            {
                return string.Empty;
            }

            return null;
        }

        // Handle literal within the gossip viewer service workflow.
        private static string Literal(object? v)
        {
            if (v == null || v == DBNull.Value)
            {
                return "NULL";
            }

            if (v is bool b)
            {
                return b ? "1" : "0";
            }

            if (v is string s)
            {
                return "'" + s.Replace("'", "''") + "'";
            }

            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "NULL";
        }

        private sealed class MenuSchema
        {
            public ColumnMetadata Entry = null!;
            public ColumnMetadata TextId = null!;
            public ColumnMetadata? ScriptId; public ColumnMetadata? ConditionId;
            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static MenuSchema? TryCreate(TableMetadata? t)
            {
                if (t == null)
                {
                    return null;
                }

                ColumnMetadata? e = Col(t, "entry", "menu_id", "MenuID");
                ColumnMetadata? x = Col(t, "text_id", "textId", "TextID");
                return e == null || x == null ? null : new MenuSchema
                {
                    Entry = e,
                    TextId = x,
                    ScriptId = Col(t, "script_id", "scriptId", "ScriptID"),
                    ConditionId = Col(t, "condition_id", "conditionId", "ConditionID")
                };
            }
        }

        private sealed class OptionSchema
        {
            public ColumnMetadata MenuId = null!;
            public ColumnMetadata Id = null!;
            public ColumnMetadata? OptionIcon, OptionText, OptionBroadcastTextId, OptionId, NpcOptionNpcFlag, ActionMenuId, ActionPoiId, ActionScriptId, BoxCoded, BoxMoney, BoxText, BoxBroadcastTextId, ConditionId;
            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static OptionSchema? TryCreate(TableMetadata? t)
            {
                if (t == null)
                {
                    return null;
                }

                ColumnMetadata? m = Col(t, "menu_id", "MenuID", "menuId", "entry");
                ColumnMetadata? id = Col(t, "id", "OptionID", "optionIndex");
                if (m == null || id == null)
                {
                    return null;
                }

                return new OptionSchema
                {
                    MenuId = m,
                    Id = id,
                    OptionIcon = Col(t, "option_icon", "OptionIcon"),
                    OptionText = Col(t, "option_text", "OptionText"),
                    OptionBroadcastTextId = Col(t, "option_broadcast_text_id", "OptionBroadcastTextID", "option_broadcast_text"),
                    OptionId = Col(t, "option_id", "OptionType", "option_type"),
                    NpcOptionNpcFlag = Col(t, "npc_option_npcflag", "OptionNpcFlag", "npc_option_flag"),
                    ActionMenuId = Col(t, "action_menu_id", "ActionMenuID"),
                    ActionPoiId = Col(t, "action_poi_id", "ActionPoiID"),
                    ActionScriptId = Col(t, "action_script_id", "ActionScriptID"),
                    BoxCoded = Col(t, "box_coded", "BoxCoded"),
                    BoxMoney = Col(t, "box_money", "BoxMoney"),
                    BoxText = Col(t, "box_text", "BoxText"),
                    BoxBroadcastTextId = Col(t, "box_broadcast_text_id", "BoxBroadcastTextID", "box_broadcast_text"),
                    ConditionId = Col(t, "condition_id", "ConditionID")
                };
            }
        }

        private sealed class NpcTextSchema
        {
            public ColumnMetadata Id = null!; public ColumnMetadata? PreviewText;
            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static NpcTextSchema? TryCreate(TableMetadata? t)
            {
                if (t == null)
                {
                    return null;
                }

                ColumnMetadata? id = Col(t, "ID", "id", "entry");
                if (id == null)
                {
                    return null;
                }

                ColumnMetadata? p = Col(t, "text0_0", "Text0_0", "text0", "Text0") ?? t.Columns.Where(c => c.IsTextual && c.Name.StartsWith("text0", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.OrdinalPosition).FirstOrDefault();
                return new NpcTextSchema
                {
                    Id = id,
                    PreviewText = p
                };
            }
        }

        private sealed class CreatureSourceSchema
        {
            public ColumnMetadata Entry = null!;
            public ColumnMetadata? Name;
            public ColumnMetadata MenuId = null!;
            public ColumnMetadata? NpcFlags;
            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static CreatureSourceSchema? TryCreate(TableMetadata? t)
            {
                if (t == null)
                {
                    return null;
                }

                ColumnMetadata? e = Col(t, "Entry", "entry");
                ColumnMetadata? m = Col(t, "GossipMenuId", "gossip_menu_id", "gossipMenuId");
                return e == null || m == null ? null : new CreatureSourceSchema
                {
                    Entry = e,
                    Name = Col(t, "Name", "name"),
                    MenuId = m,
                    NpcFlags = Col(t, "NpcFlags", "npcflag", "npc_flags", "npcFlags")
                };
            }
        }

        private sealed class GoSourceSchema
        {
            public ColumnMetadata Entry = null!;
            public ColumnMetadata? Name;
            public ColumnMetadata Type = null!;
            public ColumnMetadata? Data3, Data19;
            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static GoSourceSchema? TryCreate(TableMetadata? t)
            {
                if (t == null)
                {
                    return null;
                }

                ColumnMetadata? e = Col(t, "entry", "Entry");
                ColumnMetadata? ty = Col(t, "type", "Type");
                ColumnMetadata? d3 = Col(t, "data3", "Data3");
                ColumnMetadata? d19 = Col(t, "data19", "Data19");
                return e == null || ty == null || (d3 == null && d19 == null) ? null : new GoSourceSchema
                {
                    Entry = e,
                    Name = Col(t, "name", "Name"),
                    Type = ty,
                    Data3 = d3,
                    Data19 = d19
                };
            }
        }
    }
}
