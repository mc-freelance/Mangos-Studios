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
    internal sealed class EventViewerService : IEventViewerService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the event viewer service dependencies and state.
        public EventViewerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Search for  using schema-aware database operations.
        public async Task<PagedResult<EventSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            EventLinkSchema? creatureLinks = EventLinkSchema.TryCreate(gameEventCreature);
            EventLinkSchema? gameObjectLinks = EventLinkSchema.TryCreate(gameEventGameObject);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;

            string entryExpr = Column("e", schema.Entry);
            string descriptionExpr = TextExpr("e", schema.Description);
            string where = string.IsNullOrWhiteSpace(normalizedSearch)
                ? string.Empty
                : " WHERE CAST(" + entryExpr + " AS CHAR) LIKE @search OR " + descriptionExpr + " LIKE @search";

            string countSql = "SELECT COUNT(*) FROM " + Qualify(gameEvent) + " AS e" + where + ";";
            string dataSql =
                "SELECT " + entryExpr + " AS EventEntry, " +
                descriptionExpr + " AS EventDescription, " +
                TextExpr("e", schema.StartTime) + " AS EventStartTime, " +
                TextExpr("e", schema.EndTime) + " AS EventEndTime, " +
                NumberExpr("e", schema.Occurrence) + " AS EventOccurrence, " +
                NumberExpr("e", schema.Length) + " AS EventLength, " +
                NumberExpr("e", schema.Holiday) + " AS EventHoliday, " +
                LinkCountExpr(gameEventCreature, creatureLinks, entryExpr) + " AS CreatureCount, " +
                LinkCountExpr(gameEventGameObject, gameObjectLinks, entryExpr) + " AS GameObjectCount, " +
                NegativeCountExpr(gameEventCreature, creatureLinks, entryExpr) + " + " +
                NegativeCountExpr(gameEventGameObject, gameObjectLinks, entryExpr) + " AS NegativeLinkCount " +
                "FROM " + Qualify(gameEvent) + " AS e" + where +
                " ORDER BY " + entryExpr + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                AddSearch(command, normalizedSearch);
                total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            }

            List<EventSummary> rows = new();
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

            return new PagedResult<EventSummary>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Read  using schema-aware database operations.
        public async Task<EventDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            TableMetadata? creature,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObject,
            TableMetadata? gameObjectTemplate,
            long entry,
            int memberPage,
            int memberPageSize,
            CancellationToken cancellationToken)
        {
            EventSummary? summary = await GetSummaryAsync(profile, gameEvent, gameEventCreature, gameEventGameObject, entry, cancellationToken);
            if (summary == null)
            {
                return null;
            }

            memberPage = Math.Max(1, memberPage);
            memberPageSize = Math.Clamp(memberPageSize, 10, _options.MaximumBrowsePageSize);
            long total = summary.TotalMemberCount;
            int totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)memberPageSize);
            if (totalPages == 0)
            {
                memberPage = 1;
            }
            else if (memberPage > totalPages)
            {
                memberPage = totalPages;
            }

            int offset = checked((memberPage - 1) * memberPageSize);

            List<string> unions = new();
            EventLinkSchema? creatureLinkSchema = EventLinkSchema.TryCreate(gameEventCreature);
            EventLinkSchema? gameObjectLinkSchema = EventLinkSchema.TryCreate(gameEventGameObject);
            if (gameEventCreature != null && creatureLinkSchema != null)
            {
                unions.Add(BuildMemberSelect(
                    gameEventCreature,
                    creatureLinkSchema,
                    EventMemberSources.Creature,
                    "Creature",
                    creature,
                    creatureTemplate));
            }

            if (gameEventGameObject != null && gameObjectLinkSchema != null)
            {
                unions.Add(BuildMemberSelect(
                    gameEventGameObject,
                    gameObjectLinkSchema,
                    EventMemberSources.GameObject,
                    "GameObject",
                    gameObject,
                    gameObjectTemplate));
            }

            List<EventMember> members = new();
            if (unions.Count > 0 && total > 0)
            {
                string sql =
                    "SELECT members.SourceName, members.SourceLabel, members.SpawnGuid, members.EventValue, members.EntityEntry, members.EntityName, members.MapId " +
                    "FROM (" + string.Join(" UNION ALL ", unions) + ") AS members " +
                    "ORDER BY members.SourceName, members.EntityName, members.EntityEntry, members.SpawnGuid " +
                    "LIMIT @limit OFFSET @offset;";
                await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
                await connection.OpenAsync(cancellationToken);
                await using MySqlCommand command = new(sql, connection);
                command.Parameters.AddWithValue("@event", entry);
                command.Parameters.AddWithValue("@limit", memberPageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    long? entityEntry = NullableInt64(reader, "EntityEntry");
                    string name = StringValue(reader, "EntityName");
                    members.Add(new EventMember
                    {
                        Source = StringValue(reader, "SourceName"),
                        SourceLabel = StringValue(reader, "SourceLabel"),
                        Guid = Int64(reader, "SpawnGuid"),
                        EventValue = Int64(reader, "EventValue"),
                        EntityEntry = entityEntry,
                        ResolvedName = name,
                        IsResolved = entityEntry.HasValue,
                        MapId = NullableInt64(reader, "MapId")
                    });
                }
            }

            return new EventDetail
            {
                Event = summary,
                Members = new PagedResult<EventMember>
                {
                    Items = members,
                    TotalCount = total,
                    Page = memberPage,
                    PageSize = memberPageSize
                }
            };
        }

        // Report which editor operations the detected database schema supports.
        public EventEditorCapabilities GetEditorCapabilities(
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            List<EventMemberSourceCapability> members = new();
            AddMemberCapability(members, gameEventCreature, EventMemberSources.Creature, "Creature", "Creature spawn GUID");
            AddMemberCapability(members, gameEventGameObject, EventMemberSources.GameObject, "GameObject", "GameObject spawn GUID");
            bool safeEventKey = gameEvent.PrimaryKeyColumns.Count > 0 &&
                                gameEvent.PrimaryKeyColumns.All(column => Is(column, schema.Entry));
            return new EventEditorCapabilities
            {
                EntryColumn = schema.Entry.Name,
                StartTimeColumn = schema.StartTime?.Name,
                EndTimeColumn = schema.EndTime?.Name,
                OccurrenceColumn = schema.Occurrence?.Name,
                LengthColumn = schema.Length?.Name,
                HolidayColumn = schema.Holiday?.Name,
                DescriptionColumn = schema.Description?.Name,
                MemberSources = members,
                CanMutateEvents = safeEventKey
            };
        }

        // Add event using schema-aware database operations.
        public async Task<EventMutationResult> AddEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            EventTemplateValues values,
            CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            EnsureSafeEventMutation(gameEvent, schema);
            ValidateEventValues(schema, values);
            EventTemplateValues? existing = await GetEventValuesAsync(profile, gameEvent, schema, values.Entry, cancellationToken);
            if (existing != null)
            {
                throw new InvalidOperationException("Game event " + values.Entry.ToString(CultureInfo.InvariantCulture) + " already exists.");
            }

            List<ColumnMetadata> columns = EventWriteColumns(schema, includeEntry: true);
            string sql = "INSERT INTO " + Qualify(gameEvent) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" +
                         string.Join(", ", columns.Select((_, i) => "@v" + i.ToString(CultureInfo.InvariantCulture))) + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddEventValues(command, columns, schema, values, "@v");
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = gameEvent.Name,
                SqlPreview = BuildEventInsertPreview(gameEvent, schema, values),
                RevertSqlPreview = "DELETE FROM " + Q(gameEvent.Name) + " WHERE " + Q(schema.Entry.Name) + " = " + values.Entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;"
            };
        }

        // Update event using schema-aware database operations.
        public async Task<EventMutationResult> UpdateEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            long entry,
            EventTemplateValues values,
            CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            EnsureSafeEventMutation(gameEvent, schema);
            values.Entry = entry;
            ValidateEventValues(schema, values);
            EventTemplateValues? original = await GetEventValuesAsync(profile, gameEvent, schema, entry, cancellationToken) ?? throw new InvalidOperationException("The selected game event no longer exists.");
            List<ColumnMetadata> columns = EventWriteColumns(schema, includeEntry: false);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("This game_event schema has no editable event fields.");
            }

            string sql = "UPDATE " + Qualify(gameEvent) + " SET " + string.Join(", ", columns.Select((c, i) => Q(c.Name) + "=@v" + i.ToString(CultureInfo.InvariantCulture))) +
                         " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            AddEventValues(command, columns, schema, values, "@v");
            command.Parameters.AddWithValue("@entry", entry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = gameEvent.Name,
                SqlPreview = BuildEventUpdatePreview(gameEvent, schema, values),
                RevertSqlPreview = BuildEventUpdatePreview(gameEvent, schema, original)
            };
        }

        // Delete event using schema-aware database operations.
        public async Task<EventMutationResult> DeleteEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            long entry,
            CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            EnsureSafeEventMutation(gameEvent, schema);
            EventTemplateValues? original = await GetEventValuesAsync(profile, gameEvent, schema, entry, cancellationToken) ?? throw new InvalidOperationException("The selected game event no longer exists.");
            long linked = await CountLinksAsync(profile, gameEventCreature, entry, cancellationToken) + await CountLinksAsync(profile, gameEventGameObject, entry, cancellationToken);
            if (linked > 0)
            {
                throw new InvalidOperationException("Remove the event's creature and GameObject relationships before deleting the game_event row.");
            }

            string sql = "DELETE FROM " + Qualify(gameEvent) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = gameEvent.Name,
                SqlPreview = "DELETE FROM " + Q(gameEvent.Name) + " WHERE " + Q(schema.Entry.Name) + " = " + entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;",
                RevertSqlPreview = BuildEventInsertPreview(gameEvent, schema, original)
            };
        }

        // Add member using schema-aware database operations.
        public async Task<EventMutationResult> AddMemberAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            TableMetadata? creature,
            TableMetadata? gameObject,
            long eventEntry,
            EventMemberValues values,
            CancellationToken cancellationToken)
        {
            await EnsureEventExistsAsync(profile, gameEvent, eventEntry, cancellationToken);
            TableMetadata table = ResolveMemberTable(eventTables, values.Source);
            EventLinkSchema schema = EventLinkSchema.Create(table);
            EnsureSafeMemberMutation(table, schema);
            values.EventValue = NormalizeEventValue(eventEntry, values.EventValue, schema.SupportsNegativeEvent);
            await EnsureSpawnExistsAsync(profile, values.Source, values.Guid, creature, gameObject, cancellationToken);
            EventMemberValues? existing = HasGuidOnlyPrimaryKey(table, schema)
                ? await GetMemberByGuidAsync(profile, table, schema, values.Guid, cancellationToken)
                : await GetMemberValuesAsync(profile, table, schema, values.Guid, values.EventValue, cancellationToken);
            if (existing != null)
            {
                long existingEvent = Math.Abs(existing.EventValue);
                if (existingEvent == eventEntry)
                {
                    throw new InvalidOperationException("That spawn is already linked to this event.");
                }

                throw new InvalidOperationException("Spawn GUID " + values.Guid.ToString(CultureInfo.InvariantCulture) +
                    " is already linked to game event " + existingEvent.ToString(CultureInfo.InvariantCulture) +
                    " in " + table.Name + ". This MaNGOS schema allows one event relationship per spawn GUID in that table.");
            }

            string sql = "INSERT INTO " + Qualify(table) + " (" + Q(schema.Guid.Name) + ", " + Q(schema.Event.Name) + ") VALUES (@guid, @event);";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@guid", values.Guid);
            command.Parameters.AddWithValue("@event", values.EventValue);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = table.Name,
                SqlPreview = BuildMemberInsertPreview(table, schema, values),
                RevertSqlPreview = BuildMemberDeletePreview(table, schema, values)
            };
        }

        // Update member using schema-aware database operations.
        public async Task<EventMutationResult> UpdateMemberAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            TableMetadata? creature,
            TableMetadata? gameObject,
            long eventEntry,
            EventMemberValues original,
            EventMemberValues replacement,
            CancellationToken cancellationToken)
        {
            await EnsureEventExistsAsync(profile, gameEvent, eventEntry, cancellationToken);
            if (!string.Equals(original.Source, replacement.Source, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The membership source cannot be changed while editing. Remove the relationship and add it under the other source instead.");
            }

            TableMetadata table = ResolveMemberTable(eventTables, original.Source);
            EventLinkSchema schema = EventLinkSchema.Create(table);
            EnsureSafeMemberMutation(table, schema);
            original.EventValue = NormalizeEventValue(eventEntry, original.EventValue, schema.SupportsNegativeEvent);
            replacement.EventValue = NormalizeEventValue(eventEntry, replacement.EventValue, schema.SupportsNegativeEvent);
            EventMemberValues? persisted = await GetMemberValuesAsync(profile, table, schema, original.Guid, original.EventValue, cancellationToken) ??
                throw new InvalidOperationException("The selected event relationship no longer exists.");
            await EnsureSpawnExistsAsync(profile, replacement.Source, replacement.Guid, creature, gameObject, cancellationToken);
            if (HasGuidOnlyPrimaryKey(table, schema) && replacement.Guid != persisted.Guid)
            {
                EventMemberValues? conflict = await GetMemberByGuidAsync(profile, table, schema, replacement.Guid, cancellationToken);
                if (conflict != null)
                {
                    throw new InvalidOperationException("Spawn GUID " + replacement.Guid.ToString(CultureInfo.InvariantCulture) +
                        " already has an event relationship in " + table.Name + ".");
                }
            }

            string sql = "UPDATE " + Qualify(table) + " SET " + Q(schema.Guid.Name) + "=@newGuid, " + Q(schema.Event.Name) + "=@newEvent" +
                         " WHERE " + Q(schema.Guid.Name) + "=@oldGuid AND " + Q(schema.Event.Name) + "=@oldEvent LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@newGuid", replacement.Guid);
            command.Parameters.AddWithValue("@newEvent", replacement.EventValue);
            command.Parameters.AddWithValue("@oldGuid", persisted.Guid);
            command.Parameters.AddWithValue("@oldEvent", persisted.EventValue);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = table.Name,
                SqlPreview = BuildMemberUpdatePreview(table, schema, persisted, replacement),
                RevertSqlPreview = BuildMemberUpdatePreview(table, schema, replacement, persisted)
            };
        }

        // Remove member using schema-aware database operations.
        public async Task<EventMutationResult> RemoveMemberAsync(
            ConnectionProfile profile,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            EventMemberValues values,
            CancellationToken cancellationToken)
        {
            TableMetadata table = ResolveMemberTable(eventTables, values.Source);
            EventLinkSchema schema = EventLinkSchema.Create(table);
            EnsureSafeMemberMutation(table, schema);
            EventMemberValues? persisted = await GetMemberValuesAsync(profile, table, schema, values.Guid, values.EventValue, cancellationToken) ??
                throw new InvalidOperationException("The selected event relationship no longer exists.");
            string sql = "DELETE FROM " + Qualify(table) + " WHERE " + Q(schema.Guid.Name) + "=@guid AND " + Q(schema.Event.Name) + "=@event LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@guid", persisted.Guid);
            command.Parameters.AddWithValue("@event", persisted.EventValue);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return new EventMutationResult
            {
                AffectedRows = affected,
                ObjectName = table.Name,
                SqlPreview = BuildMemberDeletePreview(table, schema, persisted),
                RevertSqlPreview = BuildMemberInsertPreview(table, schema, persisted)
            };
        }

        // Read summary using schema-aware database operations.
        private async Task<EventSummary?> GetSummaryAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            long entry,
            CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            EventLinkSchema? creatureLinks = EventLinkSchema.TryCreate(gameEventCreature);
            EventLinkSchema? gameObjectLinks = EventLinkSchema.TryCreate(gameEventGameObject);
            string entryExpr = Column("e", schema.Entry);
            string sql =
                "SELECT " + entryExpr + " AS EventEntry, " + TextExpr("e", schema.Description) + " AS EventDescription, " +
                TextExpr("e", schema.StartTime) + " AS EventStartTime, " + TextExpr("e", schema.EndTime) + " AS EventEndTime, " +
                NumberExpr("e", schema.Occurrence) + " AS EventOccurrence, " + NumberExpr("e", schema.Length) + " AS EventLength, " +
                NumberExpr("e", schema.Holiday) + " AS EventHoliday, " +
                LinkCountExpr(gameEventCreature, creatureLinks, entryExpr) + " AS CreatureCount, " +
                LinkCountExpr(gameEventGameObject, gameObjectLinks, entryExpr) + " AS GameObjectCount, " +
                NegativeCountExpr(gameEventCreature, creatureLinks, entryExpr) + " + " + NegativeCountExpr(gameEventGameObject, gameObjectLinks, entryExpr) + " AS NegativeLinkCount " +
                "FROM " + Qualify(gameEvent) + " AS e WHERE " + entryExpr + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadSummary(reader) : null;
        }

        // Read summary using schema-aware database operations.
        private static EventSummary ReadSummary(DbDataReader reader)
        {
            return new EventSummary
            {
                Entry = Int64(reader, "EventEntry"),
                Description = StringValue(reader, "EventDescription"),
                StartTime = StringValue(reader, "EventStartTime"),
                EndTime = StringValue(reader, "EventEndTime"),
                Occurrence = NullableInt64(reader, "EventOccurrence"),
                Length = NullableInt64(reader, "EventLength"),
                Holiday = NullableInt64(reader, "EventHoliday"),
                CreatureCount = Int64(reader, "CreatureCount"),
                GameObjectCount = Int64(reader, "GameObjectCount"),
                NegativeLinkCount = Int64(reader, "NegativeLinkCount")
            };
        }

        // Build member select using schema-aware database operations.
        private static string BuildMemberSelect(
            TableMetadata membership,
            EventLinkSchema link,
            string source,
            string label,
            TableMetadata? spawn,
            TableMetadata? template)
        {
            string mGuid = Column("m", link.Guid);
            string mEvent = Column("m", link.Event);
            ColumnMetadata? spawnGuid = Find(spawn, "guid", "Guid");
            ColumnMetadata? spawnEntry = Find(spawn, "id", "entry", "Entry");
            ColumnMetadata? spawnMap = Find(spawn, "map", "mapid", "map_id");
            ColumnMetadata? templateEntry = Find(template, "entry", "Entry", "id");
            ColumnMetadata? templateName = Find(template, "name", "Name");

            string entityEntry = spawn != null && spawnGuid != null && spawnEntry != null
                ? Column("s", spawnEntry)
                : "NULL";
            string mapId = spawn != null && spawnGuid != null && spawnMap != null
                ? Column("s", spawnMap)
                : "NULL";
            string entityName = "''";
            string joins = string.Empty;
            if (spawn != null && spawnGuid != null)
            {
                joins += " LEFT JOIN " + Qualify(spawn) + " AS s ON " + Column("s", spawnGuid) + "=" + mGuid;
                if (spawnEntry != null && template != null && templateEntry != null)
                {
                    joins += " LEFT JOIN " + Qualify(template) + " AS t ON " + Column("t", templateEntry) + "=" + Column("s", spawnEntry);
                    if (templateName != null)
                    {
                        entityName = "COALESCE(" + Column("t", templateName) + ", '')";
                    }
                }
            }

            return "SELECT " + QuoteSql(source) + " AS SourceName, " + QuoteSql(label) + " AS SourceLabel, " +
                   mGuid + " AS SpawnGuid, " + mEvent + " AS EventValue, " + entityEntry + " AS EntityEntry, " +
                   entityName + " AS EntityName, " + mapId + " AS MapId FROM " + Qualify(membership) + " AS m" + joins +
                   " WHERE ABS(" + mEvent + ")=@event";
        }

        // Read event values using schema-aware database operations.
        private async Task<EventTemplateValues?> GetEventValuesAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            EventSchema schema,
            long entry,
            CancellationToken cancellationToken)
        {
            string sql = "SELECT " + Q(schema.Entry.Name) + " AS EventEntry, " + PlainTextExpr(schema.Description) + " AS EventDescription, " +
                         PlainTextExpr(schema.StartTime) + " AS EventStartTime, " + PlainTextExpr(schema.EndTime) + " AS EventEndTime, " +
                         PlainNumberExpr(schema.Occurrence) + " AS EventOccurrence, " + PlainNumberExpr(schema.Length) + " AS EventLength, " +
                         PlainNumberExpr(schema.Holiday) + " AS EventHoliday FROM " + Qualify(gameEvent) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new EventTemplateValues
            {
                Entry = Int64(reader, "EventEntry"),
                Description = StringValue(reader, "EventDescription"),
                StartTime = StringValue(reader, "EventStartTime"),
                EndTime = StringValue(reader, "EventEndTime"),
                Occurrence = NullableInt64(reader, "EventOccurrence"),
                Length = NullableInt64(reader, "EventLength"),
                Holiday = NullableInt64(reader, "EventHoliday")
            };
        }

        // Read member values using schema-aware database operations.
        private async Task<EventMemberValues?> GetMemberValuesAsync(
            ConnectionProfile profile,
            TableMetadata table,
            EventLinkSchema schema,
            long guid,
            long eventValue,
            CancellationToken cancellationToken)
        {
            string sql = "SELECT " + Q(schema.Guid.Name) + " AS SpawnGuid, " + Q(schema.Event.Name) + " AS EventValue FROM " + Qualify(table) +
                         " WHERE " + Q(schema.Guid.Name) + "=@guid AND " + Q(schema.Event.Name) + "=@event LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@guid", guid);
            command.Parameters.AddWithValue("@event", eventValue);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new EventMemberValues { Source = table.Name, Guid = Int64(reader, "SpawnGuid"), EventValue = Int64(reader, "EventValue") };
        }

        // Read member by guid using schema-aware database operations.
        private async Task<EventMemberValues?> GetMemberByGuidAsync(
            ConnectionProfile profile,
            TableMetadata table,
            EventLinkSchema schema,
            long guid,
            CancellationToken cancellationToken)
        {
            string sql = "SELECT " + Q(schema.Guid.Name) + " AS SpawnGuid, " + Q(schema.Event.Name) + " AS EventValue FROM " + Qualify(table) +
                         " WHERE " + Q(schema.Guid.Name) + "=@guid LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@guid", guid);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new EventMemberValues { Source = table.Name, Guid = Int64(reader, "SpawnGuid"), EventValue = Int64(reader, "EventValue") };
        }

        // Ensure event exists using schema-aware database operations.
        private async Task EnsureEventExistsAsync(ConnectionProfile profile, TableMetadata gameEvent, long entry, CancellationToken cancellationToken)
        {
            EventSchema schema = EventSchema.Create(gameEvent);
            string sql = "SELECT 1 FROM " + Qualify(gameEvent) + " WHERE " + Q(schema.Entry.Name) + "=@entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, gameEvent.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            object? value = await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Game event " + entry.ToString(CultureInfo.InvariantCulture) + " does not exist.");
        }

        // Ensure spawn exists using schema-aware database operations.
        private async Task EnsureSpawnExistsAsync(
            ConnectionProfile profile,
            string source,
            long guid,
            TableMetadata? creature,
            TableMetadata? gameObject,
            CancellationToken cancellationToken)
        {
            if (guid <= 0)
            {
                throw new InvalidOperationException("A positive spawn GUID is required.");
            }

            TableMetadata? spawn = string.Equals(source, EventMemberSources.Creature, StringComparison.OrdinalIgnoreCase) ? creature : gameObject;
            if (spawn == null)
            {
                return;
            }

            ColumnMetadata? guidColumn = Find(spawn, "guid", "Guid");
            if (guidColumn == null)
            {
                return;
            }

            string sql = "SELECT 1 FROM " + Qualify(spawn) + " WHERE " + Q(guidColumn.Name) + "=@guid LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, spawn.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@guid", guid);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            if (value == null)
            {
                string label = string.Equals(source, EventMemberSources.Creature, StringComparison.OrdinalIgnoreCase) ? "creature" : "GameObject";
                throw new InvalidOperationException("Spawn GUID " + guid.ToString(CultureInfo.InvariantCulture) + " was not found in the " + label + " table.");
            }
        }

        // Count links using schema-aware database operations.
        private async Task<long> CountLinksAsync(ConnectionProfile profile, TableMetadata? table, long eventEntry, CancellationToken cancellationToken)
        {
            EventLinkSchema? schema = EventLinkSchema.TryCreate(table);
            if (table == null || schema == null)
            {
                return 0;
            }

            string sql = "SELECT COUNT(*) FROM " + Qualify(table) + " WHERE ABS(" + Q(schema.Event.Name) + ")=@event;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@event", eventEntry);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
        }

        // Add member capability using schema-aware database operations.
        private static void AddMemberCapability(
            List<EventMemberSourceCapability> destination,
            TableMetadata? table,
            string source,
            string label,
            string memberLabel)
        {
            EventLinkSchema? schema = EventLinkSchema.TryCreate(table);
            if (table == null || schema == null)
            {
                return;
            }

            bool safeKey = table.PrimaryKeyColumns.Count > 0 && table.PrimaryKeyColumns.All(column => Is(column, schema.Guid) || Is(column, schema.Event));
            if (!safeKey)
            {
                return;
            }

            destination.Add(new EventMemberSourceCapability
            {
                Source = source,
                Label = label,
                MemberLabel = memberLabel,
                SupportsNegativeEvent = schema.SupportsNegativeEvent
            });
        }

        // Resolve member table using schema-aware database operations.
        private static TableMetadata ResolveMemberTable(IReadOnlyDictionary<string, TableMetadata> tables, string source)
        {
            string normalized = (source ?? string.Empty).Trim();
            bool known = string.Equals(normalized, EventMemberSources.Creature, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(normalized, EventMemberSources.GameObject, StringComparison.OrdinalIgnoreCase);
            if (!known || !tables.TryGetValue(normalized, out TableMetadata? table))
            {
                throw new InvalidOperationException("The selected event membership source is not available in this database.");
            }

            return table;
        }

        // Ensure safe event mutation using schema-aware database operations.
        private static void EnsureSafeEventMutation(TableMetadata table, EventSchema schema)
        {
            if (table.PrimaryKeyColumns.Count == 0 || table.PrimaryKeyColumns.Any(column => !Is(column, schema.Entry)))
            {
                throw new InvalidOperationException("This game_event primary key cannot be edited safely through Event Viewer.");
            }
        }

        // Ensure safe member mutation using schema-aware database operations.
        private static void EnsureSafeMemberMutation(TableMetadata table, EventLinkSchema schema)
        {
            if (table.PrimaryKeyColumns.Count == 0 || table.PrimaryKeyColumns.Any(column => !Is(column, schema.Guid) && !Is(column, schema.Event)))
            {
                throw new InvalidOperationException(table.Name + " has an unfamiliar primary key and cannot be edited safely through Event Viewer.");
            }
        }

        // Determine whether guid only primary key using schema-aware database operations.
        private static bool HasGuidOnlyPrimaryKey(TableMetadata table, EventLinkSchema schema)
        {
            return table.PrimaryKeyColumns.Count > 0 && table.PrimaryKeyColumns.All(column => Is(column, schema.Guid));
        }

        // Validate event values using schema-aware database operations.
        private static void ValidateEventValues(EventSchema schema, EventTemplateValues values)
        {
            if (values.Entry <= 0)
            {
                throw new InvalidOperationException("Game event entry must be greater than zero.");
            }

            if (schema.StartTime != null && string.IsNullOrWhiteSpace(values.StartTime))
            {
                throw new InvalidOperationException("A start time is required by this game_event schema.");
            }

            if (schema.EndTime != null && string.IsNullOrWhiteSpace(values.EndTime))
            {
                throw new InvalidOperationException("An end time is required by this game_event schema.");
            }

            if (values.Occurrence < 0)
            {
                throw new InvalidOperationException("Occurrence cannot be negative.");
            }

            if (values.Length < 0)
            {
                throw new InvalidOperationException("Length cannot be negative.");
            }

            if (values.Holiday < 0)
            {
                throw new InvalidOperationException("Holiday ID cannot be negative.");
            }
        }

        // Normalize event value using schema-aware database operations.
        private static long NormalizeEventValue(long eventEntry, long requestedValue, bool supportsNegative)
        {
            if (eventEntry <= 0)
            {
                throw new InvalidOperationException("A positive game event entry is required.");
            }

            bool negative = requestedValue < 0;
            if (negative && !supportsNegative)
            {
                throw new InvalidOperationException("This event-link column is unsigned and cannot store inverse event relationships.");
            }

            return negative ? -eventEntry : eventEntry;
        }

        // Handle event write columns within the event viewer service workflow.
        private static List<ColumnMetadata> EventWriteColumns(EventSchema schema, bool includeEntry)
        {
            List<ColumnMetadata> columns = new();
            if (includeEntry)
            {
                columns.Add(schema.Entry);
            }

            Add(columns, schema.StartTime);
            Add(columns, schema.EndTime);
            Add(columns, schema.Occurrence);
            Add(columns, schema.Length);
            Add(columns, schema.Holiday);
            Add(columns, schema.Description);
            return columns;
        }

        // Handle add within the event viewer service workflow.
        private static void Add(List<ColumnMetadata> columns, ColumnMetadata? column)
        {
            if (column != null)
            {
                columns.Add(column);
            }
        }

        // Add event values using schema-aware database operations.
        private static void AddEventValues(MySqlCommand command, List<ColumnMetadata> columns, EventSchema schema, EventTemplateValues values, string prefix)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                ColumnMetadata column = columns[i];
                object? value = EventValue(schema, values, column);
                command.Parameters.AddWithValue(prefix + i.ToString(CultureInfo.InvariantCulture), value ?? DBNull.Value);
            }
        }

        // Handle event value within the event viewer service workflow.
        private static object? EventValue(EventSchema schema, EventTemplateValues values, ColumnMetadata column)
        {
            if (Is(column, schema.Entry))
            {
                return values.Entry;
            }

            if (schema.StartTime != null && Is(column, schema.StartTime))
            {
                return values.StartTime;
            }

            if (schema.EndTime != null && Is(column, schema.EndTime))
            {
                return values.EndTime;
            }

            if (schema.Occurrence != null && Is(column, schema.Occurrence))
            {
                return values.Occurrence;
            }

            if (schema.Length != null && Is(column, schema.Length))
            {
                return values.Length;
            }

            if (schema.Holiday != null && Is(column, schema.Holiday))
            {
                return values.Holiday;
            }

            if (schema.Description != null && Is(column, schema.Description))
            {
                return values.Description;
            }

            return null;
        }

        // Build event insert preview using schema-aware database operations.
        private static string BuildEventInsertPreview(TableMetadata table, EventSchema schema, EventTemplateValues values)
        {
            List<ColumnMetadata> columns = EventWriteColumns(schema, includeEntry: true);
            return "INSERT INTO " + Q(table.Name) + " (" + string.Join(", ", columns.Select(c => Q(c.Name))) + ") VALUES (" +
                   string.Join(", ", columns.Select(c => SqlLiteral(EventValue(schema, values, c)))) + ");";
        }

        // Build event update preview using schema-aware database operations.
        private static string BuildEventUpdatePreview(TableMetadata table, EventSchema schema, EventTemplateValues values)
        {
            List<ColumnMetadata> columns = EventWriteColumns(schema, includeEntry: false);
            return "UPDATE " + Q(table.Name) + " SET " + string.Join(", ", columns.Select(c => Q(c.Name) + " = " + SqlLiteral(EventValue(schema, values, c)))) +
                   " WHERE " + Q(schema.Entry.Name) + " = " + values.Entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Build member insert preview using schema-aware database operations.
        private static string BuildMemberInsertPreview(TableMetadata table, EventLinkSchema schema, EventMemberValues values)
        {
            return "INSERT INTO " + Q(table.Name) + " (" + Q(schema.Guid.Name) + ", " + Q(schema.Event.Name) + ") VALUES (" +
                   values.Guid.ToString(CultureInfo.InvariantCulture) + ", " + values.EventValue.ToString(CultureInfo.InvariantCulture) + ");";
        }

        // Build member delete preview using schema-aware database operations.
        private static string BuildMemberDeletePreview(TableMetadata table, EventLinkSchema schema, EventMemberValues values)
        {
            return "DELETE FROM " + Q(table.Name) + " WHERE " + Q(schema.Guid.Name) + " = " + values.Guid.ToString(CultureInfo.InvariantCulture) +
                   " AND " + Q(schema.Event.Name) + " = " + values.EventValue.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Build member update preview using schema-aware database operations.
        private static string BuildMemberUpdatePreview(TableMetadata table, EventLinkSchema schema, EventMemberValues original, EventMemberValues replacement)
        {
            return "UPDATE " + Q(table.Name) + " SET " + Q(schema.Guid.Name) + " = " + replacement.Guid.ToString(CultureInfo.InvariantCulture) +
                   ", " + Q(schema.Event.Name) + " = " + replacement.EventValue.ToString(CultureInfo.InvariantCulture) +
                   " WHERE " + Q(schema.Guid.Name) + " = " + original.Guid.ToString(CultureInfo.InvariantCulture) +
                   " AND " + Q(schema.Event.Name) + " = " + original.EventValue.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Handle link count expr within the event viewer service workflow.
        private static string LinkCountExpr(TableMetadata? table, EventLinkSchema? schema, string entryExpr)
        {
            if (table == null || schema == null)
            {
                return "0";
            }

            return "(SELECT COUNT(*) FROM " + Qualify(table) + " AS l WHERE ABS(" + Column("l", schema.Event) + ")=" + entryExpr + ")";
        }

        // Handle negative count expr within the event viewer service workflow.
        private static string NegativeCountExpr(TableMetadata? table, EventLinkSchema? schema, string entryExpr)
        {
            if (table == null || schema?.SupportsNegativeEvent is not true)
            {
                return "0";
            }

            return "(SELECT COUNT(*) FROM " + Qualify(table) + " AS n WHERE " + Column("n", schema.Event) + "<0 AND ABS(" + Column("n", schema.Event) + ")=" + entryExpr + ")";
        }

        // Handle text expr within the event viewer service workflow.
        private static string TextExpr(string alias, ColumnMetadata? column) => column == null ? "''" : "COALESCE(CAST(" + Column(alias, column) + " AS CHAR), '')";
        // Create an alias-qualified numeric expression for an optional column.
        private static string NumberExpr(string alias, ColumnMetadata? column) => column == null ? "NULL" : Column(alias, column);
        // Use a default text SQL expression when the column is unavailable.
        private static string PlainTextExpr(ColumnMetadata? column) => column == null ? "''" : "COALESCE(CAST(" + Q(column.Name) + " AS CHAR), '')";
        // Return a numeric SQL expression without a table alias.
        private static string PlainNumberExpr(ColumnMetadata? column) => column == null ? "NULL" : Q(column.Name);
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

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
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

        // Handle int64 within the event viewer service workflow.
        private static long Int64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? 0 : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Handle nullable int64 within the event viewer service workflow.
        private static long? NullableInt64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? null : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Handle string value within the event viewer service workflow.
        private static string StringValue(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private sealed class EventSchema
        {
            public ColumnMetadata Entry { get; private set; } = new ColumnMetadata();

            public ColumnMetadata? StartTime
            {
                get; private set;
            }

            public ColumnMetadata? EndTime
            {
                get; private set;
            }

            public ColumnMetadata? Occurrence
            {
                get; private set;
            }

            public ColumnMetadata? Length
            {
                get; private set;
            }

            public ColumnMetadata? Holiday
            {
                get; private set;
            }

            public ColumnMetadata? Description
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static EventSchema Create(TableMetadata table)
            {
                ColumnMetadata? entry = Find(table, "entry", "eventEntry", "event_entry", "id");
                return entry == null ? throw new InvalidOperationException("game_event does not expose a recognizable event entry column.")
                    : new EventSchema
                    {
                        Entry = entry,
                        StartTime = Find(table, "start_time", "starttime", "startTime", "start"),
                        EndTime = Find(table, "end_time", "endtime", "endTime", "end"),
                        Occurrence = Find(table, "occurence", "occurrence"),
                        Length = Find(table, "length", "duration"),
                        Holiday = Find(table, "holiday", "holidayId", "holiday_id"),
                        Description = Find(table, "description", "name")
                    };
            }
        }

        private sealed class EventLinkSchema
        {
            public ColumnMetadata Guid { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Event { get; private set; } = new ColumnMetadata();
            public bool SupportsNegativeEvent => !Event.ColumnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase);

            // Build a field definition from its seed metadata and expansion variants.
            public static EventLinkSchema Create(TableMetadata table)
            {
                ColumnMetadata? guid = Find(table, "guid", "spawnGuid", "spawn_guid");
                ColumnMetadata? eventColumn = Find(table, "event", "eventEntry", "event_entry");
                if (guid == null || eventColumn == null)
                {
                    throw new InvalidOperationException(table.Name + " does not expose recognizable GUID and event columns.");
                }

                return new EventLinkSchema
                {
                    Guid = guid,
                    Event = eventColumn
                };
            }

            // Inspect schema metadata and return a compatible binding only when required columns exist.
            public static EventLinkSchema? TryCreate(TableMetadata? table)
            {
                if (table == null)
                {
                    return null;
                }

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
    }
}
