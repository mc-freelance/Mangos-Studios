// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Handle world database editors, viewers, and related authorized mutations.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.Domain.World;
using MangosDatabaseStudio.Infrastructure.MySql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.WorldBrowse)]
    public sealed class WorldEditorController : StudioController
    {
        private readonly IMySqlSchemaService _schema;
        private readonly IGenericTableService _tables;
        private readonly IMangosSchemaCatalog _catalog;
        private readonly IMangosFieldDataCatalog _fieldDataCatalog;
        private readonly INpcVendorService _npcVendors;
        private readonly INpcTrainerService _npcTrainers;
        private readonly IPoolViewerService _pools;
        private readonly ILootViewerService _loot;
        private readonly IGossipViewerService _gossip;
        private readonly IEventViewerService _events;
        private readonly IQuestViewerService _quests;
        private readonly IWorldDoubleSpawnService _doubleSpawns;
        private readonly IApplicationUserStore _users;
        private readonly IAuthorizationService _authorization;
        private readonly IChangeStorageStore _changeStorage;
        private readonly OperationProgressReporter _operationProgress;
        private static readonly string[] sourceArray = new[] { "creature_template", "npc_trainer" };
        private static readonly string[] sourceArray0 = new[]
                {
                    "quest_template", "quest_relations", "creature_questrelation", "creature_involvedrelation",
                    "gameobject_questrelation", "gameobject_involvedrelation", "creature_template", "gameobject_template"
                };

        // Initialize the world editor controller dependencies and state.
        public WorldEditorController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            IGenericTableService tables,
            IMangosSchemaCatalog catalog,
            IMangosFieldDataCatalog fieldDataCatalog,
            INpcVendorService npcVendors,
            INpcTrainerService npcTrainers,
            IPoolViewerService pools,
            ILootViewerService loot,
            IGossipViewerService gossip,
            IEventViewerService events,
            IQuestViewerService quests,
            IWorldDoubleSpawnService doubleSpawns,
            IApplicationUserStore users,
            IAuthorizationService authorization,
            IChangeStorageStore changeStorage,
            OperationProgressReporter operationProgress)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _tables = tables;
            _catalog = catalog;
            _fieldDataCatalog = fieldDataCatalog;
            _npcVendors = npcVendors;
            _npcTrainers = npcTrainers;
            _pools = pools;
            _loot = loot;
            _gossip = gossip;
            _events = events;
            _quests = quests;
            _doubleSpawns = doubleSpawns;
            _users = users;
            _authorization = authorization;
            _changeStorage = changeStorage;
            _operationProgress = operationProgress;
        }

        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(string? database, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tables, cancellationToken);
            WorldIndexViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                CatalogDescription = schemaProfile.Description,
                TableCount = tables.Count,
                Categories = _catalog.Categorize(tables.Select(table => table.Name)),
                CanBrowseDatabaseStructure = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureView)).Succeeded,
                HasNpcVendorViewer = HasTables(tables, "creature_template", "npc_vendor", "item_template"),
                HasNpcTrainerViewer = HasTables(tables, "creature_template", "npc_trainer")
            };
            return View(model);
        }

        [HttpGet]
        // Handle npc vendors within the world editor controller workflow.
        public async Task<IActionResult> NpcVendors(
            string? database,
            string? search = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int itemPage = 1,
            int? itemPageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tables, cancellationToken);
            ulong vendorFlagMask = MangosNpcFlagMasks.ResolveVendorMask(schemaProfile);
            string[] requiredTables = { "creature_template", "npc_vendor", "item_template" };
            Dictionary<string, TableMetadata> tablesByName = tables.ToDictionary(
                table => table.Name,
                StringComparer.OrdinalIgnoreCase);
            List<string> missingTables = requiredTables
                .Where(required => !tablesByName.ContainsKey(required))
                .ToList();
            bool hasSearched = show || entry.HasValue || search != null;
            string normalizedSearch = search?.Trim() ?? string.Empty;
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveItemPageSize = NormalizePageSize(itemPageSize ?? preferences.DefaultPageSize);

            NpcVendorViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                VendorFlagMask = vendorFlagMask,
                VendorFlagMaskDisplay = MangosNpcFlagMasks.FormatHex(vendorFlagMask),
                Search = normalizedSearch,
                HasSearched = hasSearched,
                IsAvailable = missingTables.Count == 0,
                MissingTables = missingTables,
                SelectedEntry = entry,
                Vendors = new PagedResult<NpcVendorSummary>
                {
                    Page = 1,
                    PageSize = effectivePageSize
                }
            };

            if (!model.IsAvailable || !hasSearched)
            {
                return View(model);
            }

            // GetTablesAsync intentionally returns lightweight table summaries for the
            // World Editor landing page. The specialized viewer must hydrate each table
            // through the same schema-aware metadata path used by raw-data browsing so
            // the query is built from the columns actually exposed by this database.
            TableMetadata creatureTemplate = await _schema.GetTableAsync(
                profile,
                database,
                tablesByName["creature_template"].Name,
                cancellationToken);
            TableMetadata npcVendor = await _schema.GetTableAsync(
                profile,
                database,
                tablesByName["npc_vendor"].Name,
                cancellationToken);
            TableMetadata itemTemplate = await _schema.GetTableAsync(
                profile,
                database,
                tablesByName["item_template"].Name,
                cancellationToken);

            model.EditorCapabilities = _npcVendors.GetEditorCapabilities(npcVendor);
            model.CanAddVendorItems = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            model.CanEditVendorItems = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            model.CanDeleteVendorItems = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;

            int effectivePage = Math.Max(1, page);

            model.Vendors = await _npcVendors.SearchAsync(
                profile,
                creatureTemplate,
                npcVendor,
                vendorFlagMask,
                normalizedSearch,
                effectivePage,
                effectivePageSize,
                cancellationToken);

            if (model.Vendors.TotalPages > 0 && model.Vendors.Page > model.Vendors.TotalPages)
            {
                effectivePage = model.Vendors.TotalPages;
                model.Vendors = await _npcVendors.SearchAsync(
                    profile,
                    creatureTemplate,
                    npcVendor,
                    vendorFlagMask,
                    normalizedSearch,
                    effectivePage,
                    effectivePageSize,
                    cancellationToken);
            }

            if (entry.HasValue)
            {
                model.SelectedVendor = await _npcVendors.GetAsync(
                    profile,
                    creatureTemplate,
                    npcVendor,
                    itemTemplate,
                    vendorFlagMask,
                    entry.Value,
                    Math.Max(1, itemPage),
                    effectiveItemPageSize,
                    cancellationToken);

                if (model.SelectedVendor == null)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "The selected creature was not found or does not have the vendor NPC flag for this expansion.");
                }
            }

            return View(model);
        }

        [HttpGet]
        // Handle npc vendor items within the world editor controller workflow.
        public async Task<IActionResult> NpcVendorItems(
            string? database,
            string? search = null,
            int page = 1,
            int pageSize = 100,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return Unauthorized();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata itemTemplate = await _schema.GetTableAsync(
                    profile,
                    database,
                    "item_template",
                    cancellationToken);
                PagedResult<NpcVendorItemOption> result = await _npcVendors.SearchItemsAsync(
                    profile,
                    itemTemplate,
                    search,
                    Math.Max(1, page),
                    Math.Clamp(pageSize, 25, 200),
                    cancellationToken);

                return Json(new
                {
                    items = result.Items.Select(item => new
                    {
                        id = item.ItemId,
                        name = item.ItemName
                    }),
                    page = result.Page,
                    pageSize = result.PageSize,
                    totalCount = result.TotalCount,
                    totalPages = result.TotalPages
                });
            }
            catch (Exception exception)
            {
                return BadRequest(new
                {
                    error = "Unable to load item_template: " + exception.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle npc vendor add items within the world editor controller workflow.
        public async Task<IActionResult> NpcVendorAddItems(
            NpcVendorAddItemsPostModel model,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata npcVendor, TableMetadata itemTemplate) = await GetVendorMutationTablesAsync(
                    profile,
                    model.Database,
                    cancellationToken);
                NpcVendorMutationResult result = await _npcVendors.AddItemsAsync(
                    profile,
                    npcVendor,
                    itemTemplate,
                    model.Entry,
                    model.Items.Select(item => item.ToDomain()).ToArray(),
                    cancellationToken);

                string summary = result.AffectedRows == 1
                    ? "Added item to NPC vendor " + model.Entry + "."
                    : "Added " + result.AffectedRows.ToString(CultureInfo.InvariantCulture) + " items to NPC vendor " + model.Entry + ".";
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcVendorEditor, "INSERT", "npc_vendor", summary, result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = result.AffectedRows == 1
                    ? "Vendor item added."
                    : result.AffectedRows.ToString(CultureInfo.InvariantCulture) + " vendor items added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add vendor items: " + exception.Message;
            }

            return RedirectToVendor(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle npc vendor edit item within the world editor controller workflow.
        public async Task<IActionResult> NpcVendorEditItem(
            NpcVendorEditItemPostModel model,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata npcVendor, TableMetadata itemTemplate) = await GetVendorMutationTablesAsync(
                    profile,
                    model.Database,
                    cancellationToken);
                NpcVendorMutationResult result = await _npcVendors.UpdateItemAsync(
                    profile,
                    npcVendor,
                    itemTemplate,
                    model.Entry,
                    model.Original.ToDomain(),
                    model.Replacement.ToDomain(),
                    cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcVendorEditor, "UPDATE", "npc_vendor", "Updated item " + model.Original.ItemId + " for NPC vendor " + model.Entry + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = "Vendor item updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the vendor item: " + exception.Message;
            }

            return RedirectToVendor(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle npc vendor delete item within the world editor controller workflow.
        public async Task<IActionResult> NpcVendorDeleteItem(
            NpcVendorDeleteItemPostModel model,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata npcVendor, TableMetadata itemTemplate) = await GetVendorMutationTablesAsync(
                    profile,
                    model.Database,
                    cancellationToken);
                NpcVendorMutationResult result = await _npcVendors.DeleteItemAsync(
                    profile,
                    npcVendor,
                    model.Entry,
                    model.Original.ToDomain(),
                    cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcVendorEditor, "DELETE", "npc_vendor", "Removed item " + model.Original.ItemId + " from NPC vendor " + model.Entry + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = "Vendor item removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the vendor item: " + exception.Message;
            }

            return RedirectToVendor(model);
        }

        [HttpGet]
        // Handle npc trainers within the world editor controller workflow.
        public async Task<IActionResult> NpcTrainers(
            string? database,
            string? search = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int spellPage = 1,
            int? spellPageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tables, cancellationToken);
            ulong trainerFlagMask = MangosNpcFlagMasks.ResolveTrainerMask(schemaProfile);
            Dictionary<string, TableMetadata> byName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            List<string> missingTables = sourceArray.Where(required => !byName.ContainsKey(required)).ToList();
            bool hasSearched = show || entry.HasValue || search != null;
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveSpellPageSize = NormalizePageSize(spellPageSize ?? preferences.DefaultPageSize);

            NpcTrainerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                TrainerFlagMask = trainerFlagMask,
                TrainerFlagMaskDisplay = MangosNpcFlagMasks.FormatHex(trainerFlagMask),
                Search = search?.Trim() ?? string.Empty,
                HasSearched = hasSearched,
                IsAvailable = missingTables.Count == 0,
                MissingTables = missingTables,
                SelectedEntry = entry,
                HasSpellTemplate = byName.ContainsKey("spell_template"),
                HasTrainerTemplate = byName.ContainsKey("npc_trainer_template"),
                Trainers = new PagedResult<NpcTrainerSummary> { Page = 1, PageSize = effectivePageSize }
            };

            if (!model.IsAvailable || !hasSearched)
            {
                return View(model);
            }

            TableMetadata creatureTemplate = await _schema.GetTableAsync(profile, database, byName["creature_template"].Name, cancellationToken);
            TableMetadata npcTrainer = await _schema.GetTableAsync(profile, database, byName["npc_trainer"].Name, cancellationToken);
            TableMetadata? npcTrainerTemplate = byName.TryGetValue("npc_trainer_template", out TableMetadata? trainerTemplateSummary)
                ? await _schema.GetTableAsync(profile, database, trainerTemplateSummary.Name, cancellationToken)
                : null;
            TableMetadata? spellTemplate = byName.TryGetValue("spell_template", out TableMetadata? spellTemplateSummary)
                ? await _schema.GetTableAsync(profile, database, spellTemplateSummary.Name, cancellationToken)
                : null;
            model.HasSpellTemplate = spellTemplate?.Columns.Any(column =>
                string.Equals(column.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "entry", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "spell", StringComparison.OrdinalIgnoreCase)) is true;

            model.EditorCapabilities = _npcTrainers.GetEditorCapabilities(npcTrainer);
            model.CanAddTrainerSpells = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            model.CanEditTrainerSpells = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            model.CanDeleteTrainerSpells = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;

            int effectivePage = Math.Max(1, page);
            model.Trainers = await _npcTrainers.SearchAsync(profile, creatureTemplate, npcTrainer, npcTrainerTemplate, trainerFlagMask,
                model.Search, effectivePage, effectivePageSize, cancellationToken);
            if (model.Trainers.TotalPages > 0 && model.Trainers.Page > model.Trainers.TotalPages)
            {
                effectivePage = model.Trainers.TotalPages;
                model.Trainers = await _npcTrainers.SearchAsync(profile, creatureTemplate, npcTrainer, npcTrainerTemplate, trainerFlagMask,
                    model.Search, effectivePage, effectivePageSize, cancellationToken);
            }

            if (entry.HasValue)
            {
                model.SelectedTrainer = await _npcTrainers.GetAsync(profile, creatureTemplate, npcTrainer, npcTrainerTemplate, spellTemplate,
                    trainerFlagMask, entry.Value, Math.Max(1, spellPage), effectiveSpellPageSize, cancellationToken);
                if (model.SelectedTrainer == null)
                {
                    ModelState.AddModelError(string.Empty, "The selected creature was not found or does not have a trainer NPC flag for this expansion.");
                }
            }

            return View(model);
        }

        [HttpGet]
        // Handle double spawns within the world editor controller workflow.
        public async Task<IActionResult> DoubleSpawns(
            string? database,
            bool scan = false,
            string scope = DoubleSpawnScopes.Both,
            string matchMode = DoubleSpawnMatchModes.SameEntry,
            string poolFilter = DoubleSpawnPoolFilters.All,
            string linkedGameObjectFilter = DoubleSpawnLinkedGameObjectFilters.All,
            string? search = null,
            int? mapId = null,
            double tolerance = 0.1d,
            bool excludeEvents = true,
            int page = 1,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IProgress<OperationProgressUpdate>? progress = scan
                ? _operationProgress.Begin(
                    "Reading world schema",
                    "Loading spawn, template, event, and pool table metadata for the selected database.")
                : null;
            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tables, cancellationToken);
            Dictionary<string, TableMetadata> byName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);

            TableMetadata? creature = await LoadOptionalTableAsync(profile, database, byName, "creature", cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObject = await LoadOptionalTableAsync(profile, database, byName, "gameobject", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);
            TableMetadata? eventCreature = await LoadOptionalTableAsync(profile, database, byName, "game_event_creature", cancellationToken);
            TableMetadata? eventCreatureData = await LoadOptionalTableAsync(profile, database, byName, "game_event_creature_data", cancellationToken);
            TableMetadata? eventGameObject = await LoadOptionalTableAsync(profile, database, byName, "game_event_gameobject", cancellationToken);
            TableMetadata? poolCreature = await LoadOptionalTableAsync(profile, database, byName, "pool_creature", cancellationToken);
            TableMetadata? poolCreatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "pool_creature_template", cancellationToken);
            TableMetadata? poolGameObject = await LoadOptionalTableAsync(profile, database, byName, "pool_gameobject", cancellationToken);
            TableMetadata? poolGameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "pool_gameobject_template", cancellationToken);
            TableMetadata? poolTemplate = await LoadOptionalTableAsync(profile, database, byName, "pool_template", cancellationToken);
            TableMetadata? poolPool = await LoadOptionalTableAsync(profile, database, byName, "pool_pool", cancellationToken);

            if (scan)
            {
                _operationProgress.Report(
                    "Spawn schema resolved",
                    "Required spawn tables are resolved; loading available map IDs before positional analysis.");
            }

            DoubleSpawnViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                HasSearched = scan,
                HasCreatureTable = creature != null,
                HasGameObjectTable = gameObject != null,
                HasCreatureEventTable = eventCreature != null || eventCreatureData != null,
                HasGameObjectEventTable = eventGameObject != null,
                Scope = NormalizeDoubleSpawnScope(scope),
                MatchMode = NormalizeDoubleSpawnMatchMode(matchMode),
                PoolFilter = NormalizeDoubleSpawnPoolFilter(poolFilter),
                LinkedGameObjectFilter = NormalizeDoubleSpawnLinkedGameObjectFilter(linkedGameObjectFilter),
                Search = (search ?? string.Empty).Trim(),
                MapId = mapId,
                DistanceTolerance = Math.Clamp(tolerance, 0d, 100d),
                ExcludeEventRelated = excludeEvents,
                Result = new DoubleSpawnSearchResult
                {
                    Matches = new PagedResult<DoubleSpawnMatch>
                    {
                        Page = Math.Max(1, page),
                        PageSize = effectivePageSize
                    }
                }
            };

            if (model.HasCreatureTable && !model.HasGameObjectTable)
            {
                model.Scope = DoubleSpawnScopes.Creatures;
            }
            else if (!model.HasCreatureTable && model.HasGameObjectTable)
            {
                model.Scope = DoubleSpawnScopes.GameObjects;
            }

            try
            {
                model.MapIds = await _doubleSpawns.GetMapIdsAsync(profile, creature, gameObject, cancellationToken);
                if (scan)
                {
                    _operationProgress.Report(
                        "Map index loaded",
                        model.MapIds.Count.ToString("N0", CultureInfo.InvariantCulture) + " map ID(s) are available for the scan.");
                }

                if (scan)
                {
                    DoubleSpawnSearchRequest request = new()
                    {
                        Scope = model.Scope,
                        MatchMode = model.MatchMode,
                        PoolFilter = model.PoolFilter,
                        LinkedGameObjectFilter = model.LinkedGameObjectFilter,
                        Search = model.Search,
                        MapId = model.MapId,
                        DistanceTolerance = model.DistanceTolerance,
                        ExcludeEventRelated = model.ExcludeEventRelated,
                        Page = Math.Max(1, page),
                        PageSize = effectivePageSize
                    };

                    model.Result = await _doubleSpawns.SearchAsync(
                        profile,
                        creature,
                        creatureTemplate,
                        gameObject,
                        gameObjectTemplate,
                        eventCreature,
                        eventCreatureData,
                        eventGameObject,
                        poolCreature,
                        poolCreatureTemplate,
                        poolGameObject,
                        poolGameObjectTemplate,
                        poolTemplate,
                        poolPool,
                        request,
                        progress,
                        cancellationToken);
                    _operationProgress.Complete(
                        model.Result.Matches.TotalCount.ToString("N0", CultureInfo.InvariantCulture) + " overlap match(es) are ready to review.");
                }
            }
            catch (InvalidOperationException exception)
            {
                if (scan)
                {
                    _operationProgress.Fail(exception.Message);
                }

                ModelState.AddModelError(string.Empty, "Spawn Overlap Analyzer could not read this schema: " + exception.Message);
            }

            return View(model);
        }

        [HttpGet]
        // Handle npc trainer spells within the world editor controller workflow.
        public async Task<IActionResult> NpcTrainerSpells(
            string? database,
            string? search = null,
            int page = 1,
            int pageSize = 100,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return Unauthorized();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata spellTemplate = await _schema.GetTableAsync(profile, database, "spell_template", cancellationToken);
                PagedResult<NpcTrainerSpellOption> result = await _npcTrainers.SearchSpellsAsync(
                    profile, spellTemplate, search, Math.Max(1, page), Math.Clamp(pageSize, 25, 200), cancellationToken);
                return Json(new
                {
                    items = result.Items.Select(spell => new { id = spell.SpellId, name = spell.SpellName }),
                    page = result.Page,
                    pageSize = result.PageSize,
                    totalCount = result.TotalCount,
                    totalPages = result.TotalPages
                });
            }
            catch (Exception exception)
            {
                return BadRequest(new
                {
                    error = "Unable to load spell_template: " + exception.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle npc trainer add spells within the world editor controller workflow.
        public async Task<IActionResult> NpcTrainerAddSpells(NpcTrainerAddSpellsPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata npcTrainer = await GetTrainerMutationTableAsync(profile, model.Database, cancellationToken);
                NpcTrainerMutationResult result = await _npcTrainers.AddSpellsAsync(profile, npcTrainer, model.Entry,
                    model.Spells.Select(spell => spell.ToDomain()).ToArray(), cancellationToken);
                string summary = result.AffectedRows == 1
                    ? "Added spell to NPC trainer " + model.Entry + "."
                    : "Added " + result.AffectedRows.ToString(CultureInfo.InvariantCulture) + " spells to NPC trainer " + model.Entry + ".";
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcTrainerEditor, "INSERT", "npc_trainer", summary, result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = result.AffectedRows == 1 ? "Trainer spell added." : result.AffectedRows.ToString(CultureInfo.InvariantCulture) + " trainer spells added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add trainer spells: " + exception.Message;
            }

            return RedirectToTrainer(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle npc trainer edit spell within the world editor controller workflow.
        public async Task<IActionResult> NpcTrainerEditSpell(NpcTrainerEditSpellPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata npcTrainer = await GetTrainerMutationTableAsync(profile, model.Database, cancellationToken);
                NpcTrainerMutationResult result = await _npcTrainers.UpdateSpellAsync(profile, npcTrainer, model.Entry, model.Original.ToDomain(), model.Replacement.ToDomain(), cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcTrainerEditor, "UPDATE", "npc_trainer", "Updated spell " + model.Original.SpellId + " for NPC trainer " + model.Entry + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = "Trainer spell updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the trainer spell: " + exception.Message;
            }

            return RedirectToTrainer(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle npc trainer delete spell within the world editor controller workflow.
        public async Task<IActionResult> NpcTrainerDeleteSpell(NpcTrainerDeleteSpellPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata npcTrainer = await GetTrainerMutationTableAsync(profile, model.Database, cancellationToken);
                NpcTrainerMutationResult result = await _npcTrainers.DeleteSpellAsync(profile, npcTrainer, model.Entry, model.Original.ToDomain(), cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.NpcTrainerEditor, "DELETE", "npc_trainer", "Removed spell " + model.Original.SpellId + " from NPC trainer " + model.Entry + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, result.AffectedRows);
                TempData["Success"] = "Trainer spell removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the trainer spell: " + exception.Message;
            }

            return RedirectToTrainer(model);
        }

        [HttpGet]
        // Handle loot within the world editor controller workflow.
        public async Task<IActionResult> Loot(
            string? database,
            string? mode = null,
            string? search = null,
            string? tableFilter = null,
            string? sourceFilter = null,
            bool show = false,
            string? lootTable = null,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int rowPage = 1,
            int? rowPageSize = null,
            string? returnUrl = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tableSummaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tableSummaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = tableSummaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<LootTableDescriptor> descriptors = _loot.GetLootTables(tableSummaries);
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveRowPageSize = NormalizePageSize(rowPageSize ?? preferences.DefaultPageSize);
            string normalizedMode = LootViewerModes.Normalize(mode);
            if (!string.IsNullOrWhiteSpace(lootTable) && entry.HasValue)
            {
                normalizedMode = LootViewerModes.Templates;
            }

            bool hasSearched = show || entry.HasValue || search != null || tableFilter != null || sourceFilter != null;

            LootViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Mode = normalizedMode,
                Search = search?.Trim() ?? string.Empty,
                TableFilter = string.IsNullOrWhiteSpace(tableFilter) ? "all" : tableFilter.Trim(),
                SourceFilter = LootSourceFilters.Normalize(sourceFilter),
                ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty,
                HasSearched = hasSearched,
                IsAvailable = descriptors.Count > 0,
                LootTables = descriptors,
                SelectedTable = lootTable,
                SelectedEntry = entry,
                Templates = new PagedResult<LootTemplateSummary> { Page = 1, PageSize = effectivePageSize },
                Sources = new PagedResult<LootSourceSummary> { Page = 1, PageSize = effectivePageSize }
            };

            if (!model.IsAvailable)
            {
                return View(model);
            }

            if (entry.HasValue && !string.IsNullOrWhiteSpace(lootTable) && string.IsNullOrWhiteSpace(model.ReturnUrl))
            {
                model.ReturnUrl = Url.Action(nameof(Loot), new
                {
                    database = model.Database,
                    mode = LootViewerModes.Templates,
                    search = model.Search,
                    tableFilter = model.TableFilter,
                    show = true,
                    page = Math.Max(1, page),
                    pageSize = effectivePageSize
                }) ?? string.Empty;
            }

            List<TableMetadata> lootTables = new();
            foreach (LootTableDescriptor descriptor in descriptors)
            {
                if (byName.TryGetValue(descriptor.TableName, out TableMetadata? summary))
                {
                    lootTables.Add(await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken));
                }
            }

            Dictionary<string, TableMetadata> lootTablesByName = lootTables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            TableMetadata? itemTemplate = await LoadOptionalTableAsync(profile, database, byName, "item_template", cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);
            model.EditorTables = lootTables.Select(_loot.GetEditorCapabilities).ToArray();
            model.CanAddRows = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded && model.EditorTables.Any(capability => capability.CanMutate);
            model.CanEditRows = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded && model.EditorTables.Any(capability => capability.CanMutate);
            model.CanRemoveRows = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded && model.EditorTables.Any(capability => capability.CanMutate);

            if (!hasSearched)
            {
                return View(model);
            }

            if (normalizedMode == LootViewerModes.Sources)
            {
                model.Sources = await _loot.SearchSourcesAsync(
                    profile,
                    creatureTemplate,
                    gameObjectTemplate,
                    lootTablesByName,
                    model.SourceFilter,
                    model.Search,
                    Math.Max(1, page),
                    effectivePageSize,
                    cancellationToken);
            }
            else
            {
                model.Templates = await _loot.SearchTemplatesAsync(
                    profile,
                    lootTables,
                    model.TableFilter,
                    model.Search,
                    Math.Max(1, page),
                    effectivePageSize,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(lootTable) && entry.HasValue && lootTablesByName.TryGetValue(lootTable, out TableMetadata? selectedLootTable))
                {
                    lootTablesByName.TryGetValue("reference_loot_template", out TableMetadata? referenceLootTable);
                    model.SelectedTemplate = await _loot.GetTemplateAsync(
                        profile,
                        selectedLootTable,
                        itemTemplate,
                        referenceLootTable,
                        creatureTemplate,
                        gameObjectTemplate,
                        entry.Value,
                        Math.Max(1, rowPage),
                        effectiveRowPageSize,
                        cancellationToken);
                    if (model.SelectedTemplate == null)
                    {
                        ModelState.AddModelError(string.Empty, "The selected loot template could not be found in " + selectedLootTable.Name + ".");
                    }
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle loot add row within the world editor controller workflow.
        public async Task<IActionResult> LootAddRow(LootAddRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata lootTable, TableMetadata? itemTemplate, TableMetadata? referenceLootTable) = await GetLootMutationTablesAsync(profile, model.Database, model.LootTable, cancellationToken);
                LootEditorCapabilities capabilities = _loot.GetEditorCapabilities(lootTable);
                if (!capabilities.CanMutate)
                {
                    throw new InvalidOperationException("This loot table cannot be edited safely through Loot Viewer.");
                }

                LootRowValues values = model.Row.ToDomain(model.Entry, capabilities.SupportsReferences);
                LootMutationResult result = await _loot.AddRowAsync(profile, lootTable, itemTemplate, referenceLootTable, values, cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.LootViewer,
                    "INSERT",
                    result.ObjectName,
                    "Added loot row " + values.ItemId.ToString(CultureInfo.InvariantCulture) + " to " + lootTable.Name + " entry " + values.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                TempData["Success"] = "Loot row added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the loot row: " + exception.Message;
            }

            return RedirectToLoot(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle loot edit row within the world editor controller workflow.
        public async Task<IActionResult> LootEditRow(LootEditRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata lootTable, TableMetadata? itemTemplate, TableMetadata? referenceLootTable) = await GetLootMutationTablesAsync(profile, model.Database, model.LootTable, cancellationToken);
                LootEditorCapabilities capabilities = _loot.GetEditorCapabilities(lootTable);
                if (!capabilities.CanMutate)
                {
                    throw new InvalidOperationException("This loot table cannot be edited safely through Loot Viewer.");
                }

                LootRowValues original = model.Original.ToDomain(model.Entry, capabilities.SupportsReferences);
                LootRowValues replacement = model.Replacement.ToDomain(model.Entry, capabilities.SupportsReferences);
                LootMutationResult result = await _loot.UpdateRowAsync(profile, lootTable, itemTemplate, referenceLootTable, original, replacement, cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.LootViewer,
                    "UPDATE",
                    result.ObjectName,
                    "Updated loot row " + original.ItemId.ToString(CultureInfo.InvariantCulture) + " in " + lootTable.Name + " entry " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Loot row updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the loot row: " + exception.Message;
            }

            return RedirectToLoot(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle loot delete row within the world editor controller workflow.
        public async Task<IActionResult> LootDeleteRow(LootDeleteRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            bool keepSelection = true;
            try
            {
                var (LootTable, ItemTemplate, ReferenceLootTable) = await GetLootMutationTablesAsync(profile, model.Database, model.LootTable, cancellationToken);
                TableMetadata lootTable = LootTable;
                LootEditorCapabilities capabilities = _loot.GetEditorCapabilities(lootTable);
                if (!capabilities.CanMutate)
                {
                    throw new InvalidOperationException("This loot table cannot be edited safely through Loot Viewer.");
                }

                LootRowValues original = model.Original.ToDomain(model.Entry, capabilities.SupportsReferences);
                LootMutationResult result = await _loot.DeleteRowAsync(profile, lootTable, original, cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.LootViewer,
                    "DELETE",
                    result.ObjectName,
                    "Removed loot row " + original.ItemId.ToString(CultureInfo.InvariantCulture) + " from " + lootTable.Name + " entry " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                keepSelection = result.RemainingRowsInDefinition > 0;
                TempData["Success"] = keepSelection
                    ? "Loot row removed."
                    : "Loot row removed. The loot definition is now empty and no longer appears in the directory.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the loot row: " + exception.Message;
            }

            return RedirectToLoot(model, keepSelection);
        }

        [HttpGet]
        // Handle gossip within the world editor controller workflow.
        public async Task<IActionResult> Gossip(
            string? database,
            string? search = null,
            string? sourceFilter = null,
            bool show = false,
            int page = 1,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, summaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            bool hasSearched = show || search != null || sourceFilter != null;

            GossipViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Search = search?.Trim() ?? string.Empty,
                SourceFilter = GossipSourceFilters.Normalize(sourceFilter),
                HasSearched = hasSearched,
                HasGossipMenuTable = byName.ContainsKey("gossip_menu"),
                HasGossipOptionTable = byName.ContainsKey("gossip_menu_option"),
                HasNpcTextTable = byName.ContainsKey("npc_text"),
                HasCreatureSources = byName.ContainsKey("creature_template"),
                HasGameObjectSources = byName.ContainsKey("gameobject_template"),
                Sources = new PagedResult<GossipSourceSummary> { Page = 1, PageSize = effectivePageSize }
            };
            model.IsAvailable = model.HasCreatureSources || model.HasGameObjectSources;
            if (!model.IsAvailable)
            {
                return View(model);
            }

            TableMetadata? gossipMenu = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu", cancellationToken);
            TableMetadata? gossipMenuOption = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu_option", cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);

            model.EditorCapabilities = _gossip.GetEditorCapabilities(gossipMenu, gossipMenuOption);
            bool canCreate = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            bool canEdit = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            model.CanAddMenuRows = canCreate && model.EditorCapabilities.CanMutateMenuRows;
            model.CanAddCreatureSources = canEdit && SupportsCreatureGossipSourceEditing(creatureTemplate);

            if (!hasSearched)
            {
                return View(model);
            }

            model.Sources = await _gossip.SearchSourcesAsync(
                profile,
                gossipMenu,
                gossipMenuOption,
                creatureTemplate,
                gameObjectTemplate,
                model.SourceFilter,
                model.Search,
                Math.Max(1, page),
                effectivePageSize,
                cancellationToken);

            return View(model);
        }

        [HttpGet]
        // Handle gossip creature options within the world editor controller workflow.
        public async Task<IActionResult> GossipCreatureOptions(
            string? database,
            string? search = null,
            int page = 1,
            int pageSize = 100,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return Unauthorized();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata creatureTemplate = await _schema.GetTableAsync(profile, database, "creature_template", cancellationToken);
                PagedResult<GossipCreatureOption> result = await _gossip.SearchEligibleCreaturesAsync(
                    profile,
                    creatureTemplate,
                    MangosNpcFlagMasks.Gossip,
                    search,
                    Math.Max(1, page),
                    Math.Clamp(pageSize, 25, 200),
                    cancellationToken);

                return Json(new
                {
                    items = result.Items.Select(creature => new
                    {
                        id = creature.Entry,
                        name = string.IsNullOrWhiteSpace(creature.Name) ? "Unnamed NPC" : creature.Name,
                        menuId = creature.GossipMenuId
                    }),
                    page = result.Page,
                    pageSize = result.PageSize,
                    totalCount = result.TotalCount,
                    totalPages = result.TotalPages
                });
            }
            catch (Exception exception)
            {
                return BadRequest(new
                {
                    error = "Unable to load eligible creature_template rows: " + exception.Message
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle gossip add creature source within the world editor controller workflow.
        public async Task<IActionResult> GossipAddCreatureSource(
            GossipAddCreatureSourcePostModel model,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                TableMetadata creatureTemplate = await _schema.GetTableAsync(profile, model.Database, "creature_template", cancellationToken);
                GossipMutationResult result = await _gossip.AddCreatureSourceAsync(
                    profile,
                    creatureTemplate,
                    model.CreatureEntry,
                    model.MenuId,
                    MangosNpcFlagMasks.Gossip,
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.GossipMenus,
                    "UPDATE",
                    result.ObjectName,
                    "Added creature " + model.CreatureEntry.ToString(CultureInfo.InvariantCulture) + " as a gossip source for menu " + model.MenuId.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    Math.Max(1, result.AffectedRows));
                TempData["Success"] = "NPC added as a gossip source.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the NPC gossip source: " + exception.Message;
            }

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction(nameof(Gossip), new
            {
                database = model.Database,
                search = model.Search,
                sourceFilter = GossipSourceFilters.Normalize(model.SourceFilter),
                show = true,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize)
            });
        }

        [HttpGet]
        // Handle menus within the world editor controller workflow.
        public async Task<IActionResult> Menus(
            string? database,
            string? search = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int textPage = 1,
            int? textPageSize = null,
            int optionPage = 1,
            int? optionPageSize = null,
            string? returnUrl = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, summaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveTextPageSize = NormalizePageSize(textPageSize ?? preferences.DefaultPageSize);
            int effectiveOptionPageSize = NormalizePageSize(optionPageSize ?? preferences.DefaultPageSize);
            bool hasSearched = show || entry.HasValue || search != null;

            GossipViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Search = search?.Trim() ?? string.Empty,
                ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : string.Empty,
                HasSearched = hasSearched,
                HasGossipMenuTable = byName.ContainsKey("gossip_menu"),
                HasGossipOptionTable = byName.ContainsKey("gossip_menu_option"),
                HasNpcTextTable = byName.ContainsKey("npc_text"),
                HasCreatureSources = byName.ContainsKey("creature_template"),
                HasGameObjectSources = byName.ContainsKey("gameobject_template"),
                Menus = new PagedResult<GossipMenuSummary> { Page = 1, PageSize = effectivePageSize },
                SelectedEntry = entry
            };
            model.IsAvailable = model.HasGossipMenuTable || model.HasGossipOptionTable;
            if (!model.IsAvailable)
            {
                return View(model);
            }

            TableMetadata? gossipMenu = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu", cancellationToken);
            TableMetadata? gossipMenuOption = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu_option", cancellationToken);
            TableMetadata? npcText = await LoadOptionalTableAsync(profile, database, byName, "npc_text", cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);

            model.EditorCapabilities = _gossip.GetEditorCapabilities(gossipMenu, gossipMenuOption);
            bool canCreate = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            bool canEdit = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            bool canDelete = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;
            model.CanAddMenuRows = canCreate && model.EditorCapabilities.CanMutateMenuRows;
            model.CanEditMenuRows = canEdit && model.EditorCapabilities.CanMutateMenuRows;
            model.CanDeleteMenuRows = canDelete && model.EditorCapabilities.CanMutateMenuRows;
            model.CanAddOptions = canCreate && model.EditorCapabilities.CanMutateOptions;
            model.CanEditOptions = canEdit && model.EditorCapabilities.CanMutateOptions;
            model.CanDeleteOptions = canDelete && model.EditorCapabilities.CanMutateOptions;
            model.CanAddCreatureSources = canEdit && SupportsCreatureGossipSourceEditing(creatureTemplate);

            if (!hasSearched)
            {
                return View(model);
            }

            model.Menus = await _gossip.SearchMenusAsync(
                profile,
                gossipMenu,
                gossipMenuOption,
                creatureTemplate,
                gameObjectTemplate,
                model.Search,
                Math.Max(1, page),
                effectivePageSize,
                cancellationToken);

            if (entry.HasValue)
            {
                if (string.IsNullOrWhiteSpace(model.ReturnUrl))
                {
                    model.ReturnUrl = Url.Action(nameof(Menus), new
                    {
                        database = model.Database,
                        search = model.Search,
                        show = true,
                        page = model.Menus.Page,
                        pageSize = model.Menus.PageSize
                    }) ?? string.Empty;
                }

                model.SelectedMenu = await _gossip.GetMenuAsync(
                    profile,
                    gossipMenu,
                    gossipMenuOption,
                    npcText,
                    creatureTemplate,
                    gameObjectTemplate,
                    entry.Value,
                    Math.Max(1, textPage),
                    effectiveTextPageSize,
                    Math.Max(1, optionPage),
                    effectiveOptionPageSize,
                    cancellationToken);

                if (model.SelectedMenu == null)
                {
                    ModelState.AddModelError(string.Empty, "The selected gossip menu could not be found.");
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle gossip add menu row within the world editor controller workflow.
        public async Task<IActionResult> GossipAddMenuRow(GossipAddMenuRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            long targetEntry = model.NewEntry;
            model.Entry = targetEntry;
            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenu == null)
                {
                    throw new InvalidOperationException("gossip_menu is not available in this database.");
                }

                GossipMenuRowValues values = model.Row.ToDomain(targetEntry);
                GossipMutationResult result = await _gossip.AddMenuRowAsync(profile, GossipMenu, NpcText, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "INSERT", result.ObjectName, "Added text " + values.TextId.ToString(CultureInfo.InvariantCulture) + " to gossip menu " + targetEntry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Gossip menu text row added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the gossip text row: " + exception.Message;
            }

            return RedirectToGossipMenus(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle gossip edit menu row within the world editor controller workflow.
        public async Task<IActionResult> GossipEditMenuRow(GossipEditMenuRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenu == null)
                {
                    throw new InvalidOperationException("gossip_menu is not available in this database.");
                }

                GossipMutationResult result = await _gossip.UpdateMenuRowAsync(profile, GossipMenu, NpcText, model.Original.ToDomain(model.Entry), model.Replacement.ToDomain(model.Entry), cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "UPDATE", result.ObjectName, "Updated text row in gossip menu " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Gossip menu text row updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the gossip text row: " + exception.Message;
            }

            return RedirectToGossipMenus(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle gossip delete menu row within the world editor controller workflow.
        public async Task<IActionResult> GossipDeleteMenuRow(GossipDeleteMenuRowPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            bool keep = true;
            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenu == null)
                {
                    throw new InvalidOperationException("gossip_menu is not available in this database.");
                }

                GossipMenuRowValues original = model.Original.ToDomain(model.Entry);
                GossipMutationResult result = await _gossip.DeleteMenuRowAsync(profile, GossipMenu, GossipMenuOption, original, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "DELETE", result.ObjectName, "Removed text " + original.TextId.ToString(CultureInfo.InvariantCulture) + " from gossip menu " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                keep = result.DefinitionExists;
                TempData["Success"] = keep ? "Gossip menu text row removed." : "Gossip menu text row removed. The menu no longer has definition rows.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the gossip text row: " + exception.Message;
            }

            return RedirectToGossipMenus(model, keep);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle gossip add option within the world editor controller workflow.
        public async Task<IActionResult> GossipAddOption(GossipAddOptionPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenuOption == null)
                {
                    throw new InvalidOperationException("gossip_menu_option is not available in this database.");
                }

                GossipOptionValues values = model.Option.ToDomain(model.Entry);
                GossipMutationResult result = await _gossip.AddOptionAsync(profile, GossipMenuOption, GossipMenu, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "INSERT", result.ObjectName, "Added option " + values.Id.ToString(CultureInfo.InvariantCulture) + " to gossip menu " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Gossip menu option added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the gossip option: " + exception.Message;
            }

            return RedirectToGossipMenus(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle gossip edit option within the world editor controller workflow.
        public async Task<IActionResult> GossipEditOption(GossipEditOptionPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenuOption == null)
                {
                    throw new InvalidOperationException("gossip_menu_option is not available in this database.");
                }

                GossipMutationResult result = await _gossip.UpdateOptionAsync(profile, GossipMenuOption, GossipMenu, model.Original.ToDomain(model.Entry), model.Replacement.ToDomain(model.Entry), cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "UPDATE", result.ObjectName, "Updated option " + model.Original.Id.ToString(CultureInfo.InvariantCulture) + " in gossip menu " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Gossip menu option updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the gossip option: " + exception.Message;
            }

            return RedirectToGossipMenus(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle gossip delete option within the world editor controller workflow.
        public async Task<IActionResult> GossipDeleteOption(GossipDeleteOptionPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            bool keep = true;
            try
            {
                var (GossipMenu, GossipMenuOption, NpcText) = await GetGossipMutationTablesAsync(profile, model.Database, cancellationToken);
                if (GossipMenuOption == null)
                {
                    throw new InvalidOperationException("gossip_menu_option is not available in this database.");
                }

                GossipOptionValues original = model.Original.ToDomain(model.Entry);
                GossipMutationResult result = await _gossip.DeleteOptionAsync(profile, GossipMenu, GossipMenuOption, original, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.GossipMenus, "DELETE", result.ObjectName, "Removed option " + original.Id.ToString(CultureInfo.InvariantCulture) + " from gossip menu " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                keep = result.DefinitionExists;
                TempData["Success"] = keep ? "Gossip menu option removed." : "Gossip menu option removed. The menu no longer has definition rows.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the gossip option: " + exception.Message;
            }

            return RedirectToGossipMenus(model, keep);
        }

        [HttpGet]
        // Handle quests within the world editor controller workflow.
        public async Task<IActionResult> Quests(
            string? database,
            string? search = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int relationPage = 1,
            int? relationPageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tableSummaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tableSummaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = tableSummaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            bool hasSearched = show || entry.HasValue || search != null;
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveRelationPageSize = NormalizePageSize(relationPageSize ?? preferences.DefaultPageSize);

            QuestViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Search = search?.Trim() ?? string.Empty,
                HasSearched = hasSearched,
                IsAvailable = byName.ContainsKey("quest_template"),
                AvailableTables = sourceArray0.Where(byName.ContainsKey).ToArray(),
                SelectedEntry = entry,
                Quests = new PagedResult<QuestSummary> { Page = 1, PageSize = effectivePageSize }
            };

            if (!model.IsAvailable)
            {
                return View(model);
            }

            TableMetadata questTemplate = await _schema.GetTableAsync(profile, database, byName["quest_template"].Name, cancellationToken);
            Dictionary<string, TableMetadata> relationTables = await LoadQuestRelationTablesAsync(profile, database, byName, cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);

            try
            {
                model.EditorCapabilities = _quests.GetEditorCapabilities(questTemplate, relationTables);
            }
            catch (InvalidOperationException exception)
            {
                model.IsAvailable = false;
                ModelState.AddModelError(string.Empty, "Quest Viewer could not read this quest_template schema: " + exception.Message);
                return View(model);
            }

            bool canCreate = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            bool canEdit = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            bool canDelete = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;
            model.CanCreateQuests = canCreate && model.EditorCapabilities.CanCreateQuests;
            model.CanEditQuests = canEdit && model.EditorCapabilities.CanMutateQuests;
            model.CanDeleteQuests = canDelete && model.EditorCapabilities.CanMutateQuests;
            model.CanAddRelations = canCreate && model.EditorCapabilities.RelationKinds.Count > 0;
            model.CanEditRelations = canEdit && model.EditorCapabilities.RelationKinds.Count > 0;
            model.CanRemoveRelations = canDelete && model.EditorCapabilities.RelationKinds.Count > 0;

            if (!hasSearched)
            {
                return View(model);
            }

            int effectivePage = Math.Max(1, page);
            model.Quests = await _quests.SearchAsync(profile, questTemplate, relationTables, model.Search, effectivePage, effectivePageSize, cancellationToken);
            if (model.Quests.TotalPages > 0 && model.Quests.Page > model.Quests.TotalPages)
            {
                effectivePage = model.Quests.TotalPages;
                model.Quests = await _quests.SearchAsync(profile, questTemplate, relationTables, model.Search, effectivePage, effectivePageSize, cancellationToken);
            }

            if (entry.HasValue)
            {
                model.SelectedQuest = await _quests.GetAsync(
                    profile, questTemplate, relationTables, creatureTemplate, gameObjectTemplate, entry.Value,
                    Math.Max(1, relationPage), effectiveRelationPageSize, cancellationToken);
                if (model.SelectedQuest == null)
                {
                    ModelState.AddModelError(string.Empty, "The selected quest could not be found in quest_template.");
                }
            }

            return View(model);
        }

        [HttpGet]
        // Handle quest options within the world editor controller workflow.
        public async Task<IActionResult> QuestOptions(
            string? database,
            string? search = null,
            int page = 1,
            int pageSize = 100,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return Unauthorized();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tableSummaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            TableMetadata? questSummary = tableSummaries.FirstOrDefault(table => string.Equals(table.Name, "quest_template", StringComparison.OrdinalIgnoreCase));
            if (questSummary == null)
            {
                return NotFound();
            }

            try
            {
                TableMetadata questTemplate = await _schema.GetTableAsync(profile, database, questSummary.Name, cancellationToken);
                PagedResult<QuestSummary> quests = await _quests.SearchAsync(
                    profile,
                    questTemplate,
                    new Dictionary<string, TableMetadata>(StringComparer.OrdinalIgnoreCase),
                    search,
                    Math.Max(1, page),
                    Math.Clamp(pageSize, 10, 200),
                    cancellationToken);

                return Json(new
                {
                    items = quests.Items.Select(quest => new
                    {
                        id = quest.Entry,
                        name = string.IsNullOrWhiteSpace(quest.Title) ? "Unnamed quest" : quest.Title
                    }),
                    quests.Page,
                    quests.PageSize,
                    quests.TotalCount,
                    quests.TotalPages
                });
            }
            catch (Exception exception)
            {
                return BadRequest(new
                {
                    error = "Could not load quests: " + exception.Message
                });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle quest edit within the world editor controller workflow.
        public async Task<IActionResult> QuestEdit(QuestEditPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate) = await GetQuestMutationTablesAsync(profile, model.Database, cancellationToken);
                QuestTemplateValues values = ToQuestValues(model.Entry, model.Title, model.Details, model.Objectives, model.MinLevel, model.QuestLevel, model.PrevQuestId, model.NextQuestId, model.ExclusiveGroup, model.NextQuestInChain);
                QuestMutationResult result = await _quests.UpdateQuestAsync(profile, QuestTemplate, model.Entry, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.QuestViewer, "UPDATE", result.ObjectName,
                    "Updated quest " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true,
                    cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Quest updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the quest: " + exception.Message;
            }

            return RedirectToQuest(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle quest delete within the world editor controller workflow.
        public async Task<IActionResult> QuestDelete(QuestDeletePostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate) = await GetQuestMutationTablesAsync(profile, model.Database, cancellationToken);
                QuestMutationResult result = await _quests.DeleteQuestAsync(profile, QuestTemplate, RelationTables, model.Entry, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.QuestViewer, "DELETE", result.ObjectName,
                    "Deleted quest " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true,
                    cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Quest deleted.";
                return RedirectToQuest(model, false);
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not delete the quest: " + exception.Message;
            }

            return RedirectToQuest(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle quest add relation within the world editor controller workflow.
        public async Task<IActionResult> QuestAddRelation(QuestRelationPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate) = await GetQuestMutationTablesAsync(profile, model.Database, cancellationToken);
                QuestRelationValues values = new()
                {
                    Kind = model.Kind,
                    EntityEntry = model.EntityEntry
                };
                QuestMutationResult result = await _quests.AddRelationAsync(profile, QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate, model.Entry, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.QuestViewer, "INSERT", result.ObjectName,
                    "Added " + QuestRelationKinds.EntityLabel(values.Kind) + " quest relationship for quest " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Quest relationship added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the quest relationship: " + exception.Message;
            }

            return RedirectToQuest(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle quest edit relation within the world editor controller workflow.
        public async Task<IActionResult> QuestEditRelation(QuestEditRelationPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate) = await GetQuestMutationTablesAsync(profile, model.Database, cancellationToken);
                QuestRelationValues original = new()
                {
                    Kind = model.OriginalKind,
                    EntityEntry = model.OriginalEntityEntry
                };
                QuestRelationValues replacement = new()
                {
                    Kind = model.Kind,
                    EntityEntry = model.EntityEntry
                };
                QuestMutationResult result = await _quests.UpdateRelationAsync(profile, QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate, model.Entry, original, replacement, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.QuestViewer, "UPDATE", result.ObjectName,
                    "Updated quest relationship for quest " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Quest relationship updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the quest relationship: " + exception.Message;
            }

            return RedirectToQuest(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle quest remove relation within the world editor controller workflow.
        public async Task<IActionResult> QuestRemoveRelation(QuestRelationPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (QuestTemplate, RelationTables, CreatureTemplate, GameObjectTemplate) = await GetQuestMutationTablesAsync(profile, model.Database, cancellationToken);
                QuestRelationValues values = new()
                {
                    Kind = model.Kind,
                    EntityEntry = model.EntityEntry
                };
                QuestMutationResult result = await _quests.RemoveRelationAsync(profile, RelationTables, model.Entry, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.QuestViewer, "DELETE", result.ObjectName,
                    "Removed quest relationship for quest " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Quest relationship removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the quest relationship: " + exception.Message;
            }

            return RedirectToQuest(model, true);
        }

        [HttpGet]
        // Handle events within the world editor controller workflow.
        public async Task<IActionResult> Events(
            string? database,
            string? search = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int memberPage = 1,
            int? memberPageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tableSummaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tableSummaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = tableSummaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            bool hasSearched = show || entry.HasValue || search != null;
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveMemberPageSize = NormalizePageSize(memberPageSize ?? preferences.DefaultPageSize);

            EventViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Search = search?.Trim() ?? string.Empty,
                HasSearched = hasSearched,
                IsAvailable = byName.ContainsKey("game_event"),
                AvailableTables = new[] { "game_event", EventMemberSources.Creature, EventMemberSources.GameObject }
                    .Where(byName.ContainsKey)
                    .ToArray(),
                SelectedEntry = entry,
                Events = new PagedResult<EventSummary> { Page = 1, PageSize = effectivePageSize }
            };

            if (!model.IsAvailable)
            {
                return View(model);
            }

            TableMetadata gameEvent = await _schema.GetTableAsync(profile, database, byName["game_event"].Name, cancellationToken);
            TableMetadata? gameEventCreature = await LoadOptionalTableAsync(profile, database, byName, EventMemberSources.Creature, cancellationToken);
            TableMetadata? gameEventGameObject = await LoadOptionalTableAsync(profile, database, byName, EventMemberSources.GameObject, cancellationToken);

            try
            {
                model.EditorCapabilities = _events.GetEditorCapabilities(gameEvent, gameEventCreature, gameEventGameObject);
            }
            catch (InvalidOperationException exception)
            {
                model.IsAvailable = false;
                ModelState.AddModelError(string.Empty, "Event Viewer could not read this game_event schema: " + exception.Message);
                return View(model);
            }

            bool canCreate = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            bool canEdit = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            bool canDelete = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;
            model.CanCreateEvents = canCreate && model.EditorCapabilities.CanMutateEvents;
            model.CanEditEvents = canEdit && model.EditorCapabilities.CanMutateEvents;
            model.CanDeleteEvents = canDelete && model.EditorCapabilities.CanMutateEvents;
            model.CanAddMembers = canCreate && model.EditorCapabilities.MemberSources.Count > 0;
            model.CanEditMembers = canEdit && model.EditorCapabilities.MemberSources.Count > 0;
            model.CanRemoveMembers = canDelete && model.EditorCapabilities.MemberSources.Count > 0;

            if (!hasSearched)
            {
                return View(model);
            }

            int effectivePage = Math.Max(1, page);
            model.Events = await _events.SearchAsync(
                profile,
                gameEvent,
                gameEventCreature,
                gameEventGameObject,
                model.Search,
                effectivePage,
                effectivePageSize,
                cancellationToken);
            if (model.Events.TotalPages > 0 && model.Events.Page > model.Events.TotalPages)
            {
                effectivePage = model.Events.TotalPages;
                model.Events = await _events.SearchAsync(
                    profile,
                    gameEvent,
                    gameEventCreature,
                    gameEventGameObject,
                    model.Search,
                    effectivePage,
                    effectivePageSize,
                    cancellationToken);
            }

            if (entry.HasValue)
            {
                TableMetadata? creature = await LoadOptionalTableAsync(profile, database, byName, "creature", cancellationToken);
                TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
                TableMetadata? gameObject = await LoadOptionalTableAsync(profile, database, byName, "gameobject", cancellationToken);
                TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);
                model.SelectedEvent = await _events.GetAsync(
                    profile,
                    gameEvent,
                    gameEventCreature,
                    gameEventGameObject,
                    creature,
                    creatureTemplate,
                    gameObject,
                    gameObjectTemplate,
                    entry.Value,
                    Math.Max(1, memberPage),
                    effectiveMemberPageSize,
                    cancellationToken);
                if (model.SelectedEvent == null)
                {
                    ModelState.AddModelError(string.Empty, "The selected game event could not be found in game_event.");
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle event create within the world editor controller workflow.
        public async Task<IActionResult> EventCreate(EventCreatePostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventTemplateValues values = new()
                {
                    Entry = model.NewEntry,
                    StartTime = model.StartTime,
                    EndTime = model.EndTime,
                    Occurrence = model.Occurrence,
                    Length = model.Length,
                    Holiday = model.Holiday,
                    Description = model.Description
                };
                EventMutationResult result = await _events.AddEventAsync(profile, GameEvent, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "INSERT", result.ObjectName,
                    "Created game event " + values.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true,
                    cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Game event created.";
                model.Entry = values.Entry;
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not create the game event: " + exception.Message;
            }

            return RedirectToEvent(model, model.Entry > 0);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle event edit within the world editor controller workflow.
        public async Task<IActionResult> EventEdit(EventEditPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventTemplateValues values = new()
                {
                    Entry = model.Entry,
                    StartTime = model.StartTime,
                    EndTime = model.EndTime,
                    Occurrence = model.Occurrence,
                    Length = model.Length,
                    Holiday = model.Holiday,
                    Description = model.Description
                };
                EventMutationResult result = await _events.UpdateEventAsync(profile, GameEvent, model.Entry, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "UPDATE", result.ObjectName,
                    "Updated game event " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true,
                    cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Game event updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the game event: " + exception.Message;
            }

            return RedirectToEvent(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle event delete within the world editor controller workflow.
        public async Task<IActionResult> EventDelete(EventDeletePostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventMutationResult result = await _events.DeleteEventAsync(profile, GameEvent,
                    EventTables.TryGetValue(EventMemberSources.Creature, out TableMetadata? creatureLinks) ? creatureLinks : null,
                    EventTables.TryGetValue(EventMemberSources.GameObject, out TableMetadata? gameObjectLinks) ? gameObjectLinks : null,
                    model.Entry, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "DELETE", result.ObjectName,
                    "Deleted game event " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".", result.SqlPreview, true,
                    cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Game event deleted.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not delete the game event: " + exception.Message;
                return RedirectToEvent(model, true);
            }

            return RedirectToEvent(model, false);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle event add member within the world editor controller workflow.
        public async Task<IActionResult> EventAddMember(EventMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventMemberValues values = new()
                {
                    Source = model.Source,
                    Guid = model.Guid,
                    EventValue = model.EventValue
                };
                EventMutationResult result = await _events.AddMemberAsync(profile, GameEvent, EventTables, Creature, GameObject,
                    model.Entry, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "INSERT", result.ObjectName,
                    "Linked " + model.Source + " spawn " + model.Guid.ToString(CultureInfo.InvariantCulture) + " to game event " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Event relationship added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the event relationship: " + exception.Message;
            }

            return RedirectToEvent(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle event edit member within the world editor controller workflow.
        public async Task<IActionResult> EventEditMember(EventEditMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventMemberValues original = new()
                {
                    Source = model.Source,
                    Guid = model.OriginalGuid,
                    EventValue = model.OriginalEventValue
                };
                EventMemberValues replacement = new()
                {
                    Source = model.Source,
                    Guid = model.Guid,
                    EventValue = model.EventValue
                };
                EventMutationResult result = await _events.UpdateMemberAsync(profile, GameEvent, EventTables, Creature, GameObject,
                    model.Entry, original, replacement, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "UPDATE", result.ObjectName,
                    "Updated " + model.Source + " relationship for game event " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Event relationship updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the event relationship: " + exception.Message;
            }

            return RedirectToEvent(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle event remove member within the world editor controller workflow.
        public async Task<IActionResult> EventRemoveMember(EventMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (GameEvent, EventTables, Creature, GameObject) = await GetEventMutationTablesAsync(profile, model.Database, cancellationToken);
                EventMemberValues values = new()
                {
                    Source = model.Source,
                    Guid = model.Guid,
                    EventValue = model.EventValue
                };
                EventMutationResult result = await _events.RemoveMemberAsync(profile, EventTables, values, cancellationToken);
                await RecordStoredChangeAsync(profile, model.Database, StoredChangeSources.EventViewer, "DELETE", result.ObjectName,
                    "Removed " + model.Source + " spawn " + model.Guid.ToString(CultureInfo.InvariantCulture) + " from game event " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview, true, cancellationToken, result.RevertSqlPreview, Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Event relationship removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the event relationship: " + exception.Message;
            }

            return RedirectToEvent(model, true);
        }

        [HttpGet]
        // Handle pools within the world editor controller workflow.
        public async Task<IActionResult> Pools(
            string? database,
            string? search = null,
            string? population = null,
            bool show = false,
            long? entry = null,
            int page = 1,
            int? pageSize = null,
            int memberPage = 1,
            int? memberPageSize = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            database = string.IsNullOrWhiteSpace(database) ? profile.DefaultDatabase : database;
            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            IReadOnlyList<TableMetadata> tableSummaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaAsync(profile, database, tableSummaries, cancellationToken);
            Dictionary<string, TableMetadata> byName = tableSummaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            bool hasSearched = show || entry.HasValue || search != null || population != null;
            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            int effectivePageSize = NormalizePageSize(pageSize ?? preferences.DefaultPageSize);
            int effectiveMemberPageSize = NormalizePageSize(memberPageSize ?? preferences.DefaultPageSize);

            PoolViewerViewModel model = new()
            {
                Database = database,
                CatalogName = schemaProfile.Name,
                CatalogVersion = schemaProfile.Version,
                Search = search?.Trim() ?? string.Empty,
                Population = PoolPopulationFilters.Normalize(population),
                HasSearched = hasSearched,
                IsAvailable = byName.ContainsKey("pool_template"),
                AvailableTables = new[]
                    {
                        "pool_template",
                        PoolMemberSources.CreatureSpawn,
                        PoolMemberSources.CreatureTemplate,
                        PoolMemberSources.GameObjectSpawn,
                        PoolMemberSources.GameObjectTemplate,
                        PoolMemberSources.ChildPool
                    }
                    .Where(byName.ContainsKey)
                    .ToArray(),
                SelectedEntry = entry,
                Pools = new PagedResult<PoolSummary> { Page = 1, PageSize = effectivePageSize }
            };

            if (!model.IsAvailable)
            {
                return View(model);
            }

            TableMetadata poolTemplate = await _schema.GetTableAsync(profile, database, byName["pool_template"].Name, cancellationToken);
            TableMetadata? poolCreature = await LoadOptionalTableAsync(profile, database, byName, PoolMemberSources.CreatureSpawn, cancellationToken);
            TableMetadata? poolCreatureTemplate = await LoadOptionalTableAsync(profile, database, byName, PoolMemberSources.CreatureTemplate, cancellationToken);
            TableMetadata? poolGameObject = await LoadOptionalTableAsync(profile, database, byName, PoolMemberSources.GameObjectSpawn, cancellationToken);
            TableMetadata? poolGameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, PoolMemberSources.GameObjectTemplate, cancellationToken);
            TableMetadata? poolPool = await LoadOptionalTableAsync(profile, database, byName, PoolMemberSources.ChildPool, cancellationToken);

            try
            {
                model.EditorCapabilities = _pools.GetEditorCapabilities(
                    poolTemplate,
                    poolCreature,
                    poolCreatureTemplate,
                    poolGameObject,
                    poolGameObjectTemplate,
                    poolPool);
            }
            catch (InvalidOperationException exception)
            {
                model.IsAvailable = false;
                ModelState.AddModelError(string.Empty, "Pool Viewer could not read this pool_template schema: " + exception.Message);
                return View(model);
            }

            model.CanCreatePools = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded;
            model.CanEditPools = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded;
            model.CanDeletePools = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded;
            model.CanAddMembers = model.CanCreatePools && model.EditorCapabilities.MemberSources.Count > 0;
            model.CanEditMembers = model.CanEditPools && model.EditorCapabilities.MemberSources.Count > 0;
            model.CanRemoveMembers = model.CanDeletePools && model.EditorCapabilities.MemberSources.Count > 0;

            if (!hasSearched)
            {
                return View(model);
            }

            int effectivePage = Math.Max(1, page);
            model.Pools = await _pools.SearchAsync(
                profile,
                poolTemplate,
                poolCreature,
                poolCreatureTemplate,
                poolGameObject,
                poolGameObjectTemplate,
                poolPool,
                model.Search,
                model.Population,
                effectivePage,
                effectivePageSize,
                cancellationToken);
            if (model.Pools.TotalPages > 0 && model.Pools.Page > model.Pools.TotalPages)
            {
                effectivePage = model.Pools.TotalPages;
                model.Pools = await _pools.SearchAsync(
                    profile,
                    poolTemplate,
                    poolCreature,
                    poolCreatureTemplate,
                    poolGameObject,
                    poolGameObjectTemplate,
                    poolPool,
                    model.Search,
                    model.Population,
                    effectivePage,
                    effectivePageSize,
                    cancellationToken);
            }

            if (entry.HasValue)
            {
                TableMetadata? creature = await LoadOptionalTableAsync(profile, database, byName, "creature", cancellationToken);
                TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
                TableMetadata? gameObject = await LoadOptionalTableAsync(profile, database, byName, "gameobject", cancellationToken);
                TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);
                model.SelectedPool = await _pools.GetAsync(
                    profile,
                    poolTemplate,
                    poolCreature,
                    poolCreatureTemplate,
                    poolGameObject,
                    poolGameObjectTemplate,
                    poolPool,
                    creature,
                    creatureTemplate,
                    gameObject,
                    gameObjectTemplate,
                    entry.Value,
                    Math.Max(1, memberPage),
                    effectiveMemberPageSize,
                    cancellationToken);
                if (model.SelectedPool == null)
                {
                    ModelState.AddModelError(string.Empty, "The selected pool could not be found in pool_template.");
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle pool create within the world editor controller workflow.
        public async Task<IActionResult> PoolCreate(PoolCreatePostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (PoolTemplate, PoolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                TableMetadata poolTemplate = PoolTemplate;
                PoolMutationResult result = await _pools.AddPoolAsync(
                    profile,
                    poolTemplate,
                    new PoolTemplateValues
                    {
                        Entry = model.NewEntry,
                        MaxLimit = model.MaxLimit,
                        Description = model.Description
                    },
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "INSERT",
                    result.ObjectName,
                    "Created pool " + model.NewEntry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                TempData["Success"] = "Pool created.";
                model.Entry = model.NewEntry;
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not create the pool: " + exception.Message;
            }

            return RedirectToPool(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle pool edit within the world editor controller workflow.
        public async Task<IActionResult> PoolEdit(PoolEditPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (PoolTemplate, PoolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                TableMetadata poolTemplate = PoolTemplate;
                PoolMutationResult result = await _pools.UpdatePoolAsync(
                    profile,
                    poolTemplate,
                    model.Entry,
                    new PoolTemplateValues
                    {
                        Entry = model.Entry,
                        MaxLimit = model.MaxLimit,
                        Description = model.Description
                    },
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "UPDATE",
                    result.ObjectName,
                    "Updated pool " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Pool details updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the pool: " + exception.Message;
            }

            return RedirectToPool(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle pool delete within the world editor controller workflow.
        public async Task<IActionResult> PoolDelete(PoolDeletePostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata poolTemplate, IReadOnlyDictionary<string, TableMetadata> poolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                poolTables.TryGetValue(PoolMemberSources.CreatureSpawn, out TableMetadata? poolCreature);
                poolTables.TryGetValue(PoolMemberSources.CreatureTemplate, out TableMetadata? poolCreatureTemplate);
                poolTables.TryGetValue(PoolMemberSources.GameObjectSpawn, out TableMetadata? poolGameObject);
                poolTables.TryGetValue(PoolMemberSources.GameObjectTemplate, out TableMetadata? poolGameObjectTemplate);
                poolTables.TryGetValue(PoolMemberSources.ChildPool, out TableMetadata? poolPool);
                PoolMutationResult result = await _pools.DeletePoolAsync(
                    profile,
                    poolTemplate,
                    poolCreature,
                    poolCreatureTemplate,
                    poolGameObject,
                    poolGameObjectTemplate,
                    poolPool,
                    model.Entry,
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "DELETE",
                    result.ObjectName,
                    "Deleted pool " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                TempData["Success"] = "Pool deleted.";
                return RedirectToAction(nameof(Pools), new
                {
                    database = model.Database,
                    search = model.Search,
                    population = PoolPopulationFilters.Normalize(model.Population),
                    show = true,
                    page = Math.Max(1, model.Page),
                    pageSize = NormalizePageSize(model.PageSize)
                });
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not delete the pool: " + exception.Message;
                return RedirectToPool(model, true);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle pool add member within the world editor controller workflow.
        public async Task<IActionResult> PoolAddMember(PoolMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata poolTemplate, IReadOnlyDictionary<string, TableMetadata> poolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                PoolMutationResult result = await _pools.AddMemberAsync(
                    profile,
                    poolTemplate,
                    poolTables,
                    model.Entry,
                    new PoolMemberValues
                    {
                        Source = model.Source,
                        MemberId = model.MemberId,
                        Chance = model.Chance,
                        Description = model.Description
                    },
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "INSERT",
                    result.ObjectName,
                    "Added " + model.Source + " member " + model.MemberId.ToString(CultureInfo.InvariantCulture) + " to pool " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                TempData["Success"] = "Pool member added.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not add the pool member: " + exception.Message;
            }

            return RedirectToPool(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle pool edit member within the world editor controller workflow.
        public async Task<IActionResult> PoolEditMember(PoolEditMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                (TableMetadata poolTemplate, IReadOnlyDictionary<string, TableMetadata> poolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                PoolMutationResult result = await _pools.UpdateMemberAsync(
                    profile,
                    poolTemplate,
                    poolTables,
                    model.Entry,
                    model.OriginalMemberId,
                    new PoolMemberValues
                    {
                        Source = model.Source,
                        MemberId = model.MemberId,
                        Chance = model.Chance,
                        Description = model.Description
                    },
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "UPDATE",
                    result.ObjectName,
                    "Updated " + model.Source + " member " + model.OriginalMemberId.ToString(CultureInfo.InvariantCulture) + " in pool " + model.Entry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    Math.Max(1, result.AffectedRows));
                TempData["Success"] = "Pool member updated.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not update the pool member: " + exception.Message;
            }

            return RedirectToPool(model, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle pool remove member within the world editor controller workflow.
        public async Task<IActionResult> PoolRemoveMember(PoolMemberPostModel model, CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            try
            {
                var (PoolTemplate, PoolTables) = await GetPoolMutationTablesAsync(profile, model.Database, cancellationToken);
                IReadOnlyDictionary<string, TableMetadata> poolTables = PoolTables;
                long membershipPoolEntry = model.MembershipPoolEntry ?? model.Entry;
                PoolMutationResult result = await _pools.RemoveMemberAsync(
                    profile,
                    poolTables,
                    membershipPoolEntry,
                    new PoolMemberValues
                    {
                        Source = model.Source,
                        MemberId = model.MemberId,
                        Chance = model.Chance,
                        Description = model.Description
                    },
                    cancellationToken);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.PoolViewer,
                    "DELETE",
                    result.ObjectName,
                    "Removed " + model.Source + " member " + model.MemberId.ToString(CultureInfo.InvariantCulture) + " from pool " + membershipPoolEntry.ToString(CultureInfo.InvariantCulture) + ".",
                    result.SqlPreview,
                    true,
                    cancellationToken,
                    result.RevertSqlPreview,
                    result.AffectedRows);
                TempData["Success"] = "Pool member removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = "Could not remove the pool member: " + exception.Message;
            }

            return RedirectToPool(model, true);
        }

        // Handle browse within the world editor controller workflow.
        public async Task<IActionResult> Browse(
            string database,
            string table,
            int? page = null,
            int? pageSize = null,
            string? search = null,
            string? sort = null,
            bool? descending = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            UserPreferences preferences = await GetPreferencesAsync(cancellationToken);
            Guid userId = GetCurrentUserId();
            bool hasExplicitBrowseState = page.HasValue ||
                                          pageSize.HasValue ||
                                          search != null ||
                                          sort != null ||
                                          descending.HasValue;
            WorldBrowseState? rememberedState;

            if (hasExplicitBrowseState)
            {
                // Explicit browsing, searching, sorting, or paging starts a normal
                // browser request and must cancel any unused return operation.
                WorldBrowseStateSession.ClearPendingReturn(
                    HttpContext.Session,
                    userId,
                    profile.Id,
                    database,
                    table);

                rememberedState = null;
            }
            else
            {
                // A saved position is restored only once after Back, Cancel, Save,
                // Create, or Delete. Normal navigation opens page 1.
                rememberedState = WorldBrowseStateSession.ConsumePendingReturn(
                    HttpContext.Session,
                    userId,
                    profile.Id,
                    database,
                    table);
            }

            int effectivePage = Math.Max(1, page ?? rememberedState?.Page ?? 1);
            int effectivePageSize = NormalizePageSize(pageSize ?? rememberedState?.PageSize ?? preferences.DefaultPageSize);
            string effectiveSearch = search ?? rememberedState?.Search ?? string.Empty;
            string effectiveSort = sort ?? rememberedState?.Sort ?? string.Empty;
            bool effectiveDescending = descending ?? rememberedState?.Descending ?? false;

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            PagedResult<TableRow> result = await _tables.BrowseAsync(
                profile,
                metadata,
                effectivePage,
                effectivePageSize,
                preferences.BrowserColumnCount,
                effectiveSearch,
                effectiveSort,
                effectiveDescending,
                cancellationToken);

            if (result.TotalPages > 0 && result.Page > result.TotalPages)
            {
                effectivePage = result.TotalPages;
                result = await _tables.BrowseAsync(
                    profile,
                    metadata,
                    effectivePage,
                    effectivePageSize,
                    preferences.BrowserColumnCount,
                    effectiveSearch,
                    effectiveSort,
                    effectiveDescending,
                    cancellationToken);
            }

            WorldBrowseStateSession.Write(
                HttpContext.Session,
                userId,
                profile.Id,
                database,
                table,
                new WorldBrowseState
                {
                    Page = result.Page,
                    PageSize = result.PageSize,
                    Search = effectiveSearch,
                    Sort = effectiveSort,
                    Descending = effectiveDescending
                });

            MangosTableCategory category = WorldEditorPresentation.ResolveCategory(_catalog, table);

            return View(new TableBrowseViewModel
            {
                Table = metadata,
                Page = result,
                Search = effectiveSearch,
                Sort = effectiveSort,
                Descending = effectiveDescending,
                DisplayName = WorldEditorPresentation.Humanize(metadata.Name),
                CategoryName = category.Name,
                CategoryIcon = category.Icon,
                VisibleColumnCount = preferences.BrowserColumnCount,
                CellPreviewLength = preferences.CellPreviewLength,
                ShowTechnicalColumnNames = preferences.ShowTechnicalColumnNames,
                CanBrowseDatabaseStructure = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureView)).Succeeded,
                CanCreate = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldCreate)).Succeeded,
                CanEdit = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldEdit)).Succeeded,
                CanDelete = (await _authorization.AuthorizeAsync(User, AppPermissions.WorldDelete)).Succeeded
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(string database, string table, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaForEditorAsync(
                profile,
                database,
                cancellationToken);
            Dictionary<string, string?> values = metadata.Columns
                .Where(column => !column.IsAutoIncrement && !column.IsGenerated)
                .ToDictionary(
                    column => column.Name,
                    column => (string?)WorldEditorValueFormatter.FormatForEditor(column, column.DefaultValue),
                    StringComparer.OrdinalIgnoreCase);
            HashSet<string> nullColumns = new(
                metadata.Columns
                    .Where(column => !column.IsAutoIncrement && !column.IsGenerated && column.IsNullable && column.DefaultValue == null)
                    .Select(column => column.Name),
                StringComparer.OrdinalIgnoreCase);

            string returnToken = WorldBrowseStateSession.CaptureReturnState(
                HttpContext.Session,
                GetCurrentUserId(),
                profile.Id,
                database,
                table);

            return View(
                "Edit",
                await BuildEditViewModelAsync(
                    metadata,
                    values,
                    nullColumns,
                    string.Empty,
                    true,
                    returnToken,
                    schemaProfile,
                    cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldCreate)]
        // Handle the create page or action with the surrounding authorization checks.
        public async Task<IActionResult> Create(TableEditPostModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, model.Database, model.Table, cancellationToken);
            HashSet<string> nullColumns = new(model.NullColumns, StringComparer.OrdinalIgnoreCase);
            try
            {
                long? insertedIdentity = await _tables.InsertAsync(profile, metadata, model.Values, nullColumns, cancellationToken);
                string storedSql = ValidateCapturedSql(model.ChangeSql, "INSERT INTO " + QuoteIdentifier(metadata.Name));
                string revertSql = BuildCreateRevertStatement(metadata, model.Values, nullColumns, insertedIdentity);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.WorldEditor,
                    "INSERT",
                    metadata.Name,
                    "Created row in " + metadata.Name + ".",
                    storedSql,
                    !string.IsNullOrWhiteSpace(storedSql),
                    cancellationToken,
                    revertSql,
                    string.IsNullOrWhiteSpace(revertSql) ? 0 : 1);
                TempData["Success"] = "Row created in " + metadata.Name + ".";

                WorldBrowseStateSession.QueueCapturedReturn(
                    HttpContext.Session,
                    GetCurrentUserId(),
                    profile.Id,
                    model.Database,
                    model.Table,
                    model.ReturnToken);

                return RedirectToAction(nameof(Browse), new
                {
                    database = model.Database,
                    table = model.Table
                });
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                MangosSchemaProfile schemaProfile = await DetectSchemaForEditorAsync(
                    profile,
                    model.Database,
                    cancellationToken);
                return View(
                    "Edit",
                    await BuildEditViewModelAsync(
                        metadata,
                        model.Values,
                        nullColumns,
                        string.Empty,
                        true,
                        model.ReturnToken,
                        schemaProfile,
                        cancellationToken));
            }
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(string database, string table, string key, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            MangosSchemaProfile schemaProfile = await DetectSchemaForEditorAsync(
                profile,
                database,
                cancellationToken);
            IReadOnlyDictionary<string, string?> keys = RowKeyCodec.Decode(key);
            TableRow? row = await _tables.GetRowAsync(profile, metadata, keys, cancellationToken);
            if (row == null)
            {
                return NotFound();
            }

            Dictionary<string, ColumnMetadata> columnsByName = metadata.Columns.ToDictionary(
                column => column.Name,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string?> values = row.Values.ToDictionary(
                item => item.Key,
                item => columnsByName.TryGetValue(item.Key, out ColumnMetadata? column)
                    ? WorldEditorValueFormatter.FormatForEditor(column, item.Value)
                    : Convert.ToString(item.Value, CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> nullColumns = new(
                row.Values.Where(item => item.Value == null).Select(item => item.Key),
                StringComparer.OrdinalIgnoreCase);

            string returnToken = WorldBrowseStateSession.CaptureReturnState(
                HttpContext.Session,
                GetCurrentUserId(),
                profile.Id,
                database,
                table);

            return View(
                await BuildEditViewModelAsync(
                    metadata,
                    values,
                    nullColumns,
                    key,
                    false,
                    returnToken,
                    schemaProfile,
                    cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldEdit)]
        // Handle the edit page or action with the surrounding authorization checks.
        public async Task<IActionResult> Edit(TableEditPostModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, model.Database, model.Table, cancellationToken);
            HashSet<string> nullColumns = new(model.NullColumns, StringComparer.OrdinalIgnoreCase);
            try
            {
                IReadOnlyDictionary<string, string?> keys = RowKeyCodec.Decode(model.KeyToken);
                TableRow? originalRow = await _tables.GetRowAsync(profile, metadata, keys, cancellationToken) ?? throw new InvalidOperationException("The row could not be updated because the original row no longer exists.");
                await _tables.UpdateAsync(profile, metadata, keys, model.Values, nullColumns, cancellationToken);
                string storedSql = ValidateCapturedSql(model.ChangeSql, "UPDATE " + QuoteIdentifier(metadata.Name));
                string revertSql = BuildUpdateRevertStatement(metadata, keys, originalRow.Values, model.Values, nullColumns);
                await RecordStoredChangeAsync(
                    profile,
                    model.Database,
                    StoredChangeSources.WorldEditor,
                    "UPDATE",
                    metadata.Name,
                    "Updated row in " + metadata.Name + ".",
                    storedSql,
                    !string.IsNullOrWhiteSpace(storedSql),
                    cancellationToken,
                    revertSql,
                    string.IsNullOrWhiteSpace(revertSql) ? 0 : 1);
                TempData["Success"] = "Row updated in " + metadata.Name + ".";

                WorldBrowseStateSession.QueueCapturedReturn(
                    HttpContext.Session,
                    GetCurrentUserId(),
                    profile.Id,
                    model.Database,
                    model.Table,
                    model.ReturnToken);

                return RedirectToAction(nameof(Browse), new
                {
                    database = model.Database,
                    table = model.Table
                });
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                MangosSchemaProfile schemaProfile = await DetectSchemaForEditorAsync(
                    profile,
                    model.Database,
                    cancellationToken);
                return View(
                    await BuildEditViewModelAsync(
                        metadata,
                        model.Values,
                        nullColumns,
                        model.KeyToken,
                        false,
                        model.ReturnToken,
                        schemaProfile,
                        cancellationToken));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.WorldDelete)]
        // Handle the delete page or action with the surrounding authorization checks.
        public async Task<IActionResult> Delete(string database, string table, string key, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            IReadOnlyDictionary<string, string?> keys = RowKeyCodec.Decode(key);
            TableRow? originalRow = await _tables.GetRowAsync(profile, metadata, keys, cancellationToken);
            if (originalRow == null)
            {
                TempData["Error"] = "The row could not be deleted because it no longer exists.";
                return RedirectToAction(nameof(Browse), new
                {
                    database,
                    table
                });
            }

            string storedSql = BuildDeleteChangeStatement(metadata, keys);
            string revertSql = BuildDeletedRowRestoreStatement(metadata, originalRow.Values);
            await _tables.DeleteAsync(profile, metadata, keys, cancellationToken);
            await RecordStoredChangeAsync(
                profile,
                database,
                StoredChangeSources.WorldEditor,
                "DELETE",
                metadata.Name,
                "Deleted row from " + metadata.Name + ".",
                storedSql,
                true,
                cancellationToken,
                revertSql,
                string.IsNullOrWhiteSpace(revertSql) ? 0 : 1);
            TempData["Success"] = "Row deleted from " + metadata.Name + ".";

            WorldBrowseStateSession.QueueCurrentReturn(
                HttpContext.Session,
                GetCurrentUserId(),
                profile.Id,
                database,
                table);

            return RedirectToAction(nameof(Browse), new
            {
                database,
                table
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle return to browse within the world editor controller workflow.
        public async Task<IActionResult> ReturnToBrowse(
            TableEditPostModel model,
            CancellationToken cancellationToken)
        {
            ConnectionProfile? profile =
                await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);

            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            WorldBrowseStateSession.QueueCapturedReturn(
                HttpContext.Session,
                GetCurrentUserId(),
                profile.Id,
                model.Database,
                model.Table,
                model.ReturnToken);

            return RedirectToAction(nameof(Browse), new
            {
                database = model.Database,
                table = model.Table
            });
        }

        // Build edit view model as part of the authorized desktop request.
        private async Task<TableEditViewModel> BuildEditViewModelAsync(
            TableMetadata metadata,
            IDictionary<string, string?> values,
            ISet<string> nullColumns,
            string keyToken,
            bool isCreate,
            string returnToken,
            MangosSchemaProfile schemaProfile,
            CancellationToken cancellationToken)
        {
            MangosTableCategory category = WorldEditorPresentation.ResolveCategory(_catalog, metadata.Name);
            IReadOnlyDictionary<string, MangosFieldEditorDefinition> fieldEditors =
                await _fieldDataCatalog.ResolveEditorsAsync(
                    schemaProfile.Generation,
                    metadata.Name,
                    metadata.Columns.Select(column => column.Name),
                    cancellationToken);
            return new TableEditViewModel
            {
                Table = metadata,
                Values = values,
                NullColumns = nullColumns,
                KeyToken = keyToken,
                ReturnToken = returnToken,
                IsCreate = isCreate,
                DisplayName = WorldEditorPresentation.Humanize(metadata.Name),
                CategoryName = category.Name,
                CategoryIcon = category.Icon,
                RecordTitle = WorldEditorPresentation.ResolveRecordTitle(metadata, values, isCreate),
                RecordIdentifier = WorldEditorPresentation.ResolveRecordIdentifier(metadata, values),
                SchemaProfile = schemaProfile,
                FieldEditors = fieldEditors,
                FieldGroups = WorldEditorPresentation.BuildFieldGroups(metadata),
                PreviewFields = WorldEditorPresentation.BuildPreviewFields(metadata, values)
            };
        }

        // Build create revert statement as part of the authorized desktop request.
        private static string BuildCreateRevertStatement(
            TableMetadata metadata,
            Dictionary<string, string?> values,
            HashSet<string> nullColumns,
            long? insertedIdentity)
        {
            if (metadata.PrimaryKeyColumns.Count == 0)
            {
                return string.Empty;
            }

            List<string> guards = new();
            foreach (ColumnMetadata keyColumn in metadata.PrimaryKeyColumns)
            {
                if (keyColumn.IsGenerated && !keyColumn.IsAutoIncrement)
                {
                    return string.Empty;
                }

                if (keyColumn.IsAutoIncrement)
                {
                    if (!insertedIdentity.HasValue)
                    {
                        return string.Empty;
                    }

                    guards.Add(
                        QuoteIdentifier(keyColumn.Name) + " <=> " +
                        SqlLiteralFromStoredValue(keyColumn, insertedIdentity.Value));
                    continue;
                }

                if (!values.TryGetValue(keyColumn.Name, out string? keyValue))
                {
                    return string.Empty;
                }

                guards.Add(
                    QuoteIdentifier(keyColumn.Name) + " <=> " +
                    SqlLiteralFromEditorValue(keyColumn, keyValue, nullColumns.Contains(keyColumn.Name)));
            }

            foreach (ColumnMetadata column in metadata.Columns.Where(column =>
                         !column.IsPrimaryKey &&
                         !column.IsAutoIncrement &&
                         !column.IsGenerated &&
                         values.ContainsKey(column.Name)))
            {
                values.TryGetValue(column.Name, out string? value);
                guards.Add(
                    QuoteIdentifier(column.Name) + " <=> " +
                    SqlLiteralFromEditorValue(column, value, nullColumns.Contains(column.Name)));
            }

            if (guards.Count == 0)
            {
                return string.Empty;
            }

            return "DELETE FROM " + QuoteIdentifier(metadata.Name) + " WHERE " + string.Join(" AND ", guards) + " LIMIT 1;";
        }

        // Build update revert statement as part of the authorized desktop request.
        private static string BuildUpdateRevertStatement(
            TableMetadata metadata,
            IReadOnlyDictionary<string, string?> keys,
            IDictionary<string, object?> originalValues,
            Dictionary<string, string?> replacementValues,
            HashSet<string> replacementNullColumns)
        {
            List<ColumnMetadata> updateColumns = metadata.Columns
                .Where(column =>
                    !column.IsGenerated &&
                    !ColumnEditPolicy.IsLockedIdentifier(metadata, column) &&
                    replacementValues.ContainsKey(column.Name) &&
                    originalValues.ContainsKey(column.Name))
                .ToList();
            if (updateColumns.Count == 0)
            {
                return string.Empty;
            }

            string setClause = string.Join(",\n    ", updateColumns.Select(column =>
                QuoteIdentifier(column.Name) + " = " +
                SqlLiteralFromStoredValue(column, originalValues[column.Name])));

            List<string> guards = new();
            foreach (ColumnMetadata keyColumn in metadata.PrimaryKeyColumns)
            {
                if (!keys.TryGetValue(keyColumn.Name, out string? keyValue))
                {
                    return string.Empty;
                }

                guards.Add(
                    QuoteIdentifier(keyColumn.Name) + " <=> " +
                    SqlLiteralFromEditorValue(keyColumn, keyValue, keyValue == null));
            }

            foreach (ColumnMetadata column in updateColumns)
            {
                replacementValues.TryGetValue(column.Name, out string? value);
                guards.Add(
                    QuoteIdentifier(column.Name) + " <=> " +
                    SqlLiteralFromEditorValue(column, value, replacementNullColumns.Contains(column.Name)));
            }

            return "UPDATE " + QuoteIdentifier(metadata.Name) + " SET\n    " +
                   setClause + "\nWHERE " + string.Join(" AND ", guards) + " LIMIT 1;";
        }

        // Build deleted row restore statement as part of the authorized desktop request.
        private static string BuildDeletedRowRestoreStatement(
            TableMetadata metadata,
            IDictionary<string, object?> rowValues)
        {
            List<ColumnMetadata> columns = metadata.Columns
                .Where(column => !column.IsGenerated && rowValues.ContainsKey(column.Name))
                .ToList();
            if (columns.Count == 0)
            {
                return string.Empty;
            }

            return "INSERT INTO " + QuoteIdentifier(metadata.Name) + " (" +
                   string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name))) +
                   ") VALUES (" +
                   string.Join(", ", columns.Select(column => SqlLiteralFromStoredValue(column, rowValues[column.Name]))) +
                   ");";
        }

        // Prepare the sql literal from editor value statement with schema-aware column handling.
        private static string SqlLiteralFromEditorValue(
            ColumnMetadata column,
            string? value,
            bool useNull)
        {
            if (useNull)
            {
                return "NULL";
            }

            string raw = value ?? string.Empty;
            if (column.IsNumeric)
            {
                // Match the parameterized editor behavior without ever embedding raw numeric input
                // into the executable rollback statement. MySQL safely coerces the quoted value
                // back to the destination numeric type.
                return QuoteSqlValue(string.IsNullOrWhiteSpace(raw) ? "0" : raw);
            }

            if (IsBinaryColumn(column))
            {
                return "FROM_BASE64(" + QuoteSqlValue(raw) + ")";
            }

            return QuoteSqlValue(raw);
        }

        // Prepare the sql literal from stored value statement with schema-aware column handling.
        private static string SqlLiteralFromStoredValue(
            ColumnMetadata column,
            object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "NULL";
            }

            if (column.IsNumeric)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            }

            if (IsBinaryColumn(column))
            {
                string base64 = value is byte[] bytes
                    ? Convert.ToBase64String(bytes)
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return "FROM_BASE64(" + QuoteSqlValue(base64) + ")";
            }

            string formatted = value switch
            {
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'),
                DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly timeOnly => timeOnly.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'),
                TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
            return QuoteSqlValue(formatted);
        }

        // Determine whether binary column as part of the authorized desktop request.
        private static bool IsBinaryColumn(ColumnMetadata column)
        {
            string type = (column.DataType ?? string.Empty).ToLowerInvariant();
            return type == "binary" || type == "varbinary" || type.Contains("blob", StringComparison.Ordinal);
        }

        // Build delete change statement as part of the authorized desktop request.
        private static string BuildDeleteChangeStatement(
            TableMetadata metadata,
            IReadOnlyDictionary<string, string?> keys)
        {
            string where = string.Join(
                " AND ",
                metadata.PrimaryKeyColumns.Select(column =>
                {
                    keys.TryGetValue(column.Name, out string? value);
                    return value == null
                        ? QuoteIdentifier(column.Name) + " IS NULL"
                        : QuoteIdentifier(column.Name) + " = " + QuoteSqlValue(value);
                }));

            return "DELETE FROM " + QuoteIdentifier(metadata.Name) +
                   " WHERE " + where + " LIMIT 1;";
        }

        // Escape and quote identifier as part of the authorized desktop request.
        private static string QuoteIdentifier(string value)
        {
            return "`" + (value ?? string.Empty).Replace("`", "``", StringComparison.Ordinal) + "`";
        }

        // Escape and quote sql value as part of the authorized desktop request.
        private static string QuoteSqlValue(string value)
        {
            return "'" + (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        // Handle record stored change async within the world editor controller workflow.
        private async Task RecordStoredChangeAsync(
            ConnectionProfile profile,
            string database,
            string source,
            string operation,
            string objectName,
            string summary,
            string sql,
            bool contributionEligible,
            CancellationToken cancellationToken,
            string revertSql = "",
            int revertExpectedRows = 0)
        {
            try
            {
                await _changeStorage.RecordAsync(new StoredDatabaseChange
                {
                    UserId = GetCurrentUserId(),
                    Username = User.Identity?.Name ?? string.Empty,
                    ConnectionId = profile.Id,
                    ConnectionName = profile.Name,
                    DatabaseName = database,
                    Source = source,
                    Operation = operation,
                    ObjectName = objectName,
                    Summary = summary,
                    SqlText = sql ?? string.Empty,
                    RevertSqlText = revertSql ?? string.Empty,
                    RevertExpectedRows = Math.Max(0, revertExpectedRows),
                    ContributionEligible = contributionEligible
                }, cancellationToken);
            }
            catch (Exception)
            {
                TempData["Warning"] = "The database change completed, but its history could not be saved to Change Storage.";
            }
        }

        // Validate captured sql as part of the authorized desktop request.
        private static string ValidateCapturedSql(string? sql, string expectedPrefix)
        {
            string normalized = (sql ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(normalized) && normalized.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                ? normalized
                : string.Empty;
        }

        // Read preferences as part of the authorized desktop request.
        private async Task<UserPreferences> GetPreferencesAsync(CancellationToken cancellationToken)
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(value, out Guid id))
            {
                return new UserPreferences();
            }

            ApplicationUser? user = await _users.GetAsync(id, cancellationToken);
            return UserPreferences.Normalize(user?.Preferences);
        }

        // Read current user id as part of the authorized desktop request.
        private Guid GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }

        // Detect schema for editor as part of the authorized desktop request.
        private async Task<MangosSchemaProfile> DetectSchemaForEditorAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(
                profile,
                database,
                cancellationToken);
            return await DetectSchemaAsync(profile, database, tables, cancellationToken);
        }

        // Read trainer mutation table as part of the authorized desktop request.
        private async Task<TableMetadata> GetTrainerMutationTableAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            TableMetadata? summary = tables.FirstOrDefault(table => string.Equals(table.Name, "npc_trainer", StringComparison.OrdinalIgnoreCase));
            return summary == null
                ? throw new InvalidOperationException("This database does not expose npc_trainer.")
                : await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken);
        }

        // Handle redirect to trainer within the world editor controller workflow.
        private RedirectToActionResult RedirectToTrainer(NpcTrainerMutationPostModel model)
        {
            return RedirectToAction(nameof(NpcTrainers), new
            {
                database = model.Database,
                search = model.Search,
                show = true,
                entry = model.Entry,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                spellPage = Math.Max(1, model.SpellPage),
                spellPageSize = NormalizePageSize(model.SpellPageSize)
            });
        }

        // Read vendor mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata NpcVendor, TableMetadata ItemTemplate)> GetVendorMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            if (!byName.TryGetValue("npc_vendor", out TableMetadata? npcVendorSummary) ||
                !byName.TryGetValue("item_template", out TableMetadata? itemTemplateSummary))
            {
                throw new InvalidOperationException("This database does not expose npc_vendor and item_template.");
            }

            TableMetadata npcVendor = await _schema.GetTableAsync(profile, database, npcVendorSummary.Name, cancellationToken);
            TableMetadata itemTemplate = await _schema.GetTableAsync(profile, database, itemTemplateSummary.Name, cancellationToken);
            return (npcVendor, itemTemplate);
        }

        // Handle redirect to vendor within the world editor controller workflow.
        private RedirectToActionResult RedirectToVendor(NpcVendorMutationPostModel model)
        {
            return RedirectToAction(
                nameof(NpcVendors),
                new
                {
                    database = model.Database,
                    search = model.Search,
                    show = true,
                    entry = model.Entry,
                    page = Math.Max(1, model.Page),
                    pageSize = NormalizePageSize(model.PageSize),
                    itemPage = Math.Max(1, model.ItemPage),
                    itemPageSize = NormalizePageSize(model.ItemPageSize)
                });
        }

        // Read loot mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata LootTable, TableMetadata? ItemTemplate, TableMetadata? ReferenceLootTable)> GetLootMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            string lootTableName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(lootTableName) || !lootTableName.EndsWith("_loot_template", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A valid loot-template table is required.");
            }

            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            if (!byName.TryGetValue(lootTableName, out TableMetadata? lootSummary))
            {
                throw new InvalidOperationException("Loot table " + lootTableName + " is not available in the selected world database.");
            }

            TableMetadata lootTable = await _schema.GetTableAsync(profile, database, lootSummary.Name, cancellationToken);
            TableMetadata? itemTemplate = await LoadOptionalTableAsync(profile, database, byName, "item_template", cancellationToken);
            TableMetadata? referenceLootTable = await LoadOptionalTableAsync(profile, database, byName, "reference_loot_template", cancellationToken);
            return (lootTable, itemTemplate, referenceLootTable);
        }

        // Handle redirect to loot within the world editor controller workflow.
        private IActionResult RedirectToLoot(LootMutationPostModel model, bool selectEntry)
        {
            string safeReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : string.Empty;
            if (!selectEntry && !string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                return Redirect(safeReturnUrl);
            }

            return RedirectToAction(nameof(Loot), new
            {
                database = model.Database,
                mode = LootViewerModes.Templates,
                search = model.Search,
                tableFilter = model.TableFilter,
                show = true,
                lootTable = selectEntry ? model.LootTable : null,
                entry = selectEntry && model.Entry > 0 ? model.Entry : (long?)null,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                rowPage = Math.Max(1, model.RowPage),
                rowPageSize = NormalizePageSize(model.RowPageSize),
                returnUrl = string.IsNullOrWhiteSpace(safeReturnUrl) ? null : safeReturnUrl
            });
        }

        // Read gossip mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata? GossipMenu, TableMetadata? GossipMenuOption, TableMetadata? NpcText)> GetGossipMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            TableMetadata? gossipMenu = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu", cancellationToken);
            TableMetadata? gossipMenuOption = await LoadOptionalTableAsync(profile, database, byName, "gossip_menu_option", cancellationToken);
            TableMetadata? npcText = await LoadOptionalTableAsync(profile, database, byName, "npc_text", cancellationToken);
            if (gossipMenu == null && gossipMenuOption == null)
            {
                throw new InvalidOperationException("This database does not expose gossip_menu or gossip_menu_option.");
            }

            return (gossipMenu, gossipMenuOption, npcText);
        }

        // Handle supports creature gossip source editing within the world editor controller workflow.
        private static bool SupportsCreatureGossipSourceEditing(TableMetadata? creatureTemplate)
        {
            if (creatureTemplate == null)
            {
                return false;
            }

            bool hasEntry = creatureTemplate.Columns.Any(column => string.Equals(column.Name, "entry", StringComparison.OrdinalIgnoreCase));
            bool hasMenu = creatureTemplate.Columns.Any(column =>
                string.Equals(column.Name, "GossipMenuId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "gossip_menu_id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "gossipMenuId", StringComparison.OrdinalIgnoreCase));
            bool hasFlags = creatureTemplate.Columns.Any(column =>
                string.Equals(column.Name, "NpcFlags", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "npcflag", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "npc_flags", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column.Name, "npcFlags", StringComparison.OrdinalIgnoreCase));
            return hasEntry && hasMenu && hasFlags;
        }

        // Handle redirect to gossip menus within the world editor controller workflow.
        private IActionResult RedirectToGossipMenus(GossipMutationPostModel model, bool selectEntry)
        {
            string safeReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : string.Empty;
            if (!selectEntry && !string.IsNullOrWhiteSpace(safeReturnUrl))
            {
                return Redirect(safeReturnUrl);
            }

            return RedirectToAction(nameof(Menus), new
            {
                database = model.Database,
                search = model.Search,
                show = true,
                entry = selectEntry ? model.Entry : (long?)null,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                textPage = Math.Max(1, model.TextPage),
                textPageSize = NormalizePageSize(model.TextPageSize),
                optionPage = Math.Max(1, model.OptionPage),
                optionPageSize = NormalizePageSize(model.OptionPageSize),
                returnUrl = string.IsNullOrWhiteSpace(safeReturnUrl) ? null : safeReturnUrl
            });
        }

        // Load quest relation tables as part of the authorized desktop request.
        private async Task<Dictionary<string, TableMetadata>> LoadQuestRelationTablesAsync(
            ConnectionProfile profile,
            string database,
            Dictionary<string, TableMetadata> byName,
            CancellationToken cancellationToken)
        {
            Dictionary<string, TableMetadata> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (string tableName in new[]
            {
                "quest_relations", "creature_questrelation", "creature_involvedrelation",
                "gameobject_questrelation", "gameobject_involvedrelation"
            })
            {
                if (!byName.TryGetValue(tableName, out TableMetadata? summary))
                {
                    continue;
                }

                result[tableName] = await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken);
            }

            return result;
        }

        // Read quest mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata QuestTemplate, IReadOnlyDictionary<string, TableMetadata> RelationTables, TableMetadata? CreatureTemplate, TableMetadata? GameObjectTemplate)> GetQuestMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            if (!byName.TryGetValue("quest_template", out TableMetadata? questSummary))
            {
                throw new InvalidOperationException("This database does not expose quest_template.");
            }

            TableMetadata questTemplate = await _schema.GetTableAsync(profile, database, questSummary.Name, cancellationToken);
            Dictionary<string, TableMetadata> relations = await LoadQuestRelationTablesAsync(profile, database, byName, cancellationToken);
            TableMetadata? creatureTemplate = await LoadOptionalTableAsync(profile, database, byName, "creature_template", cancellationToken);
            TableMetadata? gameObjectTemplate = await LoadOptionalTableAsync(profile, database, byName, "gameobject_template", cancellationToken);
            return (questTemplate, relations, creatureTemplate, gameObjectTemplate);
        }

        // Handle to quest values within the world editor controller workflow.
        private static QuestTemplateValues ToQuestValues(
            long entry,
            string? title,
            string? details,
            string? objectives,
            long? minLevel,
            long? questLevel,
            long? prevQuestId,
            long? nextQuestId,
            long? exclusiveGroup,
            long? nextQuestInChain)
        {
            return new QuestTemplateValues
            {
                Entry = entry,
                Title = title?.Trim() ?? string.Empty,
                Details = details ?? string.Empty,
                Objectives = objectives ?? string.Empty,
                MinLevel = minLevel,
                QuestLevel = questLevel,
                PrevQuestId = prevQuestId,
                NextQuestId = nextQuestId,
                ExclusiveGroup = exclusiveGroup,
                NextQuestInChain = nextQuestInChain
            };
        }

        // Handle redirect to quest within the world editor controller workflow.
        private RedirectToActionResult RedirectToQuest(QuestMutationPostModel model, bool selectEntry)
        {
            return RedirectToAction(nameof(Quests), new
            {
                database = model.Database,
                search = model.Search,
                show = true,
                entry = selectEntry && model.Entry > 0 ? model.Entry : (long?)null,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                relationPage = Math.Max(1, model.RelationPage),
                relationPageSize = NormalizePageSize(model.RelationPageSize)
            });
        }

        // Read event mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata GameEvent, IReadOnlyDictionary<string, TableMetadata> EventTables, TableMetadata? Creature, TableMetadata? GameObject)> GetEventMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            if (!byName.TryGetValue("game_event", out TableMetadata? eventSummary))
            {
                throw new InvalidOperationException("This database does not expose game_event.");
            }

            TableMetadata gameEvent = await _schema.GetTableAsync(profile, database, eventSummary.Name, cancellationToken);
            Dictionary<string, TableMetadata> eventTables = new(StringComparer.OrdinalIgnoreCase);
            foreach (string tableName in new[] { EventMemberSources.Creature, EventMemberSources.GameObject })
            {
                if (!byName.TryGetValue(tableName, out TableMetadata? summary))
                {
                    continue;
                }

                eventTables[tableName] = await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken);
            }

            TableMetadata? creature = await LoadOptionalTableAsync(profile, database, byName, "creature", cancellationToken);
            TableMetadata? gameObject = await LoadOptionalTableAsync(profile, database, byName, "gameobject", cancellationToken);
            return (gameEvent, eventTables, creature, gameObject);
        }

        // Handle redirect to event within the world editor controller workflow.
        private RedirectToActionResult RedirectToEvent(EventMutationPostModel model, bool selectEntry)
        {
            return RedirectToAction(nameof(Events), new
            {
                database = model.Database,
                search = model.Search,
                show = true,
                entry = selectEntry && model.Entry > 0 ? model.Entry : (long?)null,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                memberPage = Math.Max(1, model.MemberPage),
                memberPageSize = NormalizePageSize(model.MemberPageSize)
            });
        }

        // Read pool mutation tables as part of the authorized desktop request.
        private async Task<(TableMetadata PoolTemplate, IReadOnlyDictionary<string, TableMetadata> PoolTables)> GetPoolMutationTablesAsync(
            ConnectionProfile profile,
            string database,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TableMetadata> summaries = await _schema.GetTablesAsync(profile, database, cancellationToken);
            Dictionary<string, TableMetadata> byName = summaries.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            if (!byName.TryGetValue("pool_template", out TableMetadata? templateSummary))
            {
                throw new InvalidOperationException("This database does not expose pool_template.");
            }

            TableMetadata poolTemplate = await _schema.GetTableAsync(profile, database, templateSummary.Name, cancellationToken);
            Dictionary<string, TableMetadata> poolTables = new(StringComparer.OrdinalIgnoreCase);
            foreach (string tableName in new[]
                     {
                         PoolMemberSources.CreatureSpawn,
                         PoolMemberSources.CreatureTemplate,
                         PoolMemberSources.GameObjectSpawn,
                         PoolMemberSources.GameObjectTemplate,
                         PoolMemberSources.ChildPool
                     })
            {
                if (!byName.TryGetValue(tableName, out TableMetadata? summary))
                {
                    continue;
                }

                poolTables[tableName] = await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken);
            }

            return (poolTemplate, poolTables);
        }

        // Handle redirect to pool within the world editor controller workflow.
        private RedirectToActionResult RedirectToPool(PoolMutationPostModel model, bool selectEntry)
        {
            return RedirectToAction(nameof(Pools), new
            {
                database = model.Database,
                search = model.Search,
                population = PoolPopulationFilters.Normalize(model.Population),
                show = true,
                entry = selectEntry && model.Entry > 0 ? model.Entry : (long?)null,
                page = Math.Max(1, model.Page),
                pageSize = NormalizePageSize(model.PageSize),
                memberPage = Math.Max(1, model.MemberPage),
                memberPageSize = NormalizePageSize(model.MemberPageSize)
            });
        }

        // Detect schema as part of the authorized desktop request.
        private async Task<MangosSchemaProfile> DetectSchemaAsync(
            ConnectionProfile profile,
            string database,
            IReadOnlyList<TableMetadata> tables,
            CancellationToken cancellationToken)
        {
            Dictionary<string, TableMetadata> metadata = tables.ToDictionary(
                table => table.Name,
                StringComparer.OrdinalIgnoreCase);

            // GetTablesAsync intentionally returns lightweight summaries. Only the
            // stable cross-generation fingerprint tables are hydrated so detection is
            // structural without loading column metadata for every world table.
            foreach (string fingerprintTable in _catalog.StructuralFingerprintTables)
            {
                if (!metadata.TryGetValue(fingerprintTable, out TableMetadata? summary))
                {
                    continue;
                }

                metadata[summary.Name] = await _schema.GetTableAsync(
                    profile,
                    database,
                    summary.Name,
                    cancellationToken);
            }

            return _catalog.Detect(metadata.Values);
        }

        // Load optional table as part of the authorized desktop request.
        private async Task<TableMetadata?> LoadOptionalTableAsync(
            ConnectionProfile profile,
            string database,
            Dictionary<string, TableMetadata> tables,
            string tableName,
            CancellationToken cancellationToken)
        {
            if (!tables.TryGetValue(tableName, out TableMetadata? summary))
            {
                return null;
            }

            return await _schema.GetTableAsync(profile, database, summary.Name, cancellationToken);
        }

        // Normalize double spawn scope as part of the authorized desktop request.
        private static string NormalizeDoubleSpawnScope(string? value)
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

        // Normalize double spawn match mode as part of the authorized desktop request.
        private static string NormalizeDoubleSpawnMatchMode(string? value)
        {
            return string.Equals(value, DoubleSpawnMatchModes.AnyEntry, StringComparison.OrdinalIgnoreCase)
                ? DoubleSpawnMatchModes.AnyEntry
                : DoubleSpawnMatchModes.SameEntry;
        }

        // Normalize double spawn pool filter as part of the authorized desktop request.
        private static string NormalizeDoubleSpawnPoolFilter(string? value)
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

        // Normalize double spawn linked game object filter as part of the authorized desktop request.
        private static string NormalizeDoubleSpawnLinkedGameObjectFilter(string? value)
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

        // Determine whether tables as part of the authorized desktop request.
        private static bool HasTables(IReadOnlyList<TableMetadata> tables, params string[] requiredTables)
        {
            HashSet<string> tableNames = new(
                tables.Select(table => table.Name),
                StringComparer.OrdinalIgnoreCase);
            return requiredTables.All(tableNames.Contains);
        }

        // Clamp requested page sizes to the configured browsing limit.
        private static int NormalizePageSize(int pageSize)
        {
            return pageSize switch
            {
                10 => 10,
                20 => 20,
                50 => 50,
                100 => 100,
                200 => 200,
                _ => 20
            };
        }
    }
}
