// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.App.Services.FieldData.MapNames;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal sealed class FieldDataBuiltInMergeResult
    {
        public int CategoriesInserted
        {
            get; set;
        }

        public int DefinitionsInserted
        {
            get; set;
        }

        public int DefinitionsUpdated
        {
            get; set;
        }

        public int VariantsInserted
        {
            get; set;
        }

        public int VariantsUpdated
        {
            get; set;
        }

        public int OptionsInserted
        {
            get; set;
        }

        public int OptionsUpdated
        {
            get; set;
        }

        public int UserAdditionsPreserved
        {
            get; set;
        }

        // Format the current value for logging, display, or diagnostics.
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "categories+={0}; definitions+={1}; definitions~={2}; variants+={3}; variants~={4}; options+={5}; options~={6}; user-additions-preserved={7}",
                CategoriesInserted,
                DefinitionsInserted,
                DefinitionsUpdated,
                VariantsInserted,
                VariantsUpdated,
                OptionsInserted,
                OptionsUpdated,
                UserAdditionsPreserved);
        }
    }

    // Synchronizes the SQLite catalog with the current coded Field Data source.
    //
    // Built-in definitions, variants, generation mappings, and matching option rows are
    // source-authoritative: when a row represents a built-in value, its coded data and
    // descriptions are updated to the current source.
    //
    // User additions are never deleted. In particular, option rows whose value is not present
    // in the current source remain in SQLite. A user changing a built-in option's value also
    // leaves that changed row intact while the current built-in value is restored as a separate
    // source row. This guarantees that every current coded value exists without destroying data.
    //
    internal static class FieldDataBuiltInMerge
    {
        // Apply current using the field catalog and expansion metadata.
        public static async Task<FieldDataBuiltInMergeResult> ApplyCurrentAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IReadOnlyList<FieldDefinitionSeed> currentDefinitions,
            CancellationToken cancellationToken,
            Action<int, int, string>? reportProgress = null)
        {
            FieldDataBuiltInMergeResult result = new();
            int completed = 0;

            foreach (FieldDefinitionSeed definition in currentDefinitions)
            {
                string fieldName = definition.TableName + "." + definition.ColumnName;
                reportProgress?.Invoke(completed, currentDefinitions.Count,
                    "Synchronizing " + fieldName + " (categories, variants, and options)...");

                try
                {
                    long categoryId = await EnsureBuiltInCategoryAsync(
                    connection, transaction, definition, result, cancellationToken);

                    ExistingDefinition? existing = await ReadDefinitionAsync(
                        connection, transaction, definition.SourceKey, cancellationToken);

                    if (existing == null)
                    {
                        await InsertDefinitionAsync(connection, transaction, categoryId, definition, result, cancellationToken);
                    }
                    else
                    {
                        await SynchronizeDefinitionAsync(
                            connection, transaction, existing, categoryId, definition, result, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Preserve the specific source field in startup diagnostics.
                    throw new InvalidOperationException(
                        "Could not synchronize Field Data for " + fieldName + ".", exception);
                }

                completed++;
                reportProgress?.Invoke(completed, currentDefinitions.Count,
                    "Completed " + fieldName + ".");
            }

            // Older releases installed Map Names twice. Consolidate the old GameObject
            // dataset into the shared Creature dataset inside the same transaction.
            await ConsolidateLegacyMapNamesAsync(
                connection, transaction, currentDefinitions, cancellationToken, reportProgress);

            return result;
        }

        // A legacy copy should also cause synchronization when the source fingerprint
        // happens to match (for example, after an interrupted manual upgrade).
        public static async Task<bool> HasLegacyMapDefinitionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM field_definitions WHERE source_key = $key);";
            command.Parameters.AddWithValue("$key", MapNamesFieldData.LegacyGameObjectMapSourceKey);
            return Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;
        }

        // Merge the old duplicate Map Names definition without discarding custom map IDs.
        // Matching built-in IDs stay source-authoritative in the shared dataset. Any
        // additional legacy variants or unique option values are carried forward.
        private static async Task ConsolidateLegacyMapNamesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IReadOnlyList<FieldDefinitionSeed> currentDefinitions,
            CancellationToken cancellationToken,
            Action<int, int, string>? reportProgress)
        {
            if (!currentDefinitions.Any(definition => string.Equals(
                definition.SourceKey, MapNamesFieldData.CreatureMapSourceKey,
                StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            long? legacyId = await FindDefinitionIdAsync(
                connection, transaction, MapNamesFieldData.LegacyGameObjectMapSourceKey, cancellationToken);
            if (legacyId == null)
            {
                return;
            }

            long? sharedId = await FindDefinitionIdAsync(
                connection, transaction, MapNamesFieldData.CreatureMapSourceKey, cancellationToken);
            if (sharedId == null)
            {
                throw new InvalidOperationException("Cannot consolidate Map Names: the shared definition is missing.");
            }

            reportProgress?.Invoke(currentDefinitions.Count, currentDefinitions.Count,
                "Consolidating duplicate Map Names data into one shared dataset...");

            try
            {
                // Retain custom variants in full, including their expansion mappings and options.
                // Unique variant keys can be moved without recreating their existing records.
                await ExecuteMapMigrationSqlAsync(connection, transaction, @"
UPDATE field_variants
SET field_definition_id = $sharedId
WHERE field_definition_id = $legacyId
  AND NOT EXISTS (
      SELECT 1 FROM field_variants AS shared
      WHERE shared.field_definition_id = $sharedId
        AND shared.variant_key = field_variants.variant_key COLLATE NOCASE
  );", legacyId.Value, sharedId.Value, cancellationToken);

                // Combine custom map IDs from expansion variants common to both datasets.
                // Duplicate built-in values remain in the canonical source dataset.
                await ExecuteMapMigrationSqlAsync(connection, transaction, @"
INSERT OR IGNORE INTO field_options(field_variant_id, value, label, description, sort_order)
SELECT shared.id, options.value, options.label, options.description, options.sort_order
FROM field_variants AS legacy
INNER JOIN field_variants AS shared
    ON shared.field_definition_id = $sharedId
   AND shared.variant_key = legacy.variant_key COLLATE NOCASE
INNER JOIN field_options AS options ON options.field_variant_id = legacy.id
WHERE legacy.field_definition_id = $legacyId;", legacyId.Value, sharedId.Value, cancellationToken);

                // Preserve any administrator-added generation associations as well.
                await ExecuteMapMigrationSqlAsync(connection, transaction, @"
INSERT OR IGNORE INTO field_variant_generations(field_variant_id, generation)
SELECT shared.id, generations.generation
FROM field_variants AS legacy
INNER JOIN field_variants AS shared
    ON shared.field_definition_id = $sharedId
   AND shared.variant_key = legacy.variant_key COLLATE NOCASE
INNER JOIN field_variant_generations AS generations
    ON generations.field_variant_id = legacy.id
WHERE legacy.field_definition_id = $legacyId;", legacyId.Value, sharedId.Value, cancellationToken);

                // Explicitly clean up remaining duplicate children for databases that do
                // not have foreign key cascading enabled in their SQLite configuration.
                await ExecuteMapMigrationSqlAsync(connection, transaction, @"
DELETE FROM field_options
WHERE field_variant_id IN (
    SELECT id FROM field_variants WHERE field_definition_id = $legacyId
);", legacyId.Value, sharedId.Value, cancellationToken);
                await ExecuteMapMigrationSqlAsync(connection, transaction, @"
DELETE FROM field_variant_generations
WHERE field_variant_id IN (
    SELECT id FROM field_variants WHERE field_definition_id = $legacyId
);", legacyId.Value, sharedId.Value, cancellationToken);
                await ExecuteMapMigrationSqlAsync(connection, transaction,
                    "DELETE FROM field_variants WHERE field_definition_id = $legacyId;",
                    legacyId.Value, sharedId.Value, cancellationToken);
                await ExecuteMapMigrationSqlAsync(connection, transaction,
                    "DELETE FROM field_definitions WHERE id = $legacyId;",
                    legacyId.Value, sharedId.Value, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException("Could not consolidate duplicate Map Names datasets.", exception);
            }
        }

        // Read the ID of a built-in definition without loading its options.
        private static async Task<long?> FindDefinitionIdAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sourceKey,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM field_definitions WHERE source_key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$key", sourceKey);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // Execute a parameterized legacy Map Names migration statement.
        private static async Task ExecuteMapMigrationSqlAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            long legacyId,
            long sharedId,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$legacyId", legacyId);
            command.Parameters.AddWithValue("$sharedId", sharedId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Group ordinary definitions by their source table and shared map definitions
        // under Map Names. Existing category names, visibility and icons are never overwritten.
        private static async Task<long> EnsureBuiltInCategoryAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            FieldDefinitionSeed definition,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            string tableName = definition.TableName;
            bool isMapNames = definition.SourceKey.StartsWith(
                "builtin.map_names.", StringComparison.OrdinalIgnoreCase);
            string categoryKey = isMapNames
                ? "reference.map-names"
                : "world." + tableName.Replace('_', '-').ToLowerInvariant();
            string displayName = isMapNames
                ? "Map Names"
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                    tableName.Replace('_', ' ').ToLowerInvariant());
            bool isCreatureTemplate = string.Equals(
                tableName, "creature_template", StringComparison.OrdinalIgnoreCase);

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT OR IGNORE INTO field_categories(category_key, name, description, icon, sort_order, is_enabled)
VALUES ($key, $name, $description, $icon, $sortOrder, 1);";
            command.Parameters.AddWithValue("$key", categoryKey);
            command.Parameters.AddWithValue("$name", displayName);
            command.Parameters.AddWithValue(
                "$description",
                isMapNames
                    ? "Map.dbc names for world spawn map selectors, grouped by expansion."
                    : "Editor data for " + tableName + " fields.");
            command.Parameters.AddWithValue(
                "$icon",
                isMapNames ? "bi-map" : isCreatureTemplate ? "bi-person-badge" : "bi-collection");
            command.Parameters.AddWithValue(
                "$sortOrder",
                isMapNames ? 20 : isCreatureTemplate ? 10 : 100);
            if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                result.CategoriesInserted++;
            }

            command.CommandText = "SELECT id FROM field_categories WHERE category_key = $key LIMIT 1;";
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // Handle synchronize definition async within the field data built in merge workflow.
        private static async Task SynchronizeDefinitionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ExistingDefinition existing,
            long categoryId,
            FieldDefinitionSeed current,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            bool changed = false;

            // Repair missing or obsolete built-in category assignments while respecting
            // definitions that an administrator moved into a custom category.
            if (existing.CategoryId != categoryId &&
                (existing.CategoryKey == null ||
                    existing.CategoryKey.StartsWith("world.", StringComparison.OrdinalIgnoreCase) ||
                    existing.CategoryKey.StartsWith("reference.", StringComparison.OrdinalIgnoreCase)))
            {
                await UpdateDefinitionIntegerAsync(
                    connection, transaction, existing.Id, "category_id", categoryId, cancellationToken);
                changed = true;
            }

            string editorKind = current.EditorKind.ToString();
            string selectionMode = current.EditorKind == MangosFieldEditorKind.BitMask
                ? nameof(MangosFieldSelectionMode.Multiple)
                : nameof(MangosFieldSelectionMode.Single);
            string valueHandling = current.EditorKind == MangosFieldEditorKind.BitMask
                ? nameof(MangosFieldValueHandling.BitwiseOr)
                : nameof(MangosFieldValueHandling.Direct);

            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "table_name", existing.TableName, current.TableName, cancellationToken);
            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "column_name", existing.ColumnName, current.ColumnName, cancellationToken);
            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "editor_kind", existing.EditorKind, editorKind, cancellationToken);
            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "selection_mode", existing.SelectionMode, selectionMode, cancellationToken);
            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "value_handling", existing.ValueHandling, valueHandling, cancellationToken);
            changed |= await UpdateDefinitionValueAsync(
                connection, transaction, existing.Id, "description", existing.Description, current.Description, cancellationToken);

            if (existing.PreserveUnknownValues != 1)
            {
                await UpdateDefinitionIntegerAsync(
                    connection, transaction, existing.Id, "preserve_unknown_values", 1, cancellationToken);
                changed = true;
            }

            if (changed)
            {
                result.DefinitionsUpdated++;
            }

            List<ExistingVariant> existingVariants = await ReadVariantsAsync(
                connection, transaction, existing.Id, cancellationToken);

            foreach (FieldVariantSeed currentVariant in current.Variants)
            {
                ExistingVariant? existingVariant = existingVariants.FirstOrDefault(item =>
                    string.Equals(item.Key, currentVariant.Key, StringComparison.OrdinalIgnoreCase));

                if (existingVariant == null)
                {
                    await InsertVariantAsync(
                        connection, transaction, existing.Id, currentVariant, result, cancellationToken);
                    continue;
                }

                bool variantChanged = false;
                variantChanged |= await UpdateVariantValueAsync(
                    connection, transaction, existingVariant.Id, "description",
                    existingVariant.Description, currentVariant.Description, cancellationToken);

                if (!SameGenerations(existingVariant.Generations, currentVariant.Generations))
                {
                    await ReplaceGenerationsAsync(
                        connection, transaction, existingVariant.Id, currentVariant.Generations, cancellationToken);
                    existingVariant.Generations = currentVariant.Generations.ToList();
                    variantChanged = true;
                }

                if (variantChanged)
                {
                    result.VariantsUpdated++;
                }

                await SynchronizeOptionsAsync(
                    connection, transaction, existingVariant.Id, existingVariant.Options,
                    currentVariant.Options, result, cancellationToken);
            }

            // Do not delete variants that are not in the current source. They may be user-created
            // additions or legacy data that an administrator still needs to inspect.
            foreach (ExistingVariant existingVariant in existingVariants)
            {
                if (!current.Variants.Any(item =>
                        string.Equals(item.Key, existingVariant.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    result.UserAdditionsPreserved++;
                }
            }
        }

        // Handle synchronize options async within the field data built in merge workflow.
        private static async Task SynchronizeOptionsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long variantId,
            List<ExistingOption> existingOptions,
            IReadOnlyList<FieldOptionSeed> currentOptions,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            // Large lookup variants can contain thousands of values. Resolve existing
            // options once rather than scanning every SQLite row for each source value.
            Dictionary<string, ExistingOption> optionsByValue = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExistingOption option in existingOptions)
            {
                // Preserve case-variant user rows without discarding either from SQLite.
                optionsByValue.TryAdd(option.Value, option);
            }

            for (int currentIndex = 0; currentIndex < currentOptions.Count; currentIndex++)
            {
                FieldOptionSeed current = currentOptions[currentIndex];
                if (!optionsByValue.TryGetValue(current.Value, out ExistingOption? existing))
                {
                    long id = await InsertOptionAsync(
                        connection, transaction, variantId, current, currentIndex, cancellationToken);
                    ExistingOption inserted = new()
                    {
                        Id = id,
                        VariantId = variantId,
                        Value = current.Value,
                        Label = current.Label,
                        Description = current.Description,
                        SortOrder = currentIndex
                    };
                    existingOptions.Add(inserted);
                    optionsByValue.Add(inserted.Value, inserted);
                    result.OptionsInserted++;
                    continue;
                }

                bool changed = false;
                changed |= await UpdateOptionValueAsync(
                    connection, transaction, existing.Id, "label", existing.Label, current.Label, cancellationToken);
                changed |= await UpdateOptionValueAsync(
                    connection, transaction, existing.Id, "description", existing.Description, current.Description, cancellationToken);

                if (existing.SortOrder != currentIndex)
                {
                    await UpdateOptionSortAsync(
                        connection, transaction, existing.Id, currentIndex, cancellationToken);
                    existing.SortOrder = currentIndex;
                    changed = true;
                }

                if (changed)
                {
                    existing.Label = current.Label;
                    existing.Description = current.Description;
                    result.OptionsUpdated++;
                }
            }

            HashSet<string> sourceValues = new(
                currentOptions.Select(item => item.Value), StringComparer.OrdinalIgnoreCase);
            foreach (ExistingOption existing in existingOptions)
            {
                if (!sourceValues.Contains(existing.Value))
                {
                    result.UserAdditionsPreserved++;
                }
            }
        }

        // Read definition using the field catalog and expansion metadata.
        private static async Task<ExistingDefinition?> ReadDefinitionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sourceKey,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT d.id, d.table_name, d.column_name, d.editor_kind, d.selection_mode, d.value_handling,
       d.preserve_unknown_values, d.description, d.category_id, c.category_key
FROM field_definitions AS d
LEFT JOIN field_categories AS c ON c.id = d.category_id
WHERE d.source_key = $sourceKey
LIMIT 1;";
            command.Parameters.AddWithValue("$sourceKey", sourceKey);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ExistingDefinition
            {
                Id = reader.GetInt64(0),
                TableName = reader.GetString(1),
                ColumnName = reader.GetString(2),
                EditorKind = reader.GetString(3),
                SelectionMode = reader.GetString(4),
                ValueHandling = reader.GetString(5),
                PreserveUnknownValues = reader.GetInt32(6),
                Description = reader.GetString(7),
                CategoryId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                CategoryKey = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
        }

        // Read variants using the field catalog and expansion metadata.
        private static async Task<List<ExistingVariant>> ReadVariantsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long definitionId,
            CancellationToken cancellationToken)
        {
            List<ExistingVariant> result = new();
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, field_definition_id, variant_key, description
FROM field_variants
WHERE field_definition_id = $definitionId
ORDER BY id;";
            command.Parameters.AddWithValue("$definitionId", definitionId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new ExistingVariant
                {
                    Id = reader.GetInt64(0),
                    DefinitionId = reader.GetInt64(1),
                    Key = reader.GetString(2),
                    Description = reader.GetString(3),
                    Generations = new List<int>(),
                    Options = new List<ExistingOption>()
                });
            }

            foreach (ExistingVariant variant in result)
            {
                variant.Generations = await ReadGenerationsAsync(
                    connection, transaction, variant.Id, cancellationToken);
                variant.Options = await ReadOptionsAsync(
                    connection, transaction, variant.Id, cancellationToken);
            }

            return result;
        }

        // Read generations using the field catalog and expansion metadata.
        private static async Task<List<int>> ReadGenerationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long variantId,
            CancellationToken cancellationToken)
        {
            List<int> result = new();
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT generation FROM field_variant_generations WHERE field_variant_id = $id ORDER BY generation;";
            command.Parameters.AddWithValue("$id", variantId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(reader.GetInt32(0));
            }

            return result;
        }

        // Read options using the field catalog and expansion metadata.
        private static async Task<List<ExistingOption>> ReadOptionsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long variantId,
            CancellationToken cancellationToken)
        {
            List<ExistingOption> result = new();
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, field_variant_id, value, label, description, sort_order
FROM field_options
WHERE field_variant_id = $id
ORDER BY sort_order, id;";
            command.Parameters.AddWithValue("$id", variantId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new ExistingOption
                {
                    Id = reader.GetInt64(0),
                    VariantId = reader.GetInt64(1),
                    Value = reader.GetString(2),
                    Label = reader.GetString(3),
                    Description = reader.GetString(4),
                    SortOrder = reader.GetInt32(5)
                });
            }

            return result;
        }

        // Insert definition using the field catalog and expansion metadata.
        private static async Task InsertDefinitionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long categoryId,
            FieldDefinitionSeed definition,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            long definitionId;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO field_definitions(
    source_key, category_id, display_name, table_name, column_name, editor_kind,
    selection_mode, value_handling, preserve_unknown_values, description,
    is_system, is_enabled, priority)
