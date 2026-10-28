// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Exercise Field Data migration and synchronization against an isolated SQLite database.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Services;
using MangosDatabaseStudio.App.Desktop;
using MangosDatabaseStudio.App.Services.FieldData;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.FieldDataChecks
{
    internal static class Program
    {
        // Fail the CI check on any missing built-in update or lost user addition.
        private static async Task<int> Main()
        {
            SQLitePCL.Batteries_V2.Init();

            try
            {
                CheckVersionPresentation();
                CheckSourceDiscovery();
                await CheckMapNamesSynchronizationAsync();
                await CheckMultiTableSynchronizationAsync();
                await CheckProgressAndRollbackAsync();
                CheckFailedStartupGate();
                Console.WriteLine("Field Data synchronization and startup safety checks passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        // Confirm the version footer accepts embedded release tags and short revisions.
        private static void CheckVersionPresentation()
        {
            ApplicationVersionInfo tagged = new(
                "v1.2.3",
                "Release",
                "1.2.3+abcdef1234567890",
                "abcdef1234567890");

            Check(tagged.DisplayText == "Application: v1.2.3 (abcdef12)",
                "The sidebar is not displaying the tag and short commit hash.");

            // Preserve prerelease suffixes and normalize the repository's v. tag prefix.
            ApplicationVersionInfo prerelease = new(
                "v.0.2.5-beta",
                "Beta",
                "0.2.5-beta+abcdef1234567890",
                "abcdef1234567890");

            Check(prerelease.DisplayText == "Application: v0.2.5-beta (abcdef12)",
                "The sidebar removed the prerelease label or did not normalize a v. tag.");

            ApplicationVersionInfo untagged = new(
                "0.0.0",
                "Development",
                string.Empty,
                null);

            Check(untagged.DisplayText == "Application: v0.0.0 (unknown)",
                "Missing Git metadata should use the explicit unknown fallback.");

            ApplicationVersionInfo embedded = ApplicationVersionInfo.FromAssembly(
                typeof(ApplicationVersionInfo).Assembly);

            Check(!string.IsNullOrWhiteSpace(embedded.Version),
                "The application must expose a version even without Git metadata.");
            Check(!embedded.Version.Contains("@(", StringComparison.Ordinal) &&
                !embedded.Version.Contains("$(", StringComparison.Ordinal),
                "Unevaluated MSBuild expressions were embedded in the application version.");
            Check(embedded.DisplayText.Contains(embedded.CommitHash, StringComparison.Ordinal),
                "The version footer does not contain the embedded commit hash.");
        }

        // Validate the automatic registry includes both special faction factories.
        private static void CheckSourceDiscovery()
        {
            IReadOnlyList<FieldDefinitionSeed> definitions = FieldDataSeedRegistry.Discover();
            HashSet<string> keys = new(
                definitions.Select(definition => definition.SourceKey),
                StringComparer.OrdinalIgnoreCase);

            Check(keys.Count >= 22, "The registered CreatureTemplate source list is incomplete.");
            Check(keys.Contains("builtin.creature_template.faction_alliance"), "Alliance faction data is missing.");
            Check(keys.Contains("builtin.creature_template.faction_horde"), "Horde faction data is missing.");
            Check(keys.Contains("builtin.map_names.creature.map"), "Creature Map Names were not discovered.");
            Check(!keys.Contains("builtin.map_names.gameobject.map"),
                "GameObject Map Names must use the shared dataset, not a duplicate definition.");
        }

        // Ensure map names share a category while retaining expansion-specific choices
        // and preserving administrator-added entries in the existing SQLite catalog.
        private static async Task CheckMapNamesSynchronizationAsync()
        {
            IReadOnlyList<FieldDefinitionSeed> maps = FieldDataSeedRegistry.Discover()
                .Where(definition => definition.SourceKey.StartsWith(
                    "builtin.map_names.", StringComparison.Ordinal))
                .ToArray();

            Check(maps.Count == 1, "Map Names must have only one shared definition.");
            foreach (FieldDefinitionSeed map in maps)
            {
                Check(map.ColumnName == "map", "The Map Names definition must target the map column.");
                Check(map.Variants.Count == 5, "Map Names must provide variants for all five generations.");
                Check(map.Variants[0].Options.Any(option => option.Value == "0" &&
                    option.Label == "Eastern Kingdoms"), "The Vanilla Map Names baseline is missing.");
                Check(!map.Variants[0].Options.Any(option => option.Value == "530"),
                    "Outland must not be offered on a Vanilla server.");
                Check(map.Variants[1].Options.Any(option => option.Value == "530" &&
                    option.Label == "Outland"), "The Burning Crusade map baseline is missing.");
                Check(!map.Variants[1].Options.Any(option => option.Value == "571"),
                    "Northrend must not be offered on a Burning Crusade server.");
                Check(map.Variants[2].Options.Any(option => option.Value == "571" &&
                    option.Label == "Northrend"), "The Wrath map baseline is missing.");
                Check(map.Variants[3].Options.Any(option => option.Value == "646" &&
                    option.Label == "Deepholm"), "The Cataclysm map baseline is missing.");
                Check(!map.Variants[3].Options.Any(option => option.Value == "870"),
                    "Pandaria must not be offered on a Cataclysm server.");
                Check(map.Variants[4].Options.Any(option => option.Value == "870" &&
                    option.Label == "Pandaria"), "The MoP map baseline is missing.");
            }

            await using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            await CreateCatalogSchemaAsync(connection);
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);

            Check(await CountAsync(connection, "field_categories") == 1,
                "Both spawn tables must share one Map Names category.");
            Check(await CountAsync(connection, "field_definitions") == 1,
                "Map Names must have one shared column definition.");
            Check(await CountAsync(connection, "field_variants") == 5,
                "Map Names requires five expansion variants, not duplicated variants.");
            Check(await CountAsync(connection, "field_variant_generations") == 5,
                "Map Names expansion mappings were not stored.");
            Check(await ScalarStringAsync(connection,
                "SELECT name FROM field_categories WHERE category_key = 'reference.map-names';") == "Map Names",
                "The Map Names category was not created.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '530' AND field_variant_id IN " +
                "(SELECT id FROM field_variants WHERE variant_key = 'generation-1') LIMIT 1;") == "Outland",
                "Map names were not written to SQLite.");

            // New administrator options must survive an unchanged-source restart.
            await ExecuteAsync(connection, @"
INSERT INTO field_options(field_variant_id, value, label, sort_order)
SELECT v.id, '987654', 'Custom server map', 999
FROM field_variants AS v
JOIN field_definitions AS d ON d.id = v.field_definition_id
WHERE d.table_name = 'creature' AND v.variant_key = 'generation-4';");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '987654';") == "Custom server map",
                "A custom map entry was lost after startup synchronization.");

            // Respect administrator edits to the category's display properties.
            await ExecuteAsync(connection, @"
UPDATE field_categories SET name = 'Custom Map Labels' WHERE category_key = 'reference.map-names';
UPDATE application_metadata SET value = '13' WHERE name = 'field_catalog_version';");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await ScalarStringAsync(connection,
                "SELECT name FROM field_categories WHERE category_key = 'reference.map-names';") ==
                "Custom Map Labels", "The administrator's Map Names category label was overwritten.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '987654';") == "Custom server map",
                "A custom map option was removed during a catalog update.");

            // An administrator can remove one built-in category or all built-in
            // categories. The unchanged C# fingerprint must not prevent restoration.
            await ExecuteAsync(connection, @"
UPDATE field_definitions SET category_id = NULL
WHERE category_id = (SELECT id FROM field_categories WHERE category_key = 'reference.map-names');
DELETE FROM field_categories WHERE category_key = 'reference.map-names';");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await CountAsync(connection, "field_categories") == 1,
                "Deleted Map Names category was not restored at startup.");
            Check(await ScalarStringAsync(connection, @"
SELECT category_key FROM field_categories
WHERE id = (SELECT category_id FROM field_definitions
            WHERE source_key = 'builtin.map_names.creature.map');") == "reference.map-names",
                "The map definition was not reattached to its recreated category.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '987654';") == "Custom server map",
                "Recreating a category removed a custom map option.");

            // Simulate the old release creating a second, identical Map Names dataset.
            // Additional options and variants added to the old copy must be transferred.
            await ExecuteAsync(connection, @"
INSERT INTO field_definitions(
    source_key, category_id, display_name, table_name, column_name, editor_kind,
    selection_mode, value_handling, preserve_unknown_values, description,
    is_system, is_enabled, priority)
SELECT 'builtin.map_names.gameobject.map', category_id, display_name,
       'gameobject', column_name, editor_kind, selection_mode, value_handling,
       preserve_unknown_values, description, is_system, is_enabled, priority
FROM field_definitions WHERE source_key = 'builtin.map_names.creature.map';
INSERT INTO field_variants(field_definition_id, variant_key, description)
SELECT (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.gameobject.map'),
       variant_key, description
FROM field_variants WHERE field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.creature.map');
INSERT INTO field_variant_generations(field_variant_id, generation)
SELECT legacy.id, generations.generation
FROM field_variants AS shared
JOIN field_variant_generations AS generations ON generations.field_variant_id = shared.id
JOIN field_variants AS legacy ON legacy.variant_key = shared.variant_key
WHERE shared.field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.creature.map')
AND legacy.field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.gameobject.map');
INSERT INTO field_options(field_variant_id, value, label, description, sort_order)
SELECT legacy.id, options.value, options.label, options.description, options.sort_order
FROM field_variants AS shared
JOIN field_options AS options ON options.field_variant_id = shared.id
JOIN field_variants AS legacy ON legacy.variant_key = shared.variant_key
WHERE shared.field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.creature.map')
AND legacy.field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.gameobject.map');
INSERT INTO field_options(field_variant_id, value, label, sort_order)
SELECT id, '999999', 'Legacy custom map', 999
FROM field_variants WHERE variant_key = 'generation-4'
AND field_definition_id =
    (SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.gameobject.map');
INSERT INTO field_variants(field_definition_id, variant_key, description)
VALUES ((SELECT id FROM field_definitions WHERE source_key = 'builtin.map_names.gameobject.map'),
        'legacy-custom-variant', 'Custom generation dataset');
INSERT INTO field_variant_generations(field_variant_id, generation)
SELECT id, 4 FROM field_variants WHERE variant_key = 'legacy-custom-variant';
INSERT INTO field_options(field_variant_id, value, label, sort_order)
SELECT id, '999998', 'Legacy custom variant map', 0
FROM field_variants WHERE variant_key = 'legacy-custom-variant';");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await CountAsync(connection, "field_definitions") == 1,
                "Legacy duplicate map definition was not consolidated.");
            Check(await CountAsync(connection, "field_variants") == 6,
                "The legacy custom variant was not preserved during consolidation.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '999999';") == "Legacy custom map",
                "The unique custom map ID from the duplicate dataset was lost.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '999998';") == "Legacy custom variant map",
                "The legacy custom variant was lost during consolidation.");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await CountAsync(connection, "field_definitions") == 1,
                "Restarting after consolidation duplicated Map Names.");

            // Deleting every category and its children must bootstrap the current built-ins
            // even if the saved fingerprint still matches the C# source.
            await ExecuteAsync(connection, @"
DELETE FROM field_options;
DELETE FROM field_variant_generations;
DELETE FROM field_variants;
DELETE FROM field_definitions;
DELETE FROM field_categories;");
            await MangosFieldDataSeed.ApplyAsync(connection, maps, CancellationToken.None);
            Check(await CountAsync(connection, "field_categories") == 1,
                "Clearing all categories did not reinstall the Map Names category.");
            Check(await CountAsync(connection, "field_definitions") == 1,
                "Clearing all Field Data did not reinstall the map definition.");
            Check(await CountAsync(connection, "field_variants") == 5,
                "Reinstalled Map Names must contain all expansion variants.");
        }

        // Exercise new tables, labels, generation mappings, additional options, and idempotency.
        private static async Task CheckMultiTableSynchronizationAsync()
        {
            await using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            await CreateCatalogSchemaAsync(connection);

            IReadOnlyList<FieldDefinitionSeed> first = CreateDefinitions(false);
            await MangosFieldDataSeed.ApplyAsync(connection, first, CancellationToken.None);

            Check(await CountAsync(connection, "field_categories") == 2, "Source tables need separate categories.");
            Check(await CountAsync(connection, "field_definitions") == 2, "Initial fields were not inserted.");
            Check(await CountAsync(connection, "field_variants") == 2, "Initial variants were not inserted.");
            Check(await CountAsync(connection, "field_variant_generations") == 2, "Initial generation mappings are missing.");
            Check(await CountAsync(connection, "field_options") == 3, "Initial options were not inserted.");

            // Simulate an administrator extending the catalog with a custom option and field.
            await ExecuteAsync(connection, @"
INSERT INTO field_options(field_variant_id, value, label, sort_order)
SELECT id, '99', 'User custom value', 90 FROM field_variants WHERE variant_key = 'demo-default';
INSERT INTO field_variants(field_definition_id, variant_key, description)
SELECT id, 'custom-variant', 'Administrator variant' FROM field_definitions
WHERE source_key = 'builtin.demo_table.mode';
INSERT INTO field_variant_generations(field_variant_id, generation)
SELECT id, 3 FROM field_variants WHERE variant_key = 'custom-variant';
INSERT INTO field_options(field_variant_id, value, label, sort_order)
SELECT id, '100', 'Administrator variant option', 0 FROM field_variants
WHERE variant_key = 'custom-variant';
INSERT INTO field_definitions(
    source_key, table_name, column_name, editor_kind, category_id, display_name,
    selection_mode, value_handling, preserve_unknown_values)
VALUES ('user.demo.extra', 'demo_table', 'Extra', 'Select',
    (SELECT id FROM field_categories WHERE category_key = 'world.demo-table'),
    'Extra', 'Single', 'Direct', 1);");

            // Source data changes but catalog version stays at 14. The fingerprint must
            // trigger another merge and update every affected table in the same transaction.
            IReadOnlyList<FieldDefinitionSeed> second = CreateDefinitions(true);
            await MangosFieldDataSeed.ApplyAsync(connection, second, CancellationToken.None);

            Check(await CountAsync(connection, "field_categories") == 3, "New table category was not inserted.");
            Check(await CountAsync(connection, "field_definitions") == 4, "New table or custom definition is missing.");
            Check(await CountAsync(connection, "field_variants") == 4, "New source or custom variant was lost.");
            Check(await CountAsync(connection, "field_variant_generations") == 5, "Updated generations are missing.");
            Check(await CountAsync(connection, "field_options") == 7, "New values or custom options were lost.");

            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '1' AND field_variant_id = " +
                "(SELECT id FROM field_variants WHERE variant_key = 'demo-default');") == "Updated source label",
                "Existing source labels did not update.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '99';") == "User custom value",
                "Administrator-added field options were overwritten.");
            Check(await ScalarStringAsync(connection,
                "SELECT category_key FROM field_categories WHERE id = " +
                "(SELECT category_id FROM field_definitions WHERE table_name = 'new_table');") == "world.new-table",
                "New table definitions are assigned to the wrong category.");

            // Reopening with unchanged source must skip the costly merge and leave a user's
            // custom changes alone. The next C# source change will sync again automatically.
            await MangosFieldDataSeed.ApplyAsync(connection, second, CancellationToken.None);
            Check(await CountAsync(connection, "field_options") == 7, "Unchanged source duplicated catalog rows.");
            Check((await ScalarStringAsync(connection,
                "SELECT value FROM application_metadata WHERE name = 'field_catalog_source_sha256';"))?.Length == 64,
                "The source fingerprint was not persisted.");

            // Delete only a built-in category, leaving the associated definition in
            // place. Startup must re-create and reattach it without touching user data.
            await ExecuteAsync(connection, @"
UPDATE field_definitions SET category_id = NULL
WHERE category_id = (SELECT id FROM field_categories WHERE category_key = 'world.demo-table');
DELETE FROM field_categories WHERE category_key = 'world.demo-table';");
            await MangosFieldDataSeed.ApplyAsync(connection, second, CancellationToken.None);
            Check(await CountAsync(connection, "field_categories") == 3,
                "Missing built-in category was not restored with an unchanged fingerprint.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '99';") == "User custom value",
                "Restoring a deleted category removed administrator-added options.");

            // Repeated runs must not create duplicates after repairing missing categories.
            await MangosFieldDataSeed.ApplyAsync(connection, second, CancellationToken.None);
            Check(await CountAsync(connection, "field_categories") == 3,
                "Re-running the repair created duplicate categories.");

            // An upgrade from a previously versioned catalog must still reconcile source rows.
            await ExecuteAsync(connection, @"
UPDATE application_metadata SET value = '13' WHERE name = 'field_catalog_version';
UPDATE field_options SET label = 'Outdated source label' WHERE value = '1'
AND field_variant_id = (SELECT id FROM field_variants WHERE variant_key = 'demo-default');
UPDATE field_categories SET name = 'Custom category label', is_enabled = 0
WHERE category_key = 'world.creature-template';");
            await MangosFieldDataSeed.ApplyAsync(connection, second, CancellationToken.None);
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '1' AND field_variant_id = " +
                "(SELECT id FROM field_variants WHERE variant_key = 'demo-default');") == "Updated source label",
                "Legacy versioned catalogs were not updated.");
            Check(await ScalarStringAsync(connection,
                "SELECT name FROM field_categories WHERE category_key = 'world.creature-template';") ==
                "Custom category label", "A user's category name was overwritten.");
            Check(await ScalarStringAsync(connection,
                "SELECT label FROM field_options WHERE value = '100';") == "Administrator variant option",
                "An administrator's custom variant was removed.");
        }

        // A failed catalog merge must preserve the prior fingerprint and roll back ALL table changes.
        private static async Task CheckProgressAndRollbackAsync()
        {
            await using SqliteConnection connection = new("Data Source=:memory:");
            await connection.OpenAsync();
            await CreateCatalogSchemaAsync(connection);

            List<(int Completed, int Total, string Detail)> reports = new();
            IReadOnlyList<FieldDefinitionSeed> original = CreateDefinitions(false);
            await MangosFieldDataSeed.ApplyAsync(connection, original, CancellationToken.None,
                (completed, total, detail) => reports.Add((completed, total, detail)));

            Check(reports.Count >= 3, "The startup did not report the Field Data merge progress.");
            Check(reports[^1].Completed == original.Count, "The initial merge did not report completion.");
            Check(reports.All(report => report.Completed >= 0 && report.Completed <= report.Total),
                "The Field Data progress counts exceeded the catalog size.");

            string? originalFingerprint = await ScalarStringAsync(connection,
                "SELECT value FROM application_metadata WHERE name = 'field_catalog_source_sha256';");

            // Abort at the third definition after the first two have already been visited.
            await ExecuteAsync(connection, @"
CREATE TRIGGER fail_catalog_merge BEFORE INSERT ON field_definitions
WHEN NEW.table_name = 'new_table'
BEGIN
    SELECT RAISE(ABORT, 'Simulated Field Data SQLite failure');
END;");

            reports.Clear();
            bool failed = false;
            try
            {
                await MangosFieldDataSeed.ApplyAsync(connection, CreateDefinitions(true),
                    CancellationToken.None,
                    (completed, total, detail) => reports.Add((completed, total, detail)));
            }
            catch (InvalidOperationException exception)
            {
                failed = exception.Message.Contains("new_table", StringComparison.OrdinalIgnoreCase);
            }

            Check(failed, "The invalid catalog was not rejected with the affected field name.");
            Check(reports.Any(report => report.Detail.Contains("new_table", StringComparison.OrdinalIgnoreCase)),
                "The failing Field Data definition was not included in startup progress.");
            Check(await CountAsync(connection, "field_options") == 3,
                "An unsuccessful merge left partially inserted options in SQLite.");
            Check(await CountAsync(connection, "field_categories") == 2,
                "An unsuccessful merge left partially inserted categories in SQLite.");
            Check(await ScalarStringAsync(connection,
                "SELECT value FROM application_metadata WHERE name = 'field_catalog_source_sha256';") == originalFingerprint,
                "A failed Field Data update advanced the source fingerprint.");

            // The same source must succeed after the underlying database error is resolved.
            await ExecuteAsync(connection, "DROP TRIGGER fail_catalog_merge;");
            reports.Clear();
            IReadOnlyList<FieldDefinitionSeed> updated = CreateDefinitions(true);
            await MangosFieldDataSeed.ApplyAsync(connection, updated, CancellationToken.None,
                (completed, total, detail) => reports.Add((completed, total, detail)));
            Check(reports[^1].Completed == updated.Count, "A successful retry did not reach completion.");
            Check(await CountAsync(connection, "field_categories") == 3,
                "The recovered merge did not insert the new table category.");

            // Unchanged catalogs still tell the startup page that verification succeeded.
            reports.Clear();
            await MangosFieldDataSeed.ApplyAsync(connection, updated, CancellationToken.None,
                (completed, total, detail) => reports.Add((completed, total, detail)));
            Check(reports[^1].Detail.Contains("already up to date", StringComparison.OrdinalIgnoreCase),
                "An unchanged Field Data catalog did not report the fast-path result.");

            // A database from a newer release is not safe for this older application.
            await ExecuteAsync(connection, @"
UPDATE application_metadata SET value = '999'
WHERE name = 'field_catalog_version';");
            bool rejectedNewerCatalog = false;
            try
            {
                await MangosFieldDataSeed.ApplyAsync(connection, updated, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                rejectedNewerCatalog = exception.Message.Contains("newer Studio release", StringComparison.Ordinal);
            }

            Check(rejectedNewerCatalog, "An incompatible newer catalog was incorrectly accepted.");
        }

        // A fatal Field Data exception must leave the desktop closed to regular requests.
        private static void CheckFailedStartupGate()
        {
            DesktopStartupState state = new();
            state.Report(78, "Checking Field Data", "Preparing catalog...");
            state.ReportFieldData(1, 3, "Synchronizing creature_template.Flags...");
            state.MarkFailed(new FieldDataSynchronizationException(
                new InvalidOperationException("Invalid bitmask value in creature_template.Flags.")));
            state.MarkReady();
            state.Report(99, "Incorrectly resumed", "This must not replace the error.");

            DesktopStartupSnapshot snapshot = state.GetSnapshot();
            Check(!snapshot.IsReady && snapshot.HasFailed, "An invalid catalog unlocked the workspace.");
            Check(snapshot.Status == "Field Data update failed", "The failure was not categorized.");
            Check(snapshot.FieldDataCompleted == 1 && snapshot.FieldDataTotal == 3,
                "Startup discarded the last useful Field Data progress snapshot.");
            Check(snapshot.ErrorMessage?.Contains("Invalid bitmask value", StringComparison.Ordinal) == true,
                "The underlying validation error was hidden from recovery diagnostics.");
            Check(snapshot.RecoverySteps.Count > 0, "No safe recovery steps were provided.");
        }

        // Include two tables at baseline and add a third on an ordinary source change.
        private static IReadOnlyList<FieldDefinitionSeed> CreateDefinitions(bool updated)
        {
            FieldOptionSeed[] choices = updated
                ? new[]
                {
                    FieldDataSeedFactory.Option(1, "Updated source label"),
                    FieldDataSeedFactory.Option(2, "Second choice"),
                    FieldDataSeedFactory.Option(3, "New choice")
                }
                : new[]
                {
                    FieldDataSeedFactory.Option(1, "Original label"),
                    FieldDataSeedFactory.Option(2, "Second choice")
                };

            List<FieldDefinitionSeed> definitions = new()
            {
                FieldDataSeedFactory.Select(
                    "builtin.demo_table.mode",
                    "demo_table",
                    "Mode",
                    "A source-controlled selection field.",
                    FieldDataSeedFactory.Variant(
                        "demo-default",
                        updated ? new[] { 0, 1 } : new[] { 0 },
                        "Options available for this expansion.",
                        choices)),
                FieldDataSeedFactory.BitMask(
                    "builtin.creature_template.flags",
                    "creature_template",
                    "Flags",
                    "A source-controlled bitmask field.",
                    FieldDataSeedFactory.Variant(
                        "flags-default",
                        new[] { 0 },
                        "Bitwise OR flag combinations.",
                        FieldDataSeedFactory.Option(1, "First flag")))
            };

            if (updated)
            {
                definitions.Add(FieldDataSeedFactory.Select(
                    "builtin.new_table.status",
                    "new_table",
                    "Status",
                    "A newly registered table.",
                    FieldDataSeedFactory.Variant(
                        "status-default",
                        new[] { 2 },
                        "Another expansion.",
                        FieldDataSeedFactory.Option(7, "Available"))));
            }

            return definitions;
        }

        // Reproduce the five catalog tables and metadata contract without user storage.
        private static Task CreateCatalogSchemaAsync(SqliteConnection connection)
        {
            return ExecuteAsync(connection, @"
CREATE TABLE application_metadata (name TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE field_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    category_key TEXT NOT NULL UNIQUE COLLATE NOCASE,
    name TEXT NOT NULL, description TEXT NOT NULL DEFAULT '',
    icon TEXT NOT NULL DEFAULT 'bi-collection', sort_order INTEGER NOT NULL DEFAULT 0,
    is_enabled INTEGER NOT NULL DEFAULT 1);
CREATE TABLE field_definitions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_key TEXT NOT NULL UNIQUE, category_id INTEGER NULL,
    display_name TEXT NOT NULL DEFAULT '', table_name TEXT NOT NULL COLLATE NOCASE,
    column_name TEXT NOT NULL COLLATE NOCASE, editor_kind TEXT NOT NULL,
    selection_mode TEXT NOT NULL DEFAULT 'Single',
    value_handling TEXT NOT NULL DEFAULT 'Direct',
    preserve_unknown_values INTEGER NOT NULL DEFAULT 1,
    description TEXT NOT NULL DEFAULT '', is_system INTEGER NOT NULL DEFAULT 1,
    is_enabled INTEGER NOT NULL DEFAULT 1, priority INTEGER NOT NULL DEFAULT 0);
CREATE TABLE field_variants (
    id INTEGER PRIMARY KEY AUTOINCREMENT, field_definition_id INTEGER NOT NULL,
    variant_key TEXT NOT NULL, description TEXT NOT NULL DEFAULT '',
    UNIQUE(field_definition_id, variant_key));
CREATE TABLE field_variant_generations (
    field_variant_id INTEGER NOT NULL, generation INTEGER NOT NULL,
    PRIMARY KEY(field_variant_id, generation));
CREATE TABLE field_options (
    id INTEGER PRIMARY KEY AUTOINCREMENT, field_variant_id INTEGER NOT NULL,
    value TEXT NOT NULL, label TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '', sort_order INTEGER NOT NULL DEFAULT 0,
    UNIQUE(field_variant_id, value));");
        }

        // Simple parameter-free queries are sufficient for this isolated test database.
        private static async Task ExecuteAsync(SqliteConnection connection, string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<long> CountAsync(SqliteConnection connection, string table)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        private static async Task<string?> ScalarStringAsync(SqliteConnection connection, string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        private static void Check(bool condition, string failureMessage)
        {
            if (!condition)
            {
                throw new InvalidOperationException(failureMessage);
            }
        }
    }
}
