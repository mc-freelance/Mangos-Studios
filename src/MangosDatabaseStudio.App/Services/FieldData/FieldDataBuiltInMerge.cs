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
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal sealed class FieldDataBuiltInMergeResult
    {
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
                "definitions+={0}; definitions~={1}; variants+={2}; variants~={3}; options+={4}; options~={5}; user-additions-preserved={6}",
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
            CancellationToken cancellationToken)
        {
            FieldDataBuiltInMergeResult result = new();

            foreach (FieldDefinitionSeed definition in currentDefinitions)
            {
                ExistingDefinition? existing = await ReadDefinitionAsync(
                    connection, transaction, definition.SourceKey, cancellationToken);

                if (existing == null)
                {
                    await InsertDefinitionAsync(connection, transaction, definition, result, cancellationToken);
                    continue;
                }

                await SynchronizeDefinitionAsync(
                    connection, transaction, existing, definition, result, cancellationToken);
            }

            return result;
        }

        // Handle synchronize definition async within the field data built in merge workflow.
        private static async Task SynchronizeDefinitionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ExistingDefinition existing,
            FieldDefinitionSeed current,
            FieldDataBuiltInMergeResult result,
            CancellationToken cancellationToken)
        {
            bool changed = false;
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
            for (int currentIndex = 0; currentIndex < currentOptions.Count; currentIndex++)
            {
                FieldOptionSeed current = currentOptions[currentIndex];
                ExistingOption? existing = existingOptions.FirstOrDefault(item =>
                    string.Equals(item.Value, current.Value, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    long id = await InsertOptionAsync(
                        connection, transaction, variantId, current, currentIndex, cancellationToken);
                    existingOptions.Add(new ExistingOption
                    {
                        Id = id,
                        VariantId = variantId,
                        Value = current.Value,
                        Label = current.Label,
                        Description = current.Description,
                        SortOrder = currentIndex
                    });
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
SELECT id, table_name, column_name, editor_kind, selection_mode, value_handling,
       preserve_unknown_values, description
FROM field_definitions
WHERE source_key = $sourceKey
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
                Description = reader.GetString(7)
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
    (SELECT id FROM field_categories WHERE category_key = 'world.creature-template' LIMIT 1),
    $displayName, $tableName, $columnName, $editorKind,
    $selectionMode, $valueHandling, 1, $description, 1, 1, 0);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$sourceKey", definition.SourceKey);
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
            int value,
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