VALUES (
    $sourceKey,
    $categoryId,
    $displayName, $tableName, $columnName, $editorKind,
    $selectionMode, $valueHandling, 1, $description, 1, 1, 0);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$sourceKey", definition.SourceKey);
                command.Parameters.AddWithValue("$categoryId", categoryId);
                command.Parameters.AddWithValue("$displayName", definition.ColumnName);
                command.Parameters.AddWithValue("$tableName", definition.TableName);
                command.Parameters.AddWithValue("$columnName", definition.ColumnName);
                command.Parameters.AddWithValue("$editorKind", definition.EditorKind.ToString());
                command.Parameters.AddWithValue(
                    "$selectionMode",
                    definition.EditorKind == MangosFieldEditorKind.BitMask
                        ? nameof(MangosFieldSelectionMode.Multiple)
                        : nameof(MangosFieldSelectionMode.Single));
                command.Parameters.AddWithValue(
                    "$valueHandling",
                    definition.EditorKind == MangosFieldEditorKind.BitMask
                        ? nameof(MangosFieldValueHandling.BitwiseOr)
                        : nameof(MangosFieldValueHandling.Direct));
                command.Parameters.AddWithValue("$description", definition.Description);
                object? value = await command.ExecuteScalarAsync(cancellationToken);
                definitionId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            result.DefinitionsInserted++;
            foreach (FieldVariantSeed variant in definition.Variants)
            {
                await InsertVariantAsync(
                    connection, transaction, definitionId, variant, result, cancellationToken);
            }
        }

        // Insert variant using the field catalog and expansion metadata.
        private static async Task InsertVariantAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long definitionId,
            FieldVariantSeed variant,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            long variantId;
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO field_variants(field_definition_id, variant_key, description)
VALUES ($definitionId, $key, $description);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$definitionId", definitionId);
                command.Parameters.AddWithValue("$key", variant.Key);
                command.Parameters.AddWithValue("$description", variant.Description);
                object? value = await command.ExecuteScalarAsync(cancellationToken);
                variantId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            result.VariantsInserted++;
            foreach (int generation in variant.Generations)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO field_variant_generations(field_variant_id, generation) VALUES ($id, $generation);";
                command.Parameters.AddWithValue("$id", variantId);
                command.Parameters.AddWithValue("$generation", generation);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            for (int index = 0; index < variant.Options.Count; index++)
            {
                await InsertOptionAsync(
                    connection, transaction, variantId, variant.Options[index], index, cancellationToken);
                result.OptionsInserted++;
            }
        }

        // Insert option using the field catalog and expansion metadata.
        private static async Task<long> InsertOptionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long variantId,
            FieldOptionSeed option,
            int sortOrder,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO field_options(field_variant_id, value, label, description, sort_order)
