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
                CheckSourceDiscovery();
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

        // Validate the automatic registry includes both special faction factories.
        private static void CheckSourceDiscovery()
        {
            IReadOnlyList<FieldDefinitionSeed> definitions = FieldDataSeedRegistry.Discover();
            HashSet<string> keys = new(
                definitions.Select(definition => definition.SourceKey),
                StringComparer.OrdinalIgnoreCase);

            Check(keys.Count >= 23, "The registered CreatureTemplate source list is incomplete.");
            Check(keys.Contains("builtin.creature_template.faction_alliance"), "Alliance faction data is missing.");
            Check(keys.Contains("builtin.creature_template.faction_horde"), "Horde faction data is missing.");
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
