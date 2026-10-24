// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Find overlapping world spawns while accounting for linked or pooled objects.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class WorldDoubleSpawnService : IWorldDoubleSpawnService
    {
        private const long MaximumMatchesToCount = 500000;
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the world double spawn service dependencies and state.
        public WorldDoubleSpawnService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Read map ids using schema-aware database operations.
        public async Task<IReadOnlyList<int>> GetMapIdsAsync(
            ConnectionProfile profile,
            TableMetadata? creature,
            TableMetadata? gameObject,
            CancellationToken cancellationToken)
        {
            SortedSet<int> mapIds = new();
            string? database = creature?.DatabaseName ?? gameObject?.DatabaseName;
            if (string.IsNullOrWhiteSpace(database))
            {
                return mapIds.ToArray();
            }

            await using MySqlConnection connection = _connectionFactory.Create(profile, database);
            await connection.OpenAsync(cancellationToken);

            if (creature != null)
            {
                await AddMapIdsAsync(connection, creature, mapIds, cancellationToken);
            }

            if (gameObject != null)
            {
                await AddMapIdsAsync(connection, gameObject, mapIds, cancellationToken);
            }

            return mapIds.ToArray();
        }

        // Search for  using schema-aware database operations.
        public async Task<DoubleSpawnSearchResult> SearchAsync(
            ConnectionProfile profile,
            TableMetadata? creature,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObject,
            TableMetadata? gameObjectTemplate,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventCreatureData,
            TableMetadata? gameEventGameObject,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolTemplate,
            TableMetadata? poolPool,
            DoubleSpawnSearchRequest request,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            request.Page = Math.Max(1, request.Page);
            request.PageSize = Math.Clamp(request.PageSize, 10, _options.MaximumBrowsePageSize);
            request.DistanceTolerance = Math.Clamp(request.DistanceTolerance, 0d, 100d);
            request.Scope = NormalizeScope(request.Scope);
            request.MatchMode = NormalizeMatchMode(request.MatchMode);
            request.PoolFilter = NormalizePoolFilter(request.PoolFilter);
            request.LinkedGameObjectFilter = NormalizeLinkedGameObjectFilter(request.LinkedGameObjectFilter);
            request.Search = (request.Search ?? string.Empty).Trim();

            SearchAccumulator accumulator = new(
                request.Page,
                request.PageSize,
                MaximumMatchesToCount,
                request.PoolFilter,
                request.LinkedGameObjectFilter,
                request.Search);
            List<string> warnings = new();

            bool scanCreatures = request.Scope == DoubleSpawnScopes.Both || request.Scope == DoubleSpawnScopes.Creatures;
            bool scanGameObjects = request.Scope == DoubleSpawnScopes.Both || request.Scope == DoubleSpawnScopes.GameObjects;
            bool canInspectLinkedGameObjects = CanInspectLinkedGameObjects(gameObjectTemplate);
            bool hasAnyPoolMembershipTable =
                (scanCreatures && (poolCreature != null || poolCreatureTemplate != null)) ||
                (scanGameObjects && (poolGameObject != null || poolGameObjectTemplate != null));

            if (hasAnyPoolMembershipTable && poolTemplate == null)
            {
                warnings.Add("pool_template is not available, so pooled spawns can still be identified but pool descriptions and max limits cannot be shown.");
            }

            if (hasAnyPoolMembershipTable && poolPool == null)
            {
                warnings.Add("pool_pool is not available, so mother-pool relationships cannot be shown.");
            }

            if (scanGameObjects && gameObject != null && !canInspectLinkedGameObjects)
            {
                warnings.Add("gameobject_template does not expose the type/data fields needed to identify template-linked gameobject pairs, so linked GO classification is unavailable for this scan.");
            }

            progress?.Report(new OperationProgressUpdate(
                "Loading pool metadata",
                "Reading pool templates and parent-pool relationships before positional analysis."));
            PoolReferenceData poolReferences = await LoadPoolReferenceDataAsync(
                profile,
                poolTemplate,
                poolPool,
                cancellationToken);
            progress?.Report(new OperationProgressUpdate(
                "Pool metadata loaded",
                poolReferences.Templates.Count.ToString("N0", CultureInfo.InvariantCulture) + " pool templates and " +
                poolReferences.Parents.Count.ToString("N0", CultureInfo.InvariantCulture) + " parent-pool mappings cached."));

            if (scanCreatures && creature == null)
            {
                warnings.Add("The creature spawn table is not available in this database.");
            }
            else if (scanCreatures && creature != null)
            {
                TableMetadata?[] creatureEventTables = { gameEventCreature, gameEventCreatureData };
                bool hasCreatureEventGuid = creatureEventTables.Any(table => table != null && Find(table, "guid") != null);
                if (request.ExcludeEventRelated && !hasCreatureEventGuid)
                {
                    warnings.Add("No creature game-event GUID mapping table is available, so event-linked creature spawns cannot be excluded.");
                }

                progress?.Report(new OperationProgressUpdate(
                    "Caching creature pools",
                    "Reading creature GUID and template-entry pool memberships."));
                PoolMembershipLookup creaturePools = await LoadPoolMembershipLookupAsync(
                    profile,
                    poolCreature,
                    poolCreatureTemplate,
                    poolReferences,
                    "Creature",
                    warnings,
                    cancellationToken);
                progress?.Report(new OperationProgressUpdate(
                    "Creature pools cached",
                    creaturePools.MembershipCount.ToString("N0", CultureInfo.InvariantCulture) + " creature pool memberships are available for classification."));

                await ScanSpawnTableAsync(
                    profile,
                    creature,
                    creatureTemplate,
                    creatureEventTables,
                    creaturePools,
                    "Creature",
                    request,
                    accumulator,
                    progress,
                    cancellationToken);
            }

            if (!accumulator.IsTruncated && scanGameObjects && gameObject == null)
            {
                warnings.Add("The gameobject spawn table is not available in this database.");
            }
            else if (!accumulator.IsTruncated && scanGameObjects && gameObject != null)
            {
                bool hasGameObjectEventGuid = gameEventGameObject != null && Find(gameEventGameObject, "guid") != null;
                if (request.ExcludeEventRelated && !hasGameObjectEventGuid)
                {
                    warnings.Add("game_event_gameobject is not available with a GUID column, so event-linked gameobject spawns cannot be excluded.");
                }

                progress?.Report(new OperationProgressUpdate(
                    "Caching GameObject pools",
                    "Reading GameObject GUID and template-entry pool memberships."));
                PoolMembershipLookup gameObjectPools = await LoadPoolMembershipLookupAsync(
                    profile,
                    poolGameObject,
                    poolGameObjectTemplate,
                    poolReferences,
                    "GameObject",
                    warnings,
                    cancellationToken);
                progress?.Report(new OperationProgressUpdate(
                    "GameObject pools cached",
                    gameObjectPools.MembershipCount.ToString("N0", CultureInfo.InvariantCulture) + " GameObject pool memberships are available for classification."));

                await ScanSpawnTableAsync(
                    profile,
                    gameObject,
                    gameObjectTemplate,
                    new TableMetadata?[] { hasGameObjectEventGuid ? gameEventGameObject : null },
                    gameObjectPools,
                    "GameObject",
                    request,
                    accumulator,
                    progress,
                    cancellationToken);
            }

            if (accumulator.IsTruncated)
            {
                warnings.Add("The scan reached the safety limit of " + MaximumMatchesToCount.ToString("N0", CultureInfo.InvariantCulture) + " matching pairs. Narrow the map, entity type, entry mode, or distance tolerance for a complete result.");
            }

            progress?.Report(new OperationProgressUpdate(
                "Preparing overlap results",
                accumulator.TotalMatches.ToString("N0", CultureInfo.InvariantCulture) + " matching pairs found across " +
                accumulator.MapIds.Count.ToString("N0", CultureInfo.InvariantCulture) + " map(s)."));

            return new DoubleSpawnSearchResult
            {
                Matches = new PagedResult<DoubleSpawnMatch>
                {
                    Items = accumulator.PageItems,
                    TotalCount = accumulator.TotalMatches,
                    Page = request.Page,
                    PageSize = request.PageSize
                },
                CreatureMatchCount = accumulator.CreatureMatches,
                GameObjectMatchCount = accumulator.GameObjectMatches,
                PooledMatchCount = accumulator.PooledMatches,
                LinkedGameObjectMatchCount = accumulator.LinkedGameObjectMatches,
                CreatureSpawnsScanned = accumulator.CreatureSpawnsScanned,
                GameObjectSpawnsScanned = accumulator.GameObjectSpawnsScanned,
                MapsScanned = accumulator.MapIds.Count,
                IsTruncated = accumulator.IsTruncated,
                Warnings = warnings
            };
        }

        // Handle scan spawn table async within the world double spawn service workflow.
        private async Task ScanSpawnTableAsync(
            ConnectionProfile profile,
            TableMetadata spawnTable,
            TableMetadata? templateTable,
            IReadOnlyList<TableMetadata?> eventTables,
            PoolMembershipLookup poolLookup,
            string kind,
            DoubleSpawnSearchRequest request,
            SearchAccumulator accumulator,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            SpawnSchema schema = SpawnSchema.Create(spawnTable, templateTable);
            string spawn = Qualify(spawnTable);
            string guid = Column("s", schema.Guid);
            string entry = Column("s", schema.Entry);
            string map = Column("s", schema.Map);
            string x = Column("s", schema.PositionX);
            string y = Column("s", schema.PositionY);
            string z = Column("s", schema.PositionZ);

            string nameSelection = "''";
            string gameObjectTypeSelection = "NULL";
            string linkedGameObjectEntrySelection = "NULL";
            string linkedGameObjectFieldSelection = "''";
            string templateJoin = string.Empty;
            if (templateTable != null && schema.TemplateEntry != null)
            {
                string template = Qualify(templateTable);
                string templateEntry = Column("t", schema.TemplateEntry);
                templateJoin = " LEFT JOIN " + template + " AS t ON " + templateEntry + " = " + entry;

                if (schema.TemplateName != null)
                {
                    nameSelection = "COALESCE(" + Column("t", schema.TemplateName) + ", '')";
                }

                if (string.Equals(kind, "GameObject", StringComparison.OrdinalIgnoreCase) && schema.TemplateType != null)
                {
                    gameObjectTypeSelection = Column("t", schema.TemplateType);
                    BuildLinkedGameObjectSelections(
                        schema,
                        out linkedGameObjectEntrySelection,
                        out linkedGameObjectFieldSelection);
                }
            }

            string eventExpression = "0";
            string eventExclusion = string.Empty;
            List<string> eventChecks = new();
            int eventAliasIndex = 0;
            foreach (TableMetadata? eventTable in eventTables)
            {
                if (eventTable == null)
                {
                    continue;
                }

                ColumnMetadata? eventGuidColumn = Find(eventTable, "guid");
                if (eventGuidColumn == null)
                {
                    continue;
                }

                string alias = "ge" + eventAliasIndex.ToString(CultureInfo.InvariantCulture);
                eventAliasIndex++;
                string eventQualified = Qualify(eventTable);
                string eventGuid = Column(alias, eventGuidColumn);
                eventChecks.Add("EXISTS (SELECT 1 FROM " + eventQualified + " AS " + MySqlIdentifier.Quote(alias) + " WHERE " + eventGuid + " = " + guid + ")");
            }

            if (eventChecks.Count > 0)
            {
                string anyEvent = "(" + string.Join(" OR ", eventChecks) + ")";
                if (request.ExcludeEventRelated)
                {
                    eventExclusion = " AND NOT " + anyEvent;
                }
                else
                {
                    eventExpression = anyEvent;
                }
            }

            string mapFilter = request.MapId.HasValue ? " AND " + map + " = @mapId" : string.Empty;
            string sql =
                "SELECT " + guid + " AS SpawnGuid, " + entry + " AS SpawnEntry, " + map + " AS MapId, " +
                x + " AS PositionX, " + y + " AS PositionY, " + z + " AS PositionZ, " +
                nameSelection + " AS SpawnName, " + eventExpression + " AS IsEventRelated, " +
                gameObjectTypeSelection + " AS GameObjectType, " +
                linkedGameObjectEntrySelection + " AS LinkedGameObjectEntry, " +
                linkedGameObjectFieldSelection + " AS LinkedGameObjectField " +
                "FROM " + spawn + " AS s" + templateJoin +
                " WHERE 1 = 1" + mapFilter + eventExclusion +
                " ORDER BY " + map + " ASC, " + guid + " ASC;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, spawnTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            if (request.MapId.HasValue)
            {
                command.Parameters.AddWithValue("@mapId", request.MapId.Value);
            }

            Dictionary<BucketKey, List<DoubleSpawnPoint>> buckets = new();
            int? currentMap = null;
            string kindLabel = string.Equals(kind, "Creature", StringComparison.OrdinalIgnoreCase) ? "creature" : "GameObject";
            progress?.Report(new OperationProgressUpdate(
                "Analyzing " + kindLabel + " positions",
                "Reading " + kindLabel + " spawns and comparing nearby 3D positions."));

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (!accumulator.IsTruncated && await reader.ReadAsync(cancellationToken))
            {
                long spawnGuid = GetInt64(reader, "SpawnGuid");
                long spawnEntry = GetInt64(reader, "SpawnEntry");
                int? gameObjectType = GetNullableInt32(reader, "GameObjectType");
                long? linkedGameObjectEntry = GetNullableInt64(reader, "LinkedGameObjectEntry");
                if (linkedGameObjectEntry.GetValueOrDefault() <= 0L)
                {
                    linkedGameObjectEntry = null;
                }

                DoubleSpawnPoint point = new()
                {
                    Kind = kind,
                    Guid = spawnGuid,
                    Entry = spawnEntry,
                    Name = GetString(reader, "SpawnName"),
                    MapId = GetInt32(reader, "MapId"),
                    PositionX = GetDouble(reader, "PositionX"),
                    PositionY = GetDouble(reader, "PositionY"),
                    PositionZ = GetDouble(reader, "PositionZ"),
                    IsEventRelated = GetBoolean(reader, "IsEventRelated"),
                    GameObjectType = gameObjectType,
                    GameObjectTypeName = GetGameObjectTypeName(gameObjectType),
                    LinkedGameObjectEntry = linkedGameObjectEntry,
                    LinkedGameObjectField = GetString(reader, "LinkedGameObjectField"),
                    Pools = poolLookup.Resolve(spawnGuid, spawnEntry)
                };

                if (!currentMap.HasValue || currentMap.Value != point.MapId)
                {
                    buckets.Clear();
                    currentMap = point.MapId;
                }

                accumulator.RecordSpawn(point);
                CompareAndAdd(point, request, buckets, accumulator);

                long scanned = string.Equals(kind, "Creature", StringComparison.OrdinalIgnoreCase)
                    ? accumulator.CreatureSpawnsScanned
                    : accumulator.GameObjectSpawnsScanned;
                if ((scanned & 255L) == 0L)
                {
                    progress?.Report(new OperationProgressUpdate(
                        "Analyzing " + kindLabel + " positions",
                        scanned.ToString("N0", CultureInfo.InvariantCulture) + " " + kindLabel + " spawns examined; currently on map " +
                        point.MapId.ToString(CultureInfo.InvariantCulture) + ". " +
                        accumulator.TotalMatches.ToString("N0", CultureInfo.InvariantCulture) + " matching pairs retained.",
                        scanned,
                        null,
                        "spawns"));
                }
            }

            long finalScanned = string.Equals(kind, "Creature", StringComparison.OrdinalIgnoreCase)
                ? accumulator.CreatureSpawnsScanned
                : accumulator.GameObjectSpawnsScanned;
            progress?.Report(new OperationProgressUpdate(
                "Finished " + kindLabel + " analysis",
                finalScanned.ToString("N0", CultureInfo.InvariantCulture) + " " + kindLabel + " spawns examined."));
        }

        // Load pool reference data using schema-aware database operations.
        private async Task<PoolReferenceData> LoadPoolReferenceDataAsync(
            ConnectionProfile profile,
            TableMetadata? poolTemplate,
            TableMetadata? poolPool,
            CancellationToken cancellationToken)
        {
            PoolReferenceData result = new();

            if (poolTemplate != null)
            {
                ColumnMetadata? entryColumn = Find(poolTemplate, "entry", "pool_entry", "pool_id", "id");
                if (entryColumn != null)
                {
                    ColumnMetadata? maxLimitColumn = Find(poolTemplate, "max_limit", "maxlimit", "limit");
                    ColumnMetadata? descriptionColumn = Find(poolTemplate, "description", "comment");
                    string maxLimitSelection = maxLimitColumn == null ? "NULL" : MySqlIdentifier.Quote(maxLimitColumn.Name);
                    string descriptionSelection = descriptionColumn == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(descriptionColumn.Name) + ", '')";
                    string sql =
                        "SELECT " + MySqlIdentifier.Quote(entryColumn.Name) + " AS PoolEntry, " +
                        maxLimitSelection + " AS MaxLimit, " + descriptionSelection + " AS PoolDescription " +
                        "FROM " + Qualify(poolTemplate) + ";";

                    await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
                    await connection.OpenAsync(cancellationToken);
                    await using MySqlCommand command = new(sql, connection);
                    await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long entry = GetInt64(reader, "PoolEntry");
                        result.Templates[entry] = new PoolTemplateInfo
                        {
                            MaxLimit = GetNullableInt32(reader, "MaxLimit"),
                            Description = GetString(reader, "PoolDescription")
                        };
                    }
                }
            }

            if (poolPool != null)
            {
                ColumnMetadata? childColumn = Find(poolPool, "pool_id", "pool_entry", "entry", "child_pool");
                ColumnMetadata? motherColumn = Find(poolPool, "mother_pool", "motherpool", "mother_pool_entry", "parent_pool");
                if (childColumn != null && motherColumn != null)
                {
                    ColumnMetadata? chanceColumn = Find(poolPool, "chance");
                    ColumnMetadata? descriptionColumn = Find(poolPool, "description", "comment");
                    string chanceSelection = chanceColumn == null ? "NULL" : MySqlIdentifier.Quote(chanceColumn.Name);
                    string descriptionSelection = descriptionColumn == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(descriptionColumn.Name) + ", '')";
                    string sql =
                        "SELECT " + MySqlIdentifier.Quote(childColumn.Name) + " AS ChildPoolEntry, " +
                        MySqlIdentifier.Quote(motherColumn.Name) + " AS MotherPoolEntry, " +
                        chanceSelection + " AS MembershipChance, " + descriptionSelection + " AS MembershipDescription " +
                        "FROM " + Qualify(poolPool) + ";";

                    await using MySqlConnection connection = _connectionFactory.Create(profile, poolPool.DatabaseName);
                    await connection.OpenAsync(cancellationToken);
                    await using MySqlCommand command = new(sql, connection);
                    await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long childPoolEntry = GetInt64(reader, "ChildPoolEntry");
                        long motherPoolEntry = GetInt64(reader, "MotherPoolEntry");
                        result.Templates.TryGetValue(motherPoolEntry, out PoolTemplateInfo? motherTemplate);

                        DoubleSpawnPoolParent parent = new()
                        {
                            PoolEntry = motherPoolEntry,
                            Chance = GetNullableDouble(reader, "MembershipChance"),
                            MembershipDescription = GetString(reader, "MembershipDescription"),
                            MaxLimit = motherTemplate?.MaxLimit,
                            PoolDescription = motherTemplate?.Description ?? string.Empty
                        };

                        if (!result.Parents.TryGetValue(childPoolEntry, out List<DoubleSpawnPoolParent>? parents))
                        {
                            parents = new List<DoubleSpawnPoolParent>();
                            result.Parents.Add(childPoolEntry, parents);
                        }

                        parents.Add(parent);
                    }
                }
            }

            return result;
        }

        // Load pool membership lookup using schema-aware database operations.
        private async Task<PoolMembershipLookup> LoadPoolMembershipLookupAsync(
            ConnectionProfile profile,
            TableMetadata? directPoolTable,
            TableMetadata? templatePoolTable,
            PoolReferenceData references,
            string kind,
            List<string> warnings,
            CancellationToken cancellationToken)
        {
            PoolMembershipLookup result = new();
            string entityLabel = string.Equals(kind, "Creature", StringComparison.OrdinalIgnoreCase) ? "creature" : "gameobject";

            if (directPoolTable == null && templatePoolTable == null)
            {
                warnings.Add("No " + entityLabel + " pool membership table is available, so pooled " + entityLabel + " matches cannot be identified.");
                return result;
            }

            if (directPoolTable == null)
            {
                warnings.Add("pool_" + entityLabel + " is not available, so GUID-level " + entityLabel + " pool membership cannot be identified.");
            }
            else
            {
                bool loaded = await LoadPoolMembershipTableAsync(
                    profile,
                    directPoolTable,
                    true,
                    references,
                    result,
                    cancellationToken);
                if (!loaded)
                {
                    warnings.Add("Table '" + directPoolTable.Name + "' does not expose the expected GUID/pool columns, so GUID-level pool membership cannot be identified.");
                }
            }

            if (templatePoolTable == null)
            {
                warnings.Add("pool_" + entityLabel + "_template is not available, so template-level " + entityLabel + " pool membership cannot be identified.");
            }
            else
            {
                bool loaded = await LoadPoolMembershipTableAsync(
                    profile,
                    templatePoolTable,
                    false,
                    references,
                    result,
                    cancellationToken);
                if (!loaded)
                {
                    warnings.Add("Table '" + templatePoolTable.Name + "' does not expose the expected entry/pool columns, so template-level pool membership cannot be identified.");
                }
            }

            result.Sort();
            return result;
        }

        // Load pool membership table using schema-aware database operations.
        private async Task<bool> LoadPoolMembershipTableAsync(
            ConnectionProfile profile,
            TableMetadata table,
            bool directGuidMembership,
            PoolReferenceData references,
            PoolMembershipLookup destination,
            CancellationToken cancellationToken)
        {
            ColumnMetadata? memberColumn = directGuidMembership
                ? Find(table, "guid")
                : Find(table, "id", "entry");
            ColumnMetadata? poolColumn = Find(table, "pool_entry", "poolentry", "pool_id", "poolid", "pool");
            if (memberColumn == null || poolColumn == null)
            {
                return false;
            }

            ColumnMetadata? chanceColumn = Find(table, "chance");
            ColumnMetadata? descriptionColumn = Find(table, "description", "comment");
            string chanceSelection = chanceColumn == null ? "NULL" : MySqlIdentifier.Quote(chanceColumn.Name);
            string descriptionSelection = descriptionColumn == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(descriptionColumn.Name) + ", '')";
            string sql =
                "SELECT " + MySqlIdentifier.Quote(memberColumn.Name) + " AS MemberKey, " +
                MySqlIdentifier.Quote(poolColumn.Name) + " AS PoolEntry, " +
                chanceSelection + " AS MembershipChance, " + descriptionSelection + " AS MembershipDescription " +
                "FROM " + Qualify(table) + ";";

            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long memberKey = GetInt64(reader, "MemberKey");
                long poolEntry = GetInt64(reader, "PoolEntry");
                references.Templates.TryGetValue(poolEntry, out PoolTemplateInfo? template);
                references.Parents.TryGetValue(poolEntry, out List<DoubleSpawnPoolParent>? parents);

                DoubleSpawnPoolMembership membership = new()
                {
                    PoolEntry = poolEntry,
                    MembershipType = directGuidMembership ? "Spawn GUID" : "Template entry",
                    SourceTable = table.Name,
                    Chance = GetNullableDouble(reader, "MembershipChance"),
                    MembershipDescription = GetString(reader, "MembershipDescription"),
                    MaxLimit = template?.MaxLimit,
                    PoolDescription = template?.Description ?? string.Empty,
                    ParentPools = parents == null
                        ? Array.Empty<DoubleSpawnPoolParent>()
                        : parents.ToArray()
                };

                destination.Add(memberKey, membership, directGuidMembership);
            }

            return true;
        }

        // Determine whether inspect linked game objects using schema-aware database operations.
        private static bool CanInspectLinkedGameObjects(TableMetadata? templateTable)
        {
            if (templateTable == null || Find(templateTable, "entry", "id") == null || Find(templateTable, "type") == null)
            {
                return false;
            }

            return Find(templateTable, "data2") != null ||
                   Find(templateTable, "data3") != null ||
                   Find(templateTable, "data7") != null ||
                   Find(templateTable, "data12") != null;
        }

        // Build linked game object selections using schema-aware database operations.
        private static void BuildLinkedGameObjectSelections(
            SpawnSchema schema,
            out string linkedEntrySelection,
            out string linkedFieldSelection)
        {
            if (schema.TemplateType == null)
            {
                linkedEntrySelection = "NULL";
                linkedFieldSelection = "''";
                return;
            }

            string type = Column("t", schema.TemplateType);
            List<string> entryCases = new();
            List<string> fieldCases = new();

            AddLinkedGameObjectCase(entryCases, fieldCases, type, 1, schema.TemplateData3, "data3");   // Button
            AddLinkedGameObjectCase(entryCases, fieldCases, type, 3, schema.TemplateData7, "data7");   // Chest
            AddLinkedGameObjectCase(entryCases, fieldCases, type, 8, schema.TemplateData2, "data2");   // Spell focus
            AddLinkedGameObjectCase(entryCases, fieldCases, type, 10, schema.TemplateData12, "data12"); // Goober

            if (entryCases.Count == 0)
            {
                linkedEntrySelection = "NULL";
                linkedFieldSelection = "''";
                return;
            }

            linkedEntrySelection = "CASE " + string.Join(" ", entryCases) + " ELSE 0 END";
            linkedFieldSelection = "CASE " + string.Join(" ", fieldCases) + " ELSE '' END";
        }

        // Add linked game object case using schema-aware database operations.
        private static void AddLinkedGameObjectCase(
            List<string> entryCases,
            List<string> fieldCases,
            string typeExpression,
            int gameObjectType,
            ColumnMetadata? dataColumn,
            string fieldName)
        {
            if (dataColumn == null)
            {
                return;
            }

            entryCases.Add("WHEN " + typeExpression + " = " + gameObjectType.ToString(CultureInfo.InvariantCulture) +
                " THEN COALESCE(" + Column("t", dataColumn) + ", 0)");
            fieldCases.Add("WHEN " + typeExpression + " = " + gameObjectType.ToString(CultureInfo.InvariantCulture) +
                " THEN '" + fieldName + "'");
        }

        // Build linked game object relation using schema-aware database operations.
        private static DoubleSpawnLinkedGameObjectRelation? BuildLinkedGameObjectRelation(
            DoubleSpawnPoint first,
            DoubleSpawnPoint second)
        {
            if (!string.Equals(first.Kind, "GameObject", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(second.Kind, "GameObject", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (first.LinkedGameObjectEntry == second.Entry)
            {
                return CreateLinkedGameObjectRelation(
                    first,
                    second,
                    second.LinkedGameObjectEntry == first.Entry);
            }

            if (second.LinkedGameObjectEntry == first.Entry)
            {
                return CreateLinkedGameObjectRelation(
                    second,
                    first,
                    first.LinkedGameObjectEntry == second.Entry);
            }

            return null;
        }

        // Create linked game object relation using schema-aware database operations.
        private static DoubleSpawnLinkedGameObjectRelation CreateLinkedGameObjectRelation(
            DoubleSpawnPoint source,
            DoubleSpawnPoint target,
            bool reciprocal)
        {
            return new DoubleSpawnLinkedGameObjectRelation
            {
                SourceGuid = source.Guid,
                SourceEntry = source.Entry,
                SourceType = source.GameObjectType,
                SourceTypeName = source.GameObjectTypeName,
                SourceField = source.LinkedGameObjectField,
                LinkedEntry = source.LinkedGameObjectEntry.GetValueOrDefault(),
                TargetGuid = target.Guid,
                TargetEntry = target.Entry,
                TargetType = target.GameObjectType,
                TargetTypeName = target.GameObjectTypeName,
                IsReciprocal = reciprocal
            };
        }

        // Read game object type name using schema-aware database operations.
        private static string GetGameObjectTypeName(int? type)
        {
            if (!type.HasValue)
            {
                return string.Empty;
            }

            return type.Value switch
            {
                0 => "Door",
                1 => "Button",
                2 => "Questgiver",
                3 => "Chest",
                4 => "Binder",
                5 => "Generic",
                6 => "Trap",
                7 => "Chair",
                8 => "Spell Focus",
                9 => "Text",
                10 => "Goober",
                11 => "Transport",
                12 => "Area Damage",
                13 => "Camera",
                14 => "Map Object",
                15 => "MO Transport",
                16 => "Duel Arbiter",
                17 => "Fishing Node",
                18 => "Ritual",
                19 => "Mailbox",
                20 => "Auction House",
                21 => "Guard Post",
                22 => "Spell Caster",
                23 => "Meeting Stone",
                24 => "Flag Stand",
                25 => "Fishing Hole",
                26 => "Flag Drop",
                27 => "Mini Game",
                28 => "Lottery Kiosk",
                29 => "Capture Point",
                30 => "Aura Generator",
                31 => "Dungeon Difficulty",
                32 => "Barber Chair",
                33 => "Destructible Building",
                34 => "Guild Bank",
                35 => "Trapdoor",
                _ => "Type " + type.Value.ToString(CultureInfo.InvariantCulture)
            };
        }

        // Handle compare and add within the world double spawn service workflow.
        private static void CompareAndAdd(
            DoubleSpawnPoint point,
            DoubleSpawnSearchRequest request,
            Dictionary<BucketKey, List<DoubleSpawnPoint>> buckets,
            SearchAccumulator accumulator)
        {
            bool exact = request.DistanceTolerance <= 0d;
            long entryKey = request.MatchMode == DoubleSpawnMatchModes.SameEntry ? point.Entry : 0L;
            BucketKey center = CreateBucket(point, request.DistanceTolerance, entryKey);
            double toleranceSquared = request.DistanceTolerance * request.DistanceTolerance;

            int neighborRange = exact ? 0 : 1;
            for (int dx = -neighborRange; dx <= neighborRange && !accumulator.IsTruncated; dx++)
            {
                for (int dy = -neighborRange; dy <= neighborRange && !accumulator.IsTruncated; dy++)
                {
                    for (int dz = -neighborRange; dz <= neighborRange && !accumulator.IsTruncated; dz++)
                    {
                        BucketKey key = new(center.X + dx, center.Y + dy, center.Z + dz, entryKey);
                        if (!buckets.TryGetValue(key, out List<DoubleSpawnPoint>? candidates))
                        {
                            continue;
                        }

                        foreach (DoubleSpawnPoint candidate in candidates)
                        {
                            if (request.MatchMode == DoubleSpawnMatchModes.SameEntry && candidate.Entry != point.Entry)
                            {
                                continue;
                            }

                            double deltaX = candidate.PositionX - point.PositionX;
                            double deltaY = candidate.PositionY - point.PositionY;
                            double deltaZ = candidate.PositionZ - point.PositionZ;
                            double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
                            bool isMatch = exact
                                ? distanceSquared == 0d
                                : distanceSquared <= toleranceSquared;
                            if (!isMatch)
                            {
                                continue;
                            }

                            accumulator.RecordMatch(new DoubleSpawnMatch
                            {
                                Kind = point.Kind,
                                First = candidate,
                                Second = point,
                                Distance = Math.Sqrt(distanceSquared),
                                LinkedGameObjectRelation = BuildLinkedGameObjectRelation(candidate, point)
                            });

                            if (accumulator.IsTruncated)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            if (!buckets.TryGetValue(center, out List<DoubleSpawnPoint>? bucket))
            {
                bucket = new List<DoubleSpawnPoint>();
                buckets.Add(center, bucket);
            }

            bucket.Add(point);
        }

        // Create bucket using schema-aware database operations.
        private static BucketKey CreateBucket(DoubleSpawnPoint point, double tolerance, long entryKey)
        {
            if (tolerance <= 0d)
            {
                return new BucketKey(
                    BitConverter.DoubleToInt64Bits(point.PositionX),
                    BitConverter.DoubleToInt64Bits(point.PositionY),
                    BitConverter.DoubleToInt64Bits(point.PositionZ),
                    entryKey);
            }

            return new BucketKey(
                ToBucketCoordinate(point.PositionX, tolerance),
                ToBucketCoordinate(point.PositionY, tolerance),
                ToBucketCoordinate(point.PositionZ, tolerance),
                entryKey);
        }

        // Handle to bucket coordinate within the world double spawn service workflow.
        private static long ToBucketCoordinate(double value, double tolerance)
        {
            return checked((long)Math.Floor(value / tolerance));
        }

        // Add map ids using schema-aware database operations.
        private static async Task AddMapIdsAsync(
            MySqlConnection connection,
            TableMetadata table,
            SortedSet<int> destination,
            CancellationToken cancellationToken)
        {
            ColumnMetadata map = Required(table, "map", "mapid", "map_id");
            string sql = "SELECT DISTINCT " + MySqlIdentifier.Quote(map.Name) + " AS MapId FROM " + Qualify(table) + " ORDER BY " + MySqlIdentifier.Quote(map.Name) + ";";
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                destination.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
            }
        }

        // Normalize scope using schema-aware database operations.
        private static string NormalizeScope(string? value)
        {
            if (string.Equals(value, DoubleSpawnScopes.Creatures, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnScopes.Creatures;
            }

            if (string.Equals(value, DoubleSpawnScopes.GameObjects, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnScopes.GameObjects;
            }

            return DoubleSpawnScopes.Both;
        }

        // Normalize match mode using schema-aware database operations.
        private static string NormalizeMatchMode(string? value)
        {
            return string.Equals(value, DoubleSpawnMatchModes.AnyEntry, StringComparison.OrdinalIgnoreCase)
                ? DoubleSpawnMatchModes.AnyEntry
                : DoubleSpawnMatchModes.SameEntry;
        }

        // Normalize pool filter using schema-aware database operations.
        private static string NormalizePoolFilter(string? value)
        {
            if (string.Equals(value, DoubleSpawnPoolFilters.Pooled, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnPoolFilters.Pooled;
            }

            if (string.Equals(value, DoubleSpawnPoolFilters.NonPooled, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnPoolFilters.NonPooled;
            }

            return DoubleSpawnPoolFilters.All;
        }

        // Normalize linked game object filter using schema-aware database operations.
        private static string NormalizeLinkedGameObjectFilter(string? value)
        {
            if (string.Equals(value, DoubleSpawnLinkedGameObjectFilters.Linked, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnLinkedGameObjectFilters.Linked;
            }

            if (string.Equals(value, DoubleSpawnLinkedGameObjectFilters.NonLinked, StringComparison.OrdinalIgnoreCase))
            {
                return DoubleSpawnLinkedGameObjectFilters.NonLinked;
            }

            return DoubleSpawnLinkedGameObjectFilters.All;
        }

        // Read int64 using schema-aware database operations.
        private static long GetInt64(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // Read int32 using schema-aware database operations.
        private static int GetInt32(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        // Read nullable int64 using schema-aware database operations.
        private static long? GetNullableInt64(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? (long?)null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // Read nullable int32 using schema-aware database operations.
        private static int? GetNullableInt32(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? (int?)null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        // Read double using schema-aware database operations.
        private static double GetDouble(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // Read nullable double using schema-aware database operations.
        private static double? GetNullableDouble(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? (double?)null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        // Read string using schema-aware database operations.
        private static string GetString(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // Read boolean using schema-aware database operations.
        private static bool GetBoolean(DbDataReader reader, string name)
        {
            object value = reader[name];
            return value != DBNull.Value && Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0L;
        }

        // Build a table-alias-qualified, safely quoted column name.
        private static string Column(string alias, ColumnMetadata column)
        {
            return MySqlIdentifier.Quote(alias) + "." + MySqlIdentifier.Quote(column.Name);
        }

        // Quote and combine the database and table names as a qualified SQL identifier.
        private static string Qualify(TableMetadata table)
        {
            return MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
        }

        // Handle required within the world double spawn service workflow.
        private static ColumnMetadata Required(TableMetadata table, params string[] candidates)
        {
            return Find(table, candidates)
                ?? throw new InvalidOperationException("Table '" + table.Name + "' does not expose the required column " + string.Join("/", candidates) + ".");
        }

        // Locate a compatible column from the available schema metadata.
        private static ColumnMetadata? Find(TableMetadata table, params string[] candidates)
        {
            return table.Columns.FirstOrDefault(column => candidates.Any(candidate => string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)));
        }

        private sealed class SpawnSchema
        {
            public ColumnMetadata Guid { get; private set; } = null!;
            public ColumnMetadata Entry { get; private set; } = null!;
            public ColumnMetadata Map { get; private set; } = null!;
            public ColumnMetadata PositionX { get; private set; } = null!;
            public ColumnMetadata PositionY { get; private set; } = null!;
            public ColumnMetadata PositionZ { get; private set; } = null!;
            public ColumnMetadata? TemplateEntry
            {
                get; private set;
            }

            public ColumnMetadata? TemplateName
            {
                get; private set;
            }

            public ColumnMetadata? TemplateType
            {
                get; private set;
            }

            public ColumnMetadata? TemplateData2
            {
                get; private set;
            }

            public ColumnMetadata? TemplateData3
            {
                get; private set;
            }

            public ColumnMetadata? TemplateData7
            {
                get; private set;
            }

            public ColumnMetadata? TemplateData12
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static SpawnSchema Create(TableMetadata spawn, TableMetadata? template)
            {
                return new SpawnSchema
                {
                    Guid = Required(spawn, "guid"),
                    Entry = Required(spawn, "id", "entry"),
                    Map = Required(spawn, "map", "mapid", "map_id"),
                    PositionX = Required(spawn, "position_x", "positionx", "pos_x", "x"),
                    PositionY = Required(spawn, "position_y", "positiony", "pos_y", "y"),
                    PositionZ = Required(spawn, "position_z", "positionz", "pos_z", "z"),
                    TemplateEntry = template == null ? null : Find(template, "entry", "id"),
                    TemplateName = template == null ? null : Find(template, "name"),
                    TemplateType = template == null ? null : Find(template, "type"),
                    TemplateData2 = template == null ? null : Find(template, "data2"),
                    TemplateData3 = template == null ? null : Find(template, "data3"),
                    TemplateData7 = template == null ? null : Find(template, "data7"),
                    TemplateData12 = template == null ? null : Find(template, "data12")
                };
            }
        }

        private sealed class PoolTemplateInfo
        {
            public int? MaxLimit
            {
                get; set;
            }

            public string Description { get; set; } = string.Empty;
        }

        private sealed class PoolReferenceData
        {
            public Dictionary<long, PoolTemplateInfo> Templates { get; } = new Dictionary<long, PoolTemplateInfo>();
            public Dictionary<long, List<DoubleSpawnPoolParent>> Parents { get; } = new Dictionary<long, List<DoubleSpawnPoolParent>>();
        }

        private sealed class PoolMembershipLookup
        {
            private readonly Dictionary<long, List<DoubleSpawnPoolMembership>> _byGuid = new();
            private readonly Dictionary<long, List<DoubleSpawnPoolMembership>> _byEntry = new();

            public int MembershipCount => _byGuid.Values.Sum(items => items.Count) + _byEntry.Values.Sum(items => items.Count);

            // Handle add within the world double spawn service workflow.
            public void Add(long key, DoubleSpawnPoolMembership membership, bool directGuidMembership)
            {
                Dictionary<long, List<DoubleSpawnPoolMembership>> destination = directGuidMembership ? _byGuid : _byEntry;
                if (!destination.TryGetValue(key, out List<DoubleSpawnPoolMembership>? memberships))
                {
                    memberships = new List<DoubleSpawnPoolMembership>();
                    destination.Add(key, memberships);
                }

                memberships.Add(membership);
            }

            // Handle resolve within the world double spawn service workflow.
            public IReadOnlyList<DoubleSpawnPoolMembership> Resolve(long guid, long entry)
            {
                _byGuid.TryGetValue(guid, out List<DoubleSpawnPoolMembership>? guidMemberships);
                _byEntry.TryGetValue(entry, out List<DoubleSpawnPoolMembership>? entryMemberships);

                if (guidMemberships == null && entryMemberships == null)
                {
                    return Array.Empty<DoubleSpawnPoolMembership>();
                }

                if (guidMemberships != null && entryMemberships == null)
                {
                    return guidMemberships;
                }

                if (guidMemberships == null && entryMemberships != null)
                {
                    return entryMemberships;
                }

                List<DoubleSpawnPoolMembership> combined = new(guidMemberships!.Count + entryMemberships!.Count);
                combined.AddRange(guidMemberships);
                combined.AddRange(entryMemberships);
                return combined;
            }

            // Handle sort within the world double spawn service workflow.
            public void Sort()
            {
                foreach (List<DoubleSpawnPoolMembership> memberships in _byGuid.Values)
                {
                    memberships.Sort(CompareMemberships);
                }

                foreach (List<DoubleSpawnPoolMembership> memberships in _byEntry.Values)
                {
                    memberships.Sort(CompareMemberships);
                }
            }

            // Handle compare memberships within the world double spawn service workflow.
            private static int CompareMemberships(DoubleSpawnPoolMembership left, DoubleSpawnPoolMembership right)
            {
                int poolComparison = left.PoolEntry.CompareTo(right.PoolEntry);
                return poolComparison != 0
                    ? poolComparison
                    : string.Compare(left.MembershipType, right.MembershipType, StringComparison.OrdinalIgnoreCase);
            }
        }

        private readonly struct BucketKey : IEquatable<BucketKey>
        {
            // Handle bucket key within the world double spawn service workflow.
            public BucketKey(long x, long y, long z, long entry)
            {
                X = x;
                Y = y;
                Z = z;
                Entry = entry;
            }

            public long X
            {
                get;
            }

            public long Y
            {
                get;
            }

            public long Z
            {
                get;
            }

            public long Entry
            {
                get;
            }

            // Compare this model with another instance using its value identity.
            public bool Equals(BucketKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z && Entry == other.Entry;
            }

            // Compare this model with another instance using its value identity.
            public override bool Equals(object? obj)
            {
                return obj is BucketKey other && Equals(other);
            }

            // Read hash code using schema-aware database operations.
            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y, Z, Entry);
            }
        }

        private sealed class SearchAccumulator
        {
            private readonly long _pageStart;
            private readonly long _pageEnd;
            private readonly long _maximumMatches;
            private readonly string _poolFilter;
            private readonly string _linkedGameObjectFilter;
            private readonly string _search;
            private long _matchingPairsExamined;

            // Search for accumulator using schema-aware database operations.
            public SearchAccumulator(
                int page,
                int pageSize,
                long maximumMatches,
                string poolFilter,
                string linkedGameObjectFilter,
                string search)
            {
                _pageStart = checked((long)(page - 1) * pageSize);
                _pageEnd = checked(_pageStart + pageSize);
                _maximumMatches = maximumMatches;
                _poolFilter = poolFilter;
                _linkedGameObjectFilter = linkedGameObjectFilter;
                _search = search ?? string.Empty;
            }

            public List<DoubleSpawnMatch> PageItems { get; } = new List<DoubleSpawnMatch>();
            public HashSet<int> MapIds { get; } = new HashSet<int>();
            public long TotalMatches
            {
                get; private set;
            }

            public long CreatureMatches
            {
                get; private set;
            }

            public long GameObjectMatches
            {
                get; private set;
            }

            public long PooledMatches
            {
                get; private set;
            }

            public long LinkedGameObjectMatches
            {
                get; private set;
            }

            public long CreatureSpawnsScanned
            {
                get; private set;
            }

            public long GameObjectSpawnsScanned
            {
                get; private set;
            }

            public bool IsTruncated
            {
                get; private set;
            }

            // Handle record spawn within the world double spawn service workflow.
            public void RecordSpawn(DoubleSpawnPoint point)
            {
                MapIds.Add(point.MapId);
                if (string.Equals(point.Kind, "Creature", StringComparison.OrdinalIgnoreCase))
                {
                    CreatureSpawnsScanned++;
                }
                else
                {
                    GameObjectSpawnsScanned++;
                }
            }

            // Handle record match within the world double spawn service workflow.
            public void RecordMatch(DoubleSpawnMatch match)
            {
                if (_matchingPairsExamined >= _maximumMatches)
                {
                    IsTruncated = true;
                    return;
                }

                _matchingPairsExamined++;
                bool isPooled = match.First.IsPooled || match.Second.IsPooled;
                bool isLinkedGameObjectPair = match.IsLinkedGameObjectPair;
                bool matchesSearch = string.IsNullOrWhiteSpace(_search) ||
                    (!string.IsNullOrWhiteSpace(match.First.Name) && match.First.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(match.Second.Name) && match.Second.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));
                if (!matchesSearch ||
                    (_poolFilter == DoubleSpawnPoolFilters.Pooled && !isPooled) ||
                    (_poolFilter == DoubleSpawnPoolFilters.NonPooled && isPooled) ||
                    (_linkedGameObjectFilter == DoubleSpawnLinkedGameObjectFilters.Linked && !isLinkedGameObjectPair) ||
                    (_linkedGameObjectFilter == DoubleSpawnLinkedGameObjectFilters.NonLinked && isLinkedGameObjectPair))
                {
                    if (_matchingPairsExamined >= _maximumMatches)
                    {
                        IsTruncated = true;
                    }

                    return;
                }

                long index = TotalMatches;
                TotalMatches++;
                if (string.Equals(match.Kind, "Creature", StringComparison.OrdinalIgnoreCase))
                {
                    CreatureMatches++;
                }
                else
                {
                    GameObjectMatches++;
                }

                if (isPooled)
                {
                    PooledMatches++;
                }

                if (isLinkedGameObjectPair)
                {
                    LinkedGameObjectMatches++;
                }

                if (index >= _pageStart && index < _pageEnd)
                {
                    PageItems.Add(match);
                }

                if (_matchingPairsExamined >= _maximumMatches)
                {
                    IsTruncated = true;
                }
            }
        }
    }
}