VALUES ($variantId, $value, $label, $description, $sortOrder);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$variantId", variantId);
            command.Parameters.AddWithValue("$value", option.Value);
            command.Parameters.AddWithValue("$label", option.Label);
            command.Parameters.AddWithValue("$description", option.Description);
            command.Parameters.AddWithValue("$sortOrder", sortOrder);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // Update definition value using the field catalog and expansion metadata.
        private static async Task<bool> UpdateDefinitionValueAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            string column,
            string currentValue,
            string newValue,
            CancellationToken cancellationToken)
        {
            if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
            {
                return false;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE field_definitions SET " + column + " = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", newValue);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // Update definition integer using the field catalog and expansion metadata.
        private static async Task UpdateDefinitionIntegerAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            string column,
            long value,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE field_definitions SET " + column + " = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update variant value using the field catalog and expansion metadata.
        private static async Task<bool> UpdateVariantValueAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            string column,
            string currentValue,
            string newValue,
            CancellationToken cancellationToken)
        {
            if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
            {
                return false;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE field_variants SET " + column + " = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", newValue);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // Update option value using the field catalog and expansion metadata.
        private static async Task<bool> UpdateOptionValueAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            string column,
            string currentValue,
            string newValue,
            CancellationToken cancellationToken)
        {
            if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
            {
                return false;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE field_options SET " + column + " = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", newValue);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // Update option sort using the field catalog and expansion metadata.
        private static async Task UpdateOptionSortAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            int value,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE field_options SET sort_order = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Handle replace generations async within the field data built in merge workflow.
        private static async Task ReplaceGenerationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long variantId,
            IReadOnlyList<int> generations,
            CancellationToken cancellationToken)
        {
            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM field_variant_generations WHERE field_variant_id = $id;";
                delete.Parameters.AddWithValue("$id", variantId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (int generation in generations)
            {
                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO field_variant_generations(field_variant_id, generation) VALUES ($id, $generation);";
                insert.Parameters.AddWithValue("$id", variantId);
                insert.Parameters.AddWithValue("$generation", generation);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // Handle same generations within the field data built in merge workflow.
        private static bool SameGenerations(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            return left.SequenceEqual(right);
        }

        private sealed class ExistingDefinition
        {
            public long? CategoryId
            {
                get; set;
            }

            public string? CategoryKey
            {
                get; set;
            }

            public long Id
            {
                get; set;
            }

            public string TableName { get; set; } = string.Empty;
            public string ColumnName { get; set; } = string.Empty;
            public string EditorKind { get; set; } = string.Empty;
            public string SelectionMode { get; set; } = string.Empty;
            public string ValueHandling { get; set; } = string.Empty;
            public int PreserveUnknownValues
            {
                get; set;
            }

            public string Description { get; set; } = string.Empty;
        }

        private sealed class ExistingVariant
        {
            public long Id
            {
                get; set;
            }

            public long DefinitionId
            {
                get; set;
            }

            public string Key { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<int> Generations { get; set; } = new List<int>();
            public List<ExistingOption> Options { get; set; } = new List<ExistingOption>();
        }

        private sealed class ExistingOption
        {
            public long Id
            {
                get; set;
            }

            public long VariantId
            {
                get; set;
            }

            public string Value { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int SortOrder
            {
                get; set;
            }
        }
    }
}
