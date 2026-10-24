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
    internal sealed class QuestViewerService : IQuestViewerService
    {
        private const string UnifiedRelations = "quest_relations";
        private const string CreatureStarterTable = "creature_questrelation";
        private const string CreatureEnderTable = "creature_involvedrelation";
        private const string GameObjectStarterTable = "gameobject_questrelation";
        private const string GameObjectEnderTable = "gameobject_involvedrelation";

        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the quest viewer service dependencies and state.
        public QuestViewerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Search for  using schema-aware database operations.
        public async Task<PagedResult<QuestSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;
            string entryExpr = Column("q", schema.Entry);
            string titleExpr = TextExpr("q", schema.Title);
            string where = string.IsNullOrWhiteSpace(normalizedSearch)
                ? string.Empty
                : " WHERE CAST(" + entryExpr + " AS CHAR) LIKE @search OR " + titleExpr + " LIKE @search";

            string countSql = "SELECT COUNT(*) FROM " + Qualify(questTemplate) + " AS q" + where + ";";
            string dataSql =
                "SELECT " + entryExpr + " AS QuestEntry, " + titleExpr + " AS QuestTitle, " +
                NumberExpr("q", schema.MinLevel) + " AS MinLevel, " + NumberExpr("q", schema.QuestLevel) + " AS QuestLevel, " +
                NumberZeroExpr("q", schema.PrevQuestId) + " AS PrevQuestId, " + NumberZeroExpr("q", schema.NextQuestId) + " AS NextQuestId, " +
                NumberZeroExpr("q", schema.ExclusiveGroup) + " AS ExclusiveGroup, " + NumberZeroExpr("q", schema.NextQuestInChain) + " AS NextQuestInChain " +
                "FROM " + Qualify(questTemplate) + " AS q" + where + " ORDER BY " + entryExpr + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                AddSearch(command, normalizedSearch);
                total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            List<QuestSummary> rows = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                AddSearch(command, normalizedSearch);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(ReadSummary(reader));
                }
            }

            if (rows.Count > 0)
            {
                await LoadRelationCountsAsync(connection, relationTables, rows, cancellationToken);
            }

            return new PagedResult<QuestSummary> { Items = rows, TotalCount = total, Page = page, PageSize = pageSize };
        }

        // Read  using schema-aware database operations.
        public async Task<QuestDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long entry,
            int relationPage,
            int relationPageSize,
            CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            QuestSummary? summary = await GetSummaryAsync(profile, questTemplate, relationTables, entry, cancellationToken);
            if (summary == null)
            {
                return null;
            }

            QuestTemplateValues? values = await GetQuestValuesAsync(profile, questTemplate, schema, entry, cancellationToken);
            relationPage = Math.Max(1, relationPage);
            relationPageSize = Math.Clamp(relationPageSize, 10, _options.MaximumBrowsePageSize);

            List<RelationLocation> locations = GetRelationLocations(relationTables);
            long total = await CountRelationsAsync(profile, locations, entry, includeAllUnifiedActors: false, cancellationToken);
            int totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)relationPageSize);
            if (totalPages == 0)
            {
                relationPage = 1;
            }
            else if (relationPage > totalPages)
            {
                relationPage = totalPages;
            }

            int offset = checked((relationPage - 1) * relationPageSize);

            List<QuestRelationRow> relations = new();
            List<string> unions = BuildRelationSelects(locations, creatureTemplate, gameObjectTemplate);
            if (unions.Count > 0 && total > 0)
            {
                string sql = "SELECT rel.Kind, rel.EntityType, rel.RoleLabel, rel.EntityEntry, rel.EntityName, rel.StorageTable " +
                             "FROM (" + string.Join(" UNION ALL ", unions) + ") AS rel " +
                             "ORDER BY rel.RoleLabel DESC, rel.EntityType, rel.EntityName, rel.EntityEntry " +
                             "LIMIT @limit OFFSET @offset;";
                await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
                await connection.OpenAsync(cancellationToken);
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@quest", entry);
                command.Parameters.AddWithValue("@limit", relationPageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string name = StringValue(reader, "EntityName");
                    relations.Add(new QuestRelationRow
                    {
                        Kind = StringValue(reader, "Kind"),
                        EntityType = StringValue(reader, "EntityType"),
                        RoleLabel = StringValue(reader, "RoleLabel"),
                        EntityEntry = Int64(reader, "EntityEntry"),
                        EntityName = name,
                        StorageTable = StringValue(reader, "StorageTable"),
                        IsResolved = !string.IsNullOrWhiteSpace(name)
                    });
                }
            }

            IReadOnlyList<QuestChainLink> chain = await LoadChainLinksAsync(profile, questTemplate, schema, summary, cancellationToken);
            return new QuestDetail
            {
                Quest = summary,
                Details = values?.Details ?? string.Empty,
                Objectives = values?.Objectives ?? string.Empty,
                Relations = new PagedResult<QuestRelationRow>
                {
                    Items = relations,
                    TotalCount = total,
                    Page = relationPage,
                    PageSize = relationPageSize
                },
                ChainLinks = chain
            };
        }

        // Report which editor operations the detected database schema supports.
        public QuestEditorCapabilities GetEditorCapabilities(TableMetadata questTemplate, IReadOnlyDictionary<string, TableMetadata> relationTables)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            bool safeQuestKey = questTemplate.PrimaryKeyColumns.Count > 0 && questTemplate.PrimaryKeyColumns.All(c => Is(c, schema.Entry));
            HashSet<string> supported = new(QuestWriteColumns(schema, true).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            bool canCreate = safeQuestKey && questTemplate.Columns
                .Where(c => !c.IsGenerated && !c.IsAutoIncrement)
                .All(c => supported.Contains(c.Name) || c.IsNullable || c.DefaultValue != null);

            List<QuestRelationCapability> relationKinds = GetRelationLocations(relationTables)
                .Where(location => location.IsSafeForMutation)
                .Select(location => new QuestRelationCapability
                {
                    Kind = location.Kind,
                    Label = QuestRelationKinds.EntityLabel(location.Kind) + " — " + QuestRelationKinds.RoleLabel(location.Kind),
                    StorageTable = location.Table.Name
                })
                .ToList();

            string storageLabel = relationTables.ContainsKey(UnifiedRelations)
                ? "Unified quest_relations"
                : relationKinds.Count > 0 ? "Legacy quest relation tables" : "No supported relation tables";

            return new QuestEditorCapabilities
            {
                EntryColumn = schema.Entry.Name,
                TitleColumn = schema.Title?.Name,
                DetailsColumn = schema.Details?.Name,
                ObjectivesColumn = schema.Objectives?.Name,
                MinLevelColumn = schema.MinLevel?.Name,
                QuestLevelColumn = schema.QuestLevel?.Name,
                PrevQuestIdColumn = schema.PrevQuestId?.Name,
                NextQuestIdColumn = schema.NextQuestId?.Name,
                ExclusiveGroupColumn = schema.ExclusiveGroup?.Name,
                NextQuestInChainColumn = schema.NextQuestInChain?.Name,
                RelationStorageLabel = storageLabel,
                RelationKinds = relationKinds,
                CanMutateQuests = safeQuestKey,
                CanCreateQuests = canCreate
            };
        }

        // Add quest using schema-aware database operations.
        public async Task<QuestMutationResult> AddQuestAsync(ConnectionProfile profile, TableMetadata questTemplate, QuestTemplateValues values, CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            QuestEditorCapabilities capabilities = GetEditorCapabilities(questTemplate, new Dictionary<string, TableMetadata>());
            if (!capabilities.CanCreateQuests)
            {
                throw new InvalidOperationException("This quest_template schema has required fields outside the Quest Viewer creation form, so a new quest cannot be created safely from this viewer.");
            }

            if (values.Entry <= 0)
            {
                throw new InvalidOperationException("A positive quest entry is required.");
            }

            await EnsureQuestMissingAsync(profile, questTemplate, schema, values.Entry, cancellationToken);

            List<ColumnMetadata> columns = QuestWriteColumns(schema, includeEntry: true);
            string sql = "INSERT INTO " + Qualify(questTemplate) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" +
                         string.Join(", ", columns.Select((_, i) => "@v" + i.ToString(CultureInfo.InvariantCulture))) + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddQuestValues(command, columns, schema, values, "@v");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new QuestMutationResult
            {
                AffectedRows = affected,
                ObjectName = questTemplate.Name,
                SqlPreview = BuildQuestInsertPreview(questTemplate, schema, values),
                RevertSqlPreview = "DELETE FROM " + Q(questTemplate.Name) + " WHERE " + Q(schema.Entry.Name) + " = " + values.Entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;"
            };
        }

        // Update quest using schema-aware database operations.
        public async Task<QuestMutationResult> UpdateQuestAsync(ConnectionProfile profile, TableMetadata questTemplate, long entry, QuestTemplateValues values, CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            EnsureSafeQuestMutation(questTemplate, schema);
            QuestTemplateValues? original = await GetQuestValuesAsync(profile, questTemplate, schema, entry, cancellationToken) ?? throw new InvalidOperationException("The selected quest no longer exists.");
            values.Entry = entry;
            List<ColumnMetadata> columns = QuestWriteColumns(schema, includeEntry: false);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("This quest_template does not expose editable Quest Viewer fields.");
            }

            string sql = "UPDATE " + Qualify(questTemplate) + " SET " + string.Join(", ", columns.Select((c, i) => Q(c.Name) + "=@v" + i.ToString(CultureInfo.InvariantCulture))) +
                         " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddQuestValues(command, columns, schema, values, "@v");
            command.Parameters.AddWithValue("@entry", entry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new QuestMutationResult
            {
                AffectedRows = affected,
                ObjectName = questTemplate.Name,
                SqlPreview = BuildQuestUpdatePreview(questTemplate, schema, values),
                RevertSqlPreview = BuildQuestUpdatePreview(questTemplate, schema, original)
            };
        }

        // Delete quest using schema-aware database operations.
        public async Task<QuestMutationResult> DeleteQuestAsync(ConnectionProfile profile, TableMetadata questTemplate, IReadOnlyDictionary<string, TableMetadata> relationTables, long entry, CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            EnsureSafeQuestMutation(questTemplate, schema);
            Dictionary<string, object?>? original = await GetFullRowAsync(profile, questTemplate, schema, entry, cancellationToken) ?? throw new InvalidOperationException("The selected quest no longer exists.");
            List<RelationLocation> locations = GetRelationLocations(relationTables);
            long relations = await CountRelationsAsync(profile, locations, entry, includeAllUnifiedActors: true, cancellationToken);
            if (relations > 0)
            {
                throw new InvalidOperationException("Remove all quest source/ender relationships before deleting the quest. Quest Viewer does not cascade-delete relationship rows.");
            }

            long incoming = await CountIncomingChainReferencesAsync(profile, questTemplate, schema, entry, cancellationToken);
            if (incoming > 0)
            {
                throw new InvalidOperationException("Other quests still reference this quest in chain fields. Remove those chain references before deleting it.");
            }

            string sql = "DELETE FROM " + Qualify(questTemplate) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new QuestMutationResult
            {
                AffectedRows = affected,
                ObjectName = questTemplate.Name,
                SqlPreview = "DELETE FROM " + Q(questTemplate.Name) + " WHERE " + Q(schema.Entry.Name) + " = " + entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;",
                RevertSqlPreview = BuildFullInsertPreview(questTemplate, original)
            };
        }

        // Add relation using schema-aware database operations.
        public async Task<QuestMutationResult> AddRelationAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long questEntry,
            QuestRelationValues values,
            CancellationToken cancellationToken)
        {
            QuestSchema questSchema = QuestSchema.Create(questTemplate);
            await EnsureQuestExistsAsync(profile, questTemplate, questSchema, questEntry, cancellationToken);
            values.Kind = QuestRelationKinds.Normalize(values.Kind);
            if (values.EntityEntry <= 0)
            {
                throw new InvalidOperationException("A positive creature or GameObject template entry is required.");
            }

            await EnsureEntityExistsAsync(profile, values.Kind, values.EntityEntry, creatureTemplate, gameObjectTemplate, cancellationToken);
            RelationLocation location = ResolveRelationLocation(relationTables, values.Kind);
            if (!location.IsSafeForMutation)
            {
                throw new InvalidOperationException(location.Table.Name + " does not expose a safe quest relationship key.");
            }

            await EnsureRelationMissingAsync(profile, location, questEntry, values.EntityEntry, cancellationToken);
            (string sql, Action<MySqlCommand> bind) = BuildRelationInsert(location, questEntry, values.EntityEntry);
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            bind(command);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new QuestMutationResult
            {
                AffectedRows = affected,
                ObjectName = location.Table.Name,
                SqlPreview = BuildRelationInsertPreview(location, questEntry, values.EntityEntry),
                RevertSqlPreview = BuildRelationDeletePreview(location, questEntry, values.EntityEntry)
            };
        }

        // Update relation using schema-aware database operations.
        public async Task<QuestMutationResult> UpdateRelationAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long questEntry,
            QuestRelationValues original,
            QuestRelationValues replacement,
            CancellationToken cancellationToken)
        {
            QuestSchema questSchema = QuestSchema.Create(questTemplate);
            await EnsureQuestExistsAsync(profile, questTemplate, questSchema, questEntry, cancellationToken);
            original.Kind = QuestRelationKinds.Normalize(original.Kind);
            replacement.Kind = QuestRelationKinds.Normalize(replacement.Kind);
            if (replacement.EntityEntry <= 0)
            {
                throw new InvalidOperationException("A positive creature or GameObject template entry is required.");
            }

            await EnsureEntityExistsAsync(profile, replacement.Kind, replacement.EntityEntry, creatureTemplate, gameObjectTemplate, cancellationToken);
            RelationLocation oldLocation = ResolveRelationLocation(relationTables, original.Kind);
            RelationLocation newLocation = ResolveRelationLocation(relationTables, replacement.Kind);
            if (!oldLocation.IsSafeForMutation || !newLocation.IsSafeForMutation)
            {
                throw new InvalidOperationException("One of the selected quest relationship tables does not expose a safe mutation key.");
            }

            if (!await RelationExistsAsync(profile, oldLocation, questEntry, original.EntityEntry, cancellationToken))
            {
                throw new InvalidOperationException("The selected quest relationship no longer exists.");
            }

            if (string.Equals(original.Kind, replacement.Kind, StringComparison.OrdinalIgnoreCase) && original.EntityEntry == replacement.EntityEntry)
            {
                throw new InvalidOperationException("The quest relationship was not changed.");
            }

            await EnsureRelationMissingAsync(profile, newLocation, questEntry, replacement.EntityEntry, cancellationToken, oldLocation, original.EntityEntry);

            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                (string deleteSql, Action<MySqlCommand> bindDelete) = BuildRelationDelete(oldLocation, questEntry, original.EntityEntry);
                await using (MySqlCommand delete = new(deleteSql, connection, transaction))
                {
                    bindDelete(delete);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }

                (string insertSql, Action<MySqlCommand> bindInsert) = BuildRelationInsert(newLocation, questEntry, replacement.EntityEntry);
                await using (MySqlCommand insert = new(insertSql, connection, transaction))
                {
                    bindInsert(insert);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return new QuestMutationResult
            {
                AffectedRows = 2,
                ObjectName = oldLocation.Table.Name == newLocation.Table.Name ? oldLocation.Table.Name : oldLocation.Table.Name + " / " + newLocation.Table.Name,
                SqlPreview = BuildRelationDeletePreview(oldLocation, questEntry, original.EntityEntry) + "\n" + BuildRelationInsertPreview(newLocation, questEntry, replacement.EntityEntry),
                RevertSqlPreview = BuildRelationDeletePreview(newLocation, questEntry, replacement.EntityEntry) + "\n" + BuildRelationInsertPreview(oldLocation, questEntry, original.EntityEntry)
            };
        }

        // Remove relation using schema-aware database operations.
        public async Task<QuestMutationResult> RemoveRelationAsync(ConnectionProfile profile, IReadOnlyDictionary<string, TableMetadata> relationTables, long questEntry, QuestRelationValues values, CancellationToken cancellationToken)
        {
            values.Kind = QuestRelationKinds.Normalize(values.Kind);
            RelationLocation location = ResolveRelationLocation(relationTables, values.Kind);
            if (!location.IsSafeForMutation)
            {
                throw new InvalidOperationException(location.Table.Name + " does not expose a safe quest relationship key.");
            }

            if (!await RelationExistsAsync(profile, location, questEntry, values.EntityEntry, cancellationToken))
            {
                throw new InvalidOperationException("The selected quest relationship no longer exists.");
            }

            (string sql, Action<MySqlCommand> bind) = BuildRelationDelete(location, questEntry, values.EntityEntry);
            await using MySqlConnection connection = _connectionFactory.Create(profile, location.Table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            bind(command);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new QuestMutationResult
            {
                AffectedRows = affected,
                ObjectName = location.Table.Name,
                SqlPreview = BuildRelationDeletePreview(location, questEntry, values.EntityEntry),
                RevertSqlPreview = BuildRelationInsertPreview(location, questEntry, values.EntityEntry)
            };
        }

        // Read summary using schema-aware database operations.
        private async Task<QuestSummary?> GetSummaryAsync(ConnectionProfile profile, TableMetadata questTemplate, IReadOnlyDictionary<string, TableMetadata> relationTables, long entry, CancellationToken cancellationToken)
        {
            QuestSchema schema = QuestSchema.Create(questTemplate);
            string sql = "SELECT " + Column("q", schema.Entry) + " AS QuestEntry, " + TextExpr("q", schema.Title) + " AS QuestTitle, " +
                         NumberExpr("q", schema.MinLevel) + " AS MinLevel, " + NumberExpr("q", schema.QuestLevel) + " AS QuestLevel, " +
                         NumberZeroExpr("q", schema.PrevQuestId) + " AS PrevQuestId, " + NumberZeroExpr("q", schema.NextQuestId) + " AS NextQuestId, " +
                         NumberZeroExpr("q", schema.ExclusiveGroup) + " AS ExclusiveGroup, " + NumberZeroExpr("q", schema.NextQuestInChain) + " AS NextQuestInChain " +
                         "FROM " + Qualify(questTemplate) + " AS q WHERE " + Column("q", schema.Entry) + "=@entry LIMIT 1;";
            QuestSummary? summary = null;
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using (MySqlCommand command = new(sql, connection))
            {
                command.Parameters.AddWithValue("@entry", entry);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    summary = ReadSummary(reader);
                }
            }

            if (summary != null)
            {
                await LoadRelationCountsAsync(connection, relationTables, new List<QuestSummary> { summary }, cancellationToken);
            }

            return summary;
        }

        // Load relation counts using schema-aware database operations.
        private static async Task LoadRelationCountsAsync(MySqlConnection connection, IReadOnlyDictionary<string, TableMetadata> relationTables, List<QuestSummary> rows, CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return;
            }

            Dictionary<long, QuestSummary> byEntry = rows.ToDictionary(row => row.Entry);
            string inClause = string.Join(",", rows.Select((_, i) => "@q" + i.ToString(CultureInfo.InvariantCulture)));
            if (relationTables.TryGetValue(UnifiedRelations, out TableMetadata? unified))
            {
                UnifiedRelationSchema? schema = UnifiedRelationSchema.TryCreate(unified);
                if (schema != null)
                {
                    string sql = "SELECT " + Q(schema.Quest.Name) + " AS QuestEntry, " + Q(schema.Actor.Name) + " AS ActorValue, " + Q(schema.Role.Name) + " AS RoleValue, COUNT(*) AS RelationCount " +
                                 "FROM " + Qualify(unified) + " WHERE " + Q(schema.Quest.Name) + " IN (" + inClause + ") AND " + Q(schema.Actor.Name) + " IN (0,1) AND " + Q(schema.Role.Name) + " IN (0,1) " +
                                 "GROUP BY " + Q(schema.Quest.Name) + ", " + Q(schema.Actor.Name) + ", " + Q(schema.Role.Name) + ";";
                    await using MySqlCommand command = new(sql, connection);
                    AddQuestIdParameters(command, rows);
                    await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long quest = Int64(reader, "QuestEntry");
                        if (!byEntry.TryGetValue(quest, out QuestSummary? row))
                        {
                            continue;
                        }

                        long actor = Int64(reader, "ActorValue");
                        long role = Int64(reader, "RoleValue");
                        long count = Int64(reader, "RelationCount");
                        ApplyRelationCount(row, actor == 0 ? (role == 0 ? QuestRelationKinds.CreatureStarter : QuestRelationKinds.CreatureEnder) : (role == 0 ? QuestRelationKinds.GameObjectStarter : QuestRelationKinds.GameObjectEnder), count);
                    }

                    return;
                }
            }

            foreach (RelationLocation location in GetRelationLocations(relationTables))
            {
                if (location.IsUnified)
                {
                    continue;
                }

                string sql = "SELECT " + Q(location.Quest.Name) + " AS QuestEntry, COUNT(*) AS RelationCount FROM " + Qualify(location.Table) +
                             " WHERE " + Q(location.Quest.Name) + " IN (" + inClause + ") GROUP BY " + Q(location.Quest.Name) + ";";
                await using MySqlCommand command = new(sql, connection);
                AddQuestIdParameters(command, rows);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    long quest = Int64(reader, "QuestEntry");
                    if (byEntry.TryGetValue(quest, out QuestSummary? row))
                    {
                        ApplyRelationCount(row, location.Kind, Int64(reader, "RelationCount"));
                    }
                }
            }
        }

        // Add quest id parameters using schema-aware database operations.
        private static void AddQuestIdParameters(MySqlCommand command, List<QuestSummary> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                command.Parameters.AddWithValue("@q" + i.ToString(CultureInfo.InvariantCulture), rows[i].Entry);
            }
        }

        // Apply relation count using schema-aware database operations.
        private static void ApplyRelationCount(QuestSummary row, string kind, long count)
        {
            switch (kind)
            {
                case QuestRelationKinds.CreatureStarter:
                    row.CreatureStarterCount += count;
                    break;
                case QuestRelationKinds.CreatureEnder:
                    row.CreatureEnderCount += count;
                    break;
                case QuestRelationKinds.GameObjectStarter:
                    row.GameObjectStarterCount += count;
                    break;
                case QuestRelationKinds.GameObjectEnder:
                    row.GameObjectEnderCount += count;
                    break;
            }
        }

        // Build relation selects using schema-aware database operations.
        private static List<string> BuildRelationSelects(IReadOnlyList<RelationLocation> locations, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate)
        {
            List<string> result = new();
            foreach (RelationLocation location in locations)
            {
                TableMetadata? template = QuestRelationKinds.EntityLabel(location.Kind) == "Creature" ? creatureTemplate : gameObjectTemplate;
                ColumnMetadata? templateEntry = Find(template, "entry", "Entry", "id", "Id");
                ColumnMetadata? templateName = Find(template, "name", "Name");
                string joins = string.Empty;
                string name = "''";
                if (template != null && templateEntry != null)
                {
                    joins = " LEFT JOIN " + Qualify(template) + " AS t ON " + Column("t", templateEntry) + "=" + Column("r", location.Entity);
                    if (templateName != null)
                    {
                        name = "COALESCE(CAST(" + Column("t", templateName) + " AS CHAR), '')";
                    }
                }

                string where = Column("r", location.Quest) + "=@quest";
                if (location.IsUnified)
                {
                    where += " AND " + Column("r", location.Actor!) + "=" + location.ActorValue.ToString(CultureInfo.InvariantCulture) +
                             " AND " + Column("r", location.Role!) + "=" + location.RoleValue.ToString(CultureInfo.InvariantCulture);
                }

                result.Add("SELECT " + QuoteSql(location.Kind) + " AS Kind, " + QuoteSql(QuestRelationKinds.EntityLabel(location.Kind)) + " AS EntityType, " +
                           QuoteSql(QuestRelationKinds.RoleLabel(location.Kind)) + " AS RoleLabel, " + Column("r", location.Entity) + " AS EntityEntry, " +
                           name + " AS EntityName, " + QuoteSql(location.Table.Name) + " AS StorageTable FROM " + Qualify(location.Table) + " AS r" + joins + " WHERE " + where);
            }

            return result;
        }

        // Load chain links using schema-aware database operations.
        private async Task<IReadOnlyList<QuestChainLink>> LoadChainLinksAsync(ConnectionProfile profile, TableMetadata questTemplate, QuestSchema schema, QuestSummary selected, CancellationToken cancellationToken)
        {
            List<QuestChainLink> links = new();
            AddDirectChain(links, selected.PrevQuestId, "Previous quest reference", "PrevQuestId");
            AddDirectChain(links, selected.NextQuestId, "Next quest reference", "NextQuestId");
            AddDirectChain(links, selected.NextQuestInChain, "Next quest in chain", "NextQuestInChain");

            HashSet<long> directIds = links.Where(l => l.Entry > 0).Select(l => l.Entry).ToHashSet();
            Dictionary<long, string> directTitles = await LoadTitlesAsync(profile, questTemplate, schema, directIds, cancellationToken);
            foreach (QuestChainLink link in links)
            {
                link.Exists = directTitles.TryGetValue(link.Entry, out string? title);
                link.Title = title ?? string.Empty;
            }

            List<string> conditions = new();
            if (schema.PrevQuestId != null)
            {
                conditions.Add("ABS(" + Q(schema.PrevQuestId.Name) + ")=@entry");
            }

            if (schema.NextQuestId != null)
            {
                conditions.Add("ABS(" + Q(schema.NextQuestId.Name) + ")=@entry");
            }

            if (schema.NextQuestInChain != null)
            {
                conditions.Add(Q(schema.NextQuestInChain.Name) + "=@entry");
            }

            if (schema.ExclusiveGroup != null && selected.ExclusiveGroup != 0)
            {
                conditions.Add(Q(schema.ExclusiveGroup.Name) + "=@group");
            }

            if (conditions.Count == 0)
            {
                return links;
            }

            string sql = "SELECT " + Q(schema.Entry.Name) + " AS QuestEntry, " + PlainTextExpr(schema.Title) + " AS QuestTitle, " +
                         PlainNumberZeroExpr(schema.PrevQuestId) + " AS PrevQuestId, " + PlainNumberZeroExpr(schema.NextQuestId) + " AS NextQuestId, " +
                         PlainNumberZeroExpr(schema.ExclusiveGroup) + " AS ExclusiveGroup, " + PlainNumberZeroExpr(schema.NextQuestInChain) + " AS NextQuestInChain " +
                         "FROM " + Qualify(questTemplate) + " WHERE " + Q(schema.Entry.Name) + "<>@entry AND (" + string.Join(" OR ", conditions) + ") ORDER BY " + Q(schema.Entry.Name) + " LIMIT 200;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, questTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", selected.Entry);
            if (schema.ExclusiveGroup != null && selected.ExclusiveGroup != 0)
            {
                command.Parameters.AddWithValue("@group", selected.ExclusiveGroup);
            }

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long other = Int64(reader, "QuestEntry");
                string title = StringValue(reader, "QuestTitle");
                long prev = Int64(reader, "PrevQuestId");
                long next = Int64(reader, "NextQuestId");
                long group = Int64(reader, "ExclusiveGroup");
                long nextChain = Int64(reader, "NextQuestInChain");
                if (Math.Abs(prev) == selected.Entry)
                {
                    links.Add(Incoming(other, title, "References this quest as previous", "PrevQuestId", prev));
                }

                if (Math.Abs(next) == selected.Entry)
                {
                    links.Add(Incoming(other, title, "References this quest as next", "NextQuestId", next));
                }

                if (nextChain == selected.Entry)
                {
                    links.Add(Incoming(other, title, "Chains into this quest", "NextQuestInChain", nextChain));
                }

                if (selected.ExclusiveGroup != 0 && group == selected.ExclusiveGroup)
                {
                    links.Add(Incoming(other, title, "Exclusive-group peer", "ExclusiveGroup", group));
                }
            }

            return links;
        }

        // Add direct chain using schema-aware database operations.
        private static void AddDirectChain(List<QuestChainLink> links, long rawValue, string label, string code)
        {
            if (rawValue == 0)
            {
                return;
            }

            long entry = Math.Abs(rawValue);
            links.Add(new QuestChainLink { Entry = entry, RelationLabel = label, RelationCode = code, RawValue = rawValue, IsIncoming = false });
        }

        // Handle incoming within the quest viewer service workflow.
        private static QuestChainLink Incoming(long entry, string title, string label, string code, long rawValue) => new()
        {
            Entry = entry,
            Title = title,
            RelationLabel = label,
            RelationCode = code,
            RawValue = rawValue,
            Exists = true,
            IsIncoming = true
        };

        // Load titles using schema-aware database operations.
        private async Task<Dictionary<long, string>> LoadTitlesAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, HashSet<long> ids, CancellationToken cancellationToken)
        {
            Dictionary<long, string> result = new();
            if (ids.Count == 0)
            {
                return result;
            }

            List<long> values = ids.ToList();
            string placeholders = string.Join(",", values.Select((_, i) => "@id" + i.ToString(CultureInfo.InvariantCulture)));
            string sql = "SELECT " + Q(schema.Entry.Name) + " AS QuestEntry, " + PlainTextExpr(schema.Title) + " AS QuestTitle FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + " IN (" + placeholders + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            for (int i = 0; i < values.Count; i++)
            {
                command.Parameters.AddWithValue("@id" + i.ToString(CultureInfo.InvariantCulture), values[i]);
            }

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[Int64(reader, "QuestEntry")] = StringValue(reader, "QuestTitle");
            }

            return result;
        }

        // Read quest values using schema-aware database operations.
        private async Task<QuestTemplateValues?> GetQuestValuesAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, long entry, CancellationToken cancellationToken)
        {
            string sql = "SELECT " + Q(schema.Entry.Name) + " AS QuestEntry, " + PlainTextExpr(schema.Title) + " AS QuestTitle, " + PlainTextExpr(schema.Details) + " AS QuestDetails, " + PlainTextExpr(schema.Objectives) + " AS QuestObjectives, " +
                         PlainNumberExpr(schema.MinLevel) + " AS MinLevel, " + PlainNumberExpr(schema.QuestLevel) + " AS QuestLevel, " + PlainNumberExpr(schema.PrevQuestId) + " AS PrevQuestId, " +
                         PlainNumberExpr(schema.NextQuestId) + " AS NextQuestId, " + PlainNumberExpr(schema.ExclusiveGroup) + " AS ExclusiveGroup, " + PlainNumberExpr(schema.NextQuestInChain) + " AS NextQuestInChain " +
                         "FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new QuestTemplateValues
            {
                Entry = Int64(reader, "QuestEntry"),
                Title = StringValue(reader, "QuestTitle"),
                Details = StringValue(reader, "QuestDetails"),
                Objectives = StringValue(reader, "QuestObjectives"),
                MinLevel = NullableInt64(reader, "MinLevel"),
                QuestLevel = NullableInt64(reader, "QuestLevel"),
                PrevQuestId = NullableInt64(reader, "PrevQuestId"),
                NextQuestId = NullableInt64(reader, "NextQuestId"),
                ExclusiveGroup = NullableInt64(reader, "ExclusiveGroup"),
                NextQuestInChain = NullableInt64(reader, "NextQuestInChain")
            };
        }

        // Read full row using schema-aware database operations.
        private async Task<Dictionary<string, object?>?> GetFullRowAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, long entry, CancellationToken cancellationToken)
        {
            string sql = "SELECT * FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            return row;
        }

        // Count incoming chain references using schema-aware database operations.
        private async Task<long> CountIncomingChainReferencesAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, long entry, CancellationToken cancellationToken)
        {
            List<string> conditions = new();
            if (schema.PrevQuestId != null)
            {
                conditions.Add("ABS(" + Q(schema.PrevQuestId.Name) + ")=@entry");
            }

            if (schema.NextQuestId != null)
            {
                conditions.Add("ABS(" + Q(schema.NextQuestId.Name) + ")=@entry");
            }

            if (schema.NextQuestInChain != null)
            {
                conditions.Add(Q(schema.NextQuestInChain.Name) + "=@entry");
            }

            if (conditions.Count == 0)
            {
                return 0;
            }

            string sql = "SELECT COUNT(*) FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + "<>@entry AND (" + string.Join(" OR ", conditions) + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
        }

        // Count relations using schema-aware database operations.
        private async Task<long> CountRelationsAsync(ConnectionProfile profile, IReadOnlyList<RelationLocation> locations, long questEntry, bool includeAllUnifiedActors, CancellationToken cancellationToken)
        {
            if (locations.Count == 0)
            {
                return 0;
            }

            RelationLocation? unified = locations.FirstOrDefault(l => l.IsUnified);
            if (unified != null)
            {
                UnifiedRelationSchema schema = UnifiedRelationSchema.Create(unified.Table);
                string filter = includeAllUnifiedActors ? string.Empty : " AND " + Q(schema.Actor.Name) + " IN (0,1) AND " + Q(schema.Role.Name) + " IN (0,1)";
                string sql = "SELECT COUNT(*) FROM " + Qualify(unified.Table) + " WHERE " + Q(schema.Quest.Name) + "=@quest" + filter + ";";
                await using MySqlConnection connection = _connectionFactory.Create(profile, unified.Table.DatabaseName);
                await connection.OpenAsync(cancellationToken);
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@quest", questEntry);
                return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            long total = 0;
            foreach (RelationLocation location in locations)
            {
                string sql = "SELECT COUNT(*) FROM " + Qualify(location.Table) + " WHERE " + Q(location.Quest.Name) + "=@quest;";
                await using MySqlConnection connection = _connectionFactory.Create(profile, location.Table.DatabaseName);
                await connection.OpenAsync(cancellationToken);
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@quest", questEntry);
                total += Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            return total;
        }

        // Ensure quest exists using schema-aware database operations.
        private async Task EnsureQuestExistsAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, long entry, CancellationToken cancellationToken)
        {
            string sql = "SELECT 1 FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            if (await command.ExecuteScalarAsync(cancellationToken) == null)
            {
                throw new InvalidOperationException("Quest " + entry.ToString(CultureInfo.InvariantCulture) + " does not exist.");
            }
        }

        // Ensure quest missing using schema-aware database operations.
        private async Task EnsureQuestMissingAsync(ConnectionProfile profile, TableMetadata table, QuestSchema schema, long entry, CancellationToken cancellationToken)
        {
            string sql = "SELECT 1 FROM " + Qualify(table) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            if (await command.ExecuteScalarAsync(cancellationToken) != null)
            {
                throw new InvalidOperationException("Quest " + entry.ToString(CultureInfo.InvariantCulture) + " already exists.");
            }
        }

        // Ensure entity exists using schema-aware database operations.
        private async Task EnsureEntityExistsAsync(ConnectionProfile profile, string kind, long entityEntry, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, CancellationToken cancellationToken)
        {
            bool creature = QuestRelationKinds.EntityLabel(kind) == "Creature";
            TableMetadata? table = (creature ? creatureTemplate : gameObjectTemplate) ?? throw new InvalidOperationException((creature ? "creature_template" : "gameobject_template") + " is required to validate this relationship.");
            ColumnMetadata? entry = Find(table, "entry", "Entry", "id", "Id") ?? throw new InvalidOperationException(table.Name + " does not expose a recognizable entry column.");
            string sql = "SELECT 1 FROM " + Qualify(table) + " WHERE " + Q(entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entityEntry);
            if (await command.ExecuteScalarAsync(cancellationToken) == null)
            {
                throw new InvalidOperationException((creature ? "Creature" : "GameObject") + " template entry " + entityEntry.ToString(CultureInfo.InvariantCulture) + " was not found.");
            }
        }

        // Handle relation exists async within the quest viewer service workflow.
        private async Task<bool> RelationExistsAsync(ConnectionProfile profile, RelationLocation location, long questEntry, long entityEntry, CancellationToken cancellationToken)
        {
            (string sql, Action<MySqlCommand> bind) = BuildRelationExists(location, questEntry, entityEntry);
            await using MySqlConnection connection = _connectionFactory.Create(profile, location.Table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            bind(command);
            return await command.ExecuteScalarAsync(cancellationToken) != null;
        }

        // Ensure relation missing using schema-aware database operations.
        private async Task EnsureRelationMissingAsync(ConnectionProfile profile, RelationLocation location, long questEntry, long entityEntry, CancellationToken cancellationToken, RelationLocation? originalLocation = null, long originalEntry = 0)
        {
            if (location.IsUnified && !location.Table.PrimaryKeyColumns.Any(c => Is(c, location.Actor!)))
            {
                UnifiedRelationSchema schema = UnifiedRelationSchema.Create(location.Table);
                string sql = "SELECT " + Q(schema.Actor.Name) + " FROM " + Qualify(location.Table) + " WHERE " + Q(schema.Entity.Name) + "=@entity AND " + Q(schema.Quest.Name) + "=@quest AND " + Q(schema.Role.Name) + "=@role LIMIT 1;";
                await using MySqlConnection connection = _connectionFactory.Create(profile, location.Table.DatabaseName);
                await connection.OpenAsync(cancellationToken);
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@entity", entityEntry);
                command.Parameters.AddWithValue("@quest", questEntry);
                command.Parameters.AddWithValue("@role", location.RoleValue);
                object? existingActor = await command.ExecuteScalarAsync(cancellationToken);
                if (existingActor != null)
                {
                    bool isOriginal = originalLocation?.IsUnified is true && originalEntry == entityEntry && originalLocation.RoleValue == location.RoleValue && Convert.ToInt64(existingActor, CultureInfo.InvariantCulture) == originalLocation.ActorValue;
                    if (!isOriginal)
                    {
                        throw new InvalidOperationException("quest_relations already contains this entry/quest/role key. This schema's primary key does not include actor, so a creature and GameObject with the same entry cannot share that quest role.");
                    }
                }

                return;
            }

            if (await RelationExistsAsync(profile, location, questEntry, entityEntry, cancellationToken))
            {
                bool isOriginal = originalLocation != null && string.Equals(originalLocation.Kind, location.Kind, StringComparison.OrdinalIgnoreCase) && originalEntry == entityEntry;
                if (!isOriginal)
                {
                    throw new InvalidOperationException("That quest relationship already exists.");
                }
            }
        }

        // Build relation exists using schema-aware database operations.
        private static (string Sql, Action<MySqlCommand> Bind) BuildRelationExists(RelationLocation location, long questEntry, long entityEntry)
        {
            string where = Q(location.Entity.Name) + "=@entity AND " + Q(location.Quest.Name) + "=@quest";
            if (location.IsUnified)
            {
                where += " AND " + Q(location.Actor!.Name) + "=@actor AND " + Q(location.Role!.Name) + "=@role";
            }

            return ("SELECT 1 FROM " + Qualify(location.Table) + " WHERE " + where + " LIMIT 1;", command => BindRelation(command, location, questEntry, entityEntry));
        }

        // Build relation insert using schema-aware database operations.
        private static (string Sql, Action<MySqlCommand> Bind) BuildRelationInsert(RelationLocation location, long questEntry, long entityEntry)
        {
            if (location.IsUnified)
            {
                string sql = "INSERT INTO " + Qualify(location.Table) + " (" + Q(location.Actor!.Name) + ", " + Q(location.Entity.Name) + ", " + Q(location.Quest.Name) + ", " + Q(location.Role!.Name) + ") VALUES (@actor, @entity, @quest, @role);";
                return (sql, command => BindRelation(command, location, questEntry, entityEntry));
            }

            string legacy = "INSERT INTO " + Qualify(location.Table) + " (" + Q(location.Entity.Name) + ", " + Q(location.Quest.Name) + ") VALUES (@entity, @quest);";
            return (legacy, command => BindRelation(command, location, questEntry, entityEntry));
        }

        // Build relation delete using schema-aware database operations.
        private static (string Sql, Action<MySqlCommand> Bind) BuildRelationDelete(RelationLocation location, long questEntry, long entityEntry)
        {
            string where = Q(location.Entity.Name) + "=@entity AND " + Q(location.Quest.Name) + "=@quest";
            if (location.IsUnified)
            {
                where += " AND " + Q(location.Actor!.Name) + "=@actor AND " + Q(location.Role!.Name) + "=@role";
            }

            return ("DELETE FROM " + Qualify(location.Table) + " WHERE " + where + " LIMIT 1;", command => BindRelation(command, location, questEntry, entityEntry));
        }

        // Bind relation using schema-aware database operations.
        private static void BindRelation(MySqlCommand command, RelationLocation location, long questEntry, long entityEntry)
        {
            command.Parameters.AddWithValue("@entity", entityEntry);
            command.Parameters.AddWithValue("@quest", questEntry);
            if (location.IsUnified)
            {
                command.Parameters.AddWithValue("@actor", location.ActorValue);
                command.Parameters.AddWithValue("@role", location.RoleValue);
            }
        }

        // Build relation insert preview using schema-aware database operations.
        private static string BuildRelationInsertPreview(RelationLocation location, long questEntry, long entityEntry)
        {
            if (location.IsUnified)
            {
                return "INSERT INTO " + Q(location.Table.Name) + " (" + Q(location.Actor!.Name) + ", " + Q(location.Entity.Name) + ", " + Q(location.Quest.Name) + ", " + Q(location.Role!.Name) + ") VALUES (" +
                       location.ActorValue.ToString(CultureInfo.InvariantCulture) + ", " + entityEntry.ToString(CultureInfo.InvariantCulture) + ", " + questEntry.ToString(CultureInfo.InvariantCulture) + ", " + location.RoleValue.ToString(CultureInfo.InvariantCulture) + ");";
            }

            return "INSERT INTO " + Q(location.Table.Name) + " (" + Q(location.Entity.Name) + ", " + Q(location.Quest.Name) + ") VALUES (" + entityEntry.ToString(CultureInfo.InvariantCulture) + ", " + questEntry.ToString(CultureInfo.InvariantCulture) + ");";
        }

        // Build relation delete preview using schema-aware database operations.
        private static string BuildRelationDeletePreview(RelationLocation location, long questEntry, long entityEntry)
        {
            string where = Q(location.Entity.Name) + " = " + entityEntry.ToString(CultureInfo.InvariantCulture) + " AND " + Q(location.Quest.Name) + " = " + questEntry.ToString(CultureInfo.InvariantCulture);
            if (location.IsUnified)
            {
                where += " AND " + Q(location.Actor!.Name) + " = " + location.ActorValue.ToString(CultureInfo.InvariantCulture) + " AND " + Q(location.Role!.Name) + " = " + location.RoleValue.ToString(CultureInfo.InvariantCulture);
            }

            return "DELETE FROM " + Q(location.Table.Name) + " WHERE " + where + " LIMIT 1;";
        }

        // Read relation locations using schema-aware database operations.
        private static List<RelationLocation> GetRelationLocations(IReadOnlyDictionary<string, TableMetadata> tables)
        {
            List<RelationLocation> result = new();
            if (tables.TryGetValue(UnifiedRelations, out TableMetadata? unified))
            {
                UnifiedRelationSchema? schema = UnifiedRelationSchema.TryCreate(unified);
                if (schema != null)
                {
                    result.Add(RelationLocation.Unified(unified, schema, QuestRelationKinds.CreatureStarter, 0, 0));
                    result.Add(RelationLocation.Unified(unified, schema, QuestRelationKinds.CreatureEnder, 0, 1));
                    result.Add(RelationLocation.Unified(unified, schema, QuestRelationKinds.GameObjectStarter, 1, 0));
                    result.Add(RelationLocation.Unified(unified, schema, QuestRelationKinds.GameObjectEnder, 1, 1));
                    return result;
                }
            }

            AddLegacy(result, tables, CreatureStarterTable, QuestRelationKinds.CreatureStarter);
            AddLegacy(result, tables, CreatureEnderTable, QuestRelationKinds.CreatureEnder);
            AddLegacy(result, tables, GameObjectStarterTable, QuestRelationKinds.GameObjectStarter);
            AddLegacy(result, tables, GameObjectEnderTable, QuestRelationKinds.GameObjectEnder);
            return result;
        }

        // Add legacy using schema-aware database operations.
        private static void AddLegacy(ICollection<RelationLocation> result, IReadOnlyDictionary<string, TableMetadata> tables, string name, string kind)
        {
            if (!tables.TryGetValue(name, out TableMetadata? table))
            {
                return;
            }

            LegacyRelationSchema? schema = LegacyRelationSchema.TryCreate(table);
            if (schema != null)
            {
                result.Add(RelationLocation.Legacy(table, schema, kind));
            }
        }

        // Resolve relation location using schema-aware database operations.
        private static RelationLocation ResolveRelationLocation(IReadOnlyDictionary<string, TableMetadata> tables, string kind)
        {
            kind = QuestRelationKinds.Normalize(kind);
            RelationLocation? location = GetRelationLocations(tables).FirstOrDefault(l => string.Equals(l.Kind, kind, StringComparison.OrdinalIgnoreCase));
            return location ?? throw new InvalidOperationException("The database does not expose the table needed for that quest relationship type.");
        }

        // Read summary using schema-aware database operations.
        private static QuestSummary ReadSummary(DbDataReader reader) => new()
        {
            Entry = Int64(reader, "QuestEntry"),
            Title = StringValue(reader, "QuestTitle"),
            MinLevel = NullableInt64(reader, "MinLevel"),
            QuestLevel = NullableInt64(reader, "QuestLevel"),
            PrevQuestId = Int64(reader, "PrevQuestId"),
            NextQuestId = Int64(reader, "NextQuestId"),
            ExclusiveGroup = Int64(reader, "ExclusiveGroup"),
            NextQuestInChain = Int64(reader, "NextQuestInChain")
        };

        // Ensure safe quest mutation using schema-aware database operations.
        private static void EnsureSafeQuestMutation(TableMetadata table, QuestSchema schema)
        {
            if (table.PrimaryKeyColumns.Count == 0 || table.PrimaryKeyColumns.Any(c => !Is(c, schema.Entry)))
            {
                throw new InvalidOperationException("Quest Viewer requires quest_template to be keyed by its quest entry column for safe mutations.");
            }
        }

        // Handle quest write columns within the quest viewer service workflow.
        private static List<ColumnMetadata> QuestWriteColumns(QuestSchema schema, bool includeEntry)
        {
            List<ColumnMetadata> columns = new();
            if (includeEntry)
            {
                columns.Add(schema.Entry);
            }

            Add(columns, schema.Title);
            Add(columns, schema.Details);
            Add(columns, schema.Objectives);
            Add(columns, schema.MinLevel);
            Add(columns, schema.QuestLevel);
            Add(columns, schema.PrevQuestId);
            Add(columns, schema.NextQuestId);
            Add(columns, schema.ExclusiveGroup);
            Add(columns, schema.NextQuestInChain);
            return columns.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Handle add within the quest viewer service workflow.
        private static void Add(List<ColumnMetadata> columns, ColumnMetadata? column)
        {
            if (column != null)
            {
                columns.Add(column);
            }
        }

        // Add quest values using schema-aware database operations.
        private static void AddQuestValues(MySqlCommand command, List<ColumnMetadata> columns, QuestSchema schema, QuestTemplateValues values, string prefix)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                command.Parameters.AddWithValue(prefix + i.ToString(CultureInfo.InvariantCulture), QuestValue(schema, values, columns[i]) ?? DBNull.Value);
            }
        }

        // Handle quest value within the quest viewer service workflow.
        private static object? QuestValue(QuestSchema schema, QuestTemplateValues values, ColumnMetadata column)
        {
            if (Is(column, schema.Entry))
            {
                return values.Entry;
            }

            if (schema.Title != null && Is(column, schema.Title))
            {
                return values.Title ?? string.Empty;
            }

            if (schema.Details != null && Is(column, schema.Details))
            {
                return values.Details ?? string.Empty;
            }

            if (schema.Objectives != null && Is(column, schema.Objectives))
            {
                return values.Objectives ?? string.Empty;
            }

            long? numeric = null;
            if (schema.MinLevel != null && Is(column, schema.MinLevel))
            {
                numeric = values.MinLevel;
            }
            else if (schema.QuestLevel != null && Is(column, schema.QuestLevel))
            {
                numeric = values.QuestLevel;
            }
            else if (schema.PrevQuestId != null && Is(column, schema.PrevQuestId))
            {
                numeric = values.PrevQuestId;
            }
            else if (schema.NextQuestId != null && Is(column, schema.NextQuestId))
            {
                numeric = values.NextQuestId;
            }
            else if (schema.ExclusiveGroup != null && Is(column, schema.ExclusiveGroup))
            {
                numeric = values.ExclusiveGroup;
            }
            else if (schema.NextQuestInChain != null && Is(column, schema.NextQuestInChain))
            {
                numeric = values.NextQuestInChain;
            }

            return numeric ?? (column.IsNullable ? null : 0L);
        }

        // Build quest insert preview using schema-aware database operations.
        private static string BuildQuestInsertPreview(TableMetadata table, QuestSchema schema, QuestTemplateValues values)
        {
            List<ColumnMetadata> columns = QuestWriteColumns(schema, true);
            return "INSERT INTO " + Q(table.Name) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" + string.Join(", ", columns.Select(c => SqlLiteral(QuestValue(schema, values, c)))) + ");";
        }

        // Build quest update preview using schema-aware database operations.
        private static string BuildQuestUpdatePreview(TableMetadata table, QuestSchema schema, QuestTemplateValues values)
        {
            List<ColumnMetadata> columns = QuestWriteColumns(schema, false);
            return "UPDATE " + Q(table.Name) + " SET " + string.Join(", ", columns.Select(c => Q(c.Name) + " = " + SqlLiteral(QuestValue(schema, values, c)))) +
                   " WHERE " + Q(schema.Entry.Name) + " = " + values.Entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Build full insert preview using schema-aware database operations.
        private static string BuildFullInsertPreview(TableMetadata table, Dictionary<string, object?> row)
        {
            List<ColumnMetadata> columns = table.Columns.Where(c => !c.IsGenerated && row.ContainsKey(c.Name)).ToList();
            return "INSERT INTO " + Q(table.Name) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" + string.Join(", ", columns.Select(c => SqlLiteral(row[c.Name]))) + ");";
        }

        // Handle text expr within the quest viewer service workflow.
        private static string TextExpr(string alias, ColumnMetadata? column) => column == null ? "''" : "COALESCE(CAST(" + Column(alias, column) + " AS CHAR), '')";
        // Create an alias-qualified numeric expression for an optional column.
        private static string NumberExpr(string alias, ColumnMetadata? column) => column == null ? "NULL" : Column(alias, column);
        // Build a numeric SQL expression with zero as the missing-value fallback.
        private static string NumberZeroExpr(string alias, ColumnMetadata? column) => column == null ? "0" : "COALESCE(" + Column(alias, column) + ",0)";
        // Use a default text SQL expression when the column is unavailable.
        private static string PlainTextExpr(ColumnMetadata? column) => column == null ? "''" : "COALESCE(CAST(" + Q(column.Name) + " AS CHAR), '')";
        // Return a numeric SQL expression without a table alias.
        private static string PlainNumberExpr(ColumnMetadata? column) => column == null ? "NULL" : Q(column.Name);
        // Return a numeric SQL expression with a zero fallback.
        private static string PlainNumberZeroExpr(ColumnMetadata? column) => column == null ? "0" : "COALESCE(" + Q(column.Name) + ",0)";
        // Quote and combine the database and table names as a qualified SQL identifier.
        private static string Qualify(TableMetadata table) => Q(table.DatabaseName) + "." + Q(table.Name);
        // Build a table-alias-qualified, safely quoted column name.
        private static string Column(string alias, ColumnMetadata column) => alias + "." + Q(column.Name);
        // Quote a database identifier through the validated quoting helper.
        private static string Q(string value) => MySqlIdentifier.Quote(value);
        // Compare schema column names without regard to letter case.
        private static bool Is(ColumnMetadata a, ColumnMetadata b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        // Locate a compatible column from the available schema metadata.
        private static ColumnMetadata? Find(TableMetadata? table, params string[] names) => table?.Columns.FirstOrDefault(c => names.Any(n => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)));
        // Escape a value for a generated SQL preview; command values use parameters.
        private static string QuoteSql(string value) => "'" + (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";
        // Format a value as a SQL literal for a generated SQL preview.
        private static string SqlLiteral(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "NULL";
            }

            if (value is byte[] bytes)
            {
                return "0x" + Convert.ToHexString(bytes);
            }

            if (value is bool b)
            {
                return b ? "1" : "0";
            }

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            }

            if (value is DateTime date)
            {
                return QuoteSql(date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }

            return QuoteSql(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        // Bind search parameters rather than interpolating user input into SQL.
        private static void AddSearch(MySqlCommand command, string search)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
            }
        }

        // Handle int64 within the quest viewer service workflow.
        private static long Int64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? 0 : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Handle nullable int64 within the quest viewer service workflow.
        private static long? NullableInt64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? null : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Handle string value within the quest viewer service workflow.
        private static string StringValue(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private sealed class QuestSchema
        {
            public ColumnMetadata Entry { get; private set; } = new ColumnMetadata();
            public ColumnMetadata? Title
            {
                get; private set;
            }

            public ColumnMetadata? Details
            {
                get; private set;
            }

            public ColumnMetadata? Objectives
            {
                get; private set;
            }

            public ColumnMetadata? MinLevel
            {
                get; private set;
            }

            public ColumnMetadata? QuestLevel
            {
                get; private set;
            }

            public ColumnMetadata? PrevQuestId
            {
                get; private set;
            }

            public ColumnMetadata? NextQuestId
            {
                get; private set;
            }

            public ColumnMetadata? ExclusiveGroup
            {
                get; private set;
            }

            public ColumnMetadata? NextQuestInChain
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static QuestSchema Create(TableMetadata table)
            {
                ColumnMetadata? entry = Find(table, "entry", "Entry", "id", "Id");
                return entry == null
                    ? throw new InvalidOperationException("quest_template does not expose a recognizable quest entry column.")
                    : new QuestSchema
                    {
                        Entry = entry,
                        Title = Find(table, "Title", "title", "QuestTitle", "quest_title"),
                        Details = Find(table, "Details", "details"),
                        Objectives = Find(table, "Objectives", "objectives"),
                        MinLevel = Find(table, "MinLevel", "minlevel", "min_level"),
                        QuestLevel = Find(table, "QuestLevel", "questlevel", "quest_level", "Level", "level"),
                        PrevQuestId = Find(table, "PrevQuestId", "prevquestid", "PrevQuestID", "prev_quest_id"),
                        NextQuestId = Find(table, "NextQuestId", "nextquestid", "NextQuestID", "next_quest_id"),
                        ExclusiveGroup = Find(table, "ExclusiveGroup", "exclusivegroup", "exclusive_group"),
                        NextQuestInChain = Find(table, "NextQuestInChain", "nextquestinchain", "next_quest_in_chain")
                    };
            }
        }

        private sealed class UnifiedRelationSchema
        {
            public ColumnMetadata Actor { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Entity { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Quest { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Role { get; private set; } = new ColumnMetadata();
            // Build a field definition from its seed metadata and expansion variants.
            public static UnifiedRelationSchema Create(TableMetadata table)
            {
                ColumnMetadata? actor = Find(table, "actor", "type");
                ColumnMetadata? entity = Find(table, "entry", "id");
                ColumnMetadata? quest = Find(table, "quest", "quest_id", "questId");
                ColumnMetadata? role = Find(table, "role");
                if (actor == null || entity == null || quest == null || role == null)
                {
                    throw new InvalidOperationException("quest_relations does not expose recognizable actor, entry, quest, and role columns.");
                }

                return new UnifiedRelationSchema { Actor = actor, Entity = entity, Quest = quest, Role = role };
            }

            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static UnifiedRelationSchema? TryCreate(TableMetadata table)
            {
                try
                {
                    return Create(table);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        private sealed class LegacyRelationSchema
        {
            public ColumnMetadata Entity { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Quest { get; private set; } = new ColumnMetadata();
            // Build a field definition from its seed metadata and expansion variants.
            public static LegacyRelationSchema Create(TableMetadata table)
            {
                ColumnMetadata? entity = Find(table, "id", "entry", "Entry");
                ColumnMetadata? quest = Find(table, "quest", "quest_id", "questId");
                if (entity == null || quest == null)
                {
                    throw new InvalidOperationException(table.Name + " does not expose recognizable entity and quest columns.");
                }

                return new LegacyRelationSchema { Entity = entity, Quest = quest };
            }

            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static LegacyRelationSchema? TryCreate(TableMetadata table)
            {
                try
                {
                    return Create(table);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        private sealed class RelationLocation
        {
            public TableMetadata Table { get; private set; } = new TableMetadata();
            public string Kind { get; private set; } = string.Empty;
            public ColumnMetadata Entity { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Quest { get; private set; } = new ColumnMetadata();
            public ColumnMetadata? Actor
            {
                get; private set;
            }

            public ColumnMetadata? Role
            {
                get; private set;
            }

            public long ActorValue
            {
                get; private set;
            }

            public long RoleValue
            {
                get; private set;
            }

            public bool IsUnified => Actor != null && Role != null;
            public bool IsSafeForMutation => Table.PrimaryKeyColumns.Count > 0 && Table.PrimaryKeyColumns.All(c => Is(c, Entity) || Is(c, Quest) || (Actor != null && Is(c, Actor)) || (Role != null && Is(c, Role)));

            // Handle unified within the quest viewer service workflow.
            public static RelationLocation Unified(TableMetadata table, UnifiedRelationSchema schema, string kind, long actor, long role) => new() { Table = table, Kind = kind, Entity = schema.Entity, Quest = schema.Quest, Actor = schema.Actor, Role = schema.Role, ActorValue = actor, RoleValue = role };
            // Construct a relation locator for a legacy MaNGOS table layout.
            public static RelationLocation Legacy(TableMetadata table, LegacyRelationSchema schema, string kind) => new() { Table = table, Kind = kind, Entity = schema.Entity, Quest = schema.Quest };
        }
    }
}
