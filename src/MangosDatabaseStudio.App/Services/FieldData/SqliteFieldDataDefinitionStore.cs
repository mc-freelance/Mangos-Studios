// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Read and update the user-editable SQLite field-definition catalog.

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
    public sealed class SqliteFieldDataDefinitionStore : IFieldDataDefinitionStore
    {
        private readonly ApplicationDatabase _database;

        // Initialize the sqlite field data definition store dependencies and state.
        public SqliteFieldDataDefinitionStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Read stats using the field catalog and expansion metadata.
        public async Task<FieldDataCatalogStats> GetStatsAsync(CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    (SELECT COUNT(*) FROM field_categories),
    (SELECT COUNT(*) FROM field_definitions),
    (SELECT COUNT(*) FROM field_variants),
    (SELECT COUNT(*) FROM field_options);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new FieldDataCatalogStats();
            }

            return new FieldDataCatalogStats
            {
                CategoryCount = checked((int)reader.GetInt64(0)),
                DefinitionCount = checked((int)reader.GetInt64(1)),
                VariantCount = checked((int)reader.GetInt64(2)),
                OptionCount = checked((int)reader.GetInt64(3))
            };
        }

        // Read categories using the field catalog and expansion metadata.
        public async Task<IReadOnlyList<FieldDataCategoryRecord>> GetCategoriesAsync(CancellationToken cancellationToken)
        {
            List<FieldDataCategoryRecord> result = new();
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    c.id,
    c.category_key,
    c.name,
    c.description,
    c.icon,
    c.sort_order,
    c.is_enabled,
    COUNT(DISTINCT d.id) AS definition_count,
    COUNT(DISTINCT v.id) AS variant_count,
    COUNT(DISTINCT o.id) AS option_count
FROM field_categories AS c
LEFT JOIN field_definitions AS d ON d.category_id = c.id
LEFT JOIN field_variants AS v ON v.field_definition_id = d.id
LEFT JOIN field_options AS o ON o.field_variant_id = v.id
GROUP BY c.id, c.category_key, c.name, c.description, c.icon, c.sort_order, c.is_enabled
ORDER BY c.sort_order, c.name COLLATE NOCASE, c.id;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadCategory(reader));
            }

            return result;
        }

        // Read category using the field catalog and expansion metadata.
        public async Task<FieldDataCategoryRecord?> GetCategoryAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    c.id,
    c.category_key,
    c.name,
    c.description,
    c.icon,
    c.sort_order,
    c.is_enabled,
    COUNT(DISTINCT d.id) AS definition_count,
    COUNT(DISTINCT v.id) AS variant_count,
    COUNT(DISTINCT o.id) AS option_count
FROM field_categories AS c
LEFT JOIN field_definitions AS d ON d.category_id = c.id
LEFT JOIN field_variants AS v ON v.field_definition_id = d.id
LEFT JOIN field_options AS o ON o.field_variant_id = v.id
WHERE c.id = $id
GROUP BY c.id, c.category_key, c.name, c.description, c.icon, c.sort_order, c.is_enabled;";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadCategory(reader) : null;
        }

        // Create category using the field catalog and expansion metadata.
        public async Task<long> CreateCategoryAsync(FieldDataCategoryRecord category, CancellationToken cancellationToken)
        {
            NormalizeCategory(category);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO field_categories(category_key, name, description, icon, sort_order, is_enabled)
VALUES ($key, $name, $description, $icon, $sortOrder, $enabled);
SELECT last_insert_rowid();";
            AddCategoryParameters(command, category);
            try
            {
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("A field-data category with that key already exists.", exception);
            }
        }

        // Update category using the field catalog and expansion metadata.
        public async Task UpdateCategoryAsync(FieldDataCategoryRecord category, CancellationToken cancellationToken)
        {
            NormalizeCategory(category);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE field_categories
SET category_key = $key,
    name = $name,
    description = $description,
    icon = $icon,
    sort_order = $sortOrder,
    is_enabled = $enabled
WHERE id = $id;";
            AddCategoryParameters(command, category);
            command.Parameters.AddWithValue("$id", category.Id);
            try
            {
                if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    throw new InvalidOperationException("The field-data category no longer exists.");
                }
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("A field-data category with that key already exists.", exception);
            }
        }

        // Delete category using the field catalog and expansion metadata.
        public async Task DeleteCategoryAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using (SqliteCommand count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM field_definitions WHERE category_id = $id;";
                count.Parameters.AddWithValue("$id", id);
                long definitions = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (definitions > 0)
                {
                    throw new InvalidOperationException("Move or remove the field definitions in this category before deleting it.");
                }
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM field_categories WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Read definitions using the field catalog and expansion metadata.
        public async Task<IReadOnlyList<FieldDataDefinitionRecord>> GetDefinitionsAsync(long? categoryId, string? search, CancellationToken cancellationToken)
        {
            List<FieldDataDefinitionRecord> result = new();
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    d.id,
    d.category_id,
    COALESCE(c.name, ''),
    d.source_key,
    d.display_name,
    d.table_name,
    d.column_name,
    d.selection_mode,
    d.value_handling,
    d.preserve_unknown_values,
    d.description,
    d.is_system,
    d.is_enabled,
    d.priority,
    COUNT(DISTINCT v.id) AS variant_count,
    COUNT(DISTINCT o.id) AS option_count
FROM field_definitions AS d
LEFT JOIN field_categories AS c ON c.id = d.category_id
LEFT JOIN field_variants AS v ON v.field_definition_id = d.id
LEFT JOIN field_options AS o ON o.field_variant_id = v.id
WHERE ($categoryId IS NULL OR d.category_id = $categoryId)
  AND ($search = '' OR
       d.display_name LIKE $like COLLATE NOCASE OR
       d.table_name LIKE $like COLLATE NOCASE OR
       d.column_name LIKE $like COLLATE NOCASE OR
       d.description LIKE $like COLLATE NOCASE OR
       c.name LIKE $like COLLATE NOCASE)
GROUP BY d.id, d.category_id, c.name, d.source_key, d.display_name, d.table_name, d.column_name,
         d.selection_mode, d.value_handling, d.preserve_unknown_values, d.description, d.is_system,
         d.is_enabled, d.priority
ORDER BY COALESCE(c.sort_order, 9999), c.name COLLATE NOCASE, d.table_name COLLATE NOCASE,
         d.column_name COLLATE NOCASE, d.priority DESC, d.id;";
            command.Parameters.AddWithValue("$categoryId", categoryId.HasValue ? (object)categoryId.Value : DBNull.Value);
            string normalizedSearch = (search ?? string.Empty).Trim();
            command.Parameters.AddWithValue("$search", normalizedSearch);
            command.Parameters.AddWithValue("$like", "%" + normalizedSearch + "%");
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadDefinition(reader));
            }

            await reader.DisposeAsync();

            await PopulateDefinitionGenerationsAsync(connection, result, cancellationToken);
            return result;
        }

        // Read definition using the field catalog and expansion metadata.
        public async Task<FieldDataDefinitionRecord?> GetDefinitionAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    d.id,
    d.category_id,
    COALESCE(c.name, ''),
    d.source_key,
    d.display_name,
    d.table_name,
    d.column_name,
    d.selection_mode,
    d.value_handling,
    d.preserve_unknown_values,
    d.description,
    d.is_system,
    d.is_enabled,
    d.priority,
    COUNT(DISTINCT v.id) AS variant_count,
    COUNT(DISTINCT o.id) AS option_count
FROM field_definitions AS d
LEFT JOIN field_categories AS c ON c.id = d.category_id
LEFT JOIN field_variants AS v ON v.field_definition_id = d.id
LEFT JOIN field_options AS o ON o.field_variant_id = v.id
WHERE d.id = $id
GROUP BY d.id, d.category_id, c.name, d.source_key, d.display_name, d.table_name, d.column_name,
         d.selection_mode, d.value_handling, d.preserve_unknown_values, d.description, d.is_system,
         d.is_enabled, d.priority;";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            FieldDataDefinitionRecord definition = ReadDefinition(reader);
            await reader.DisposeAsync();
            List<FieldDataDefinitionRecord> list = new() { definition };
            await PopulateDefinitionGenerationsAsync(connection, list, cancellationToken);
            return definition;
        }

        // Create definition using the field catalog and expansion metadata.
        public async Task<long> CreateDefinitionAsync(FieldDataDefinitionRecord definition, CancellationToken cancellationToken)
        {
            NormalizeDefinition(definition);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await EnsureCategoryExistsAsync(connection, definition.CategoryId, cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO field_definitions(
    source_key, category_id, display_name, table_name, column_name, editor_kind,
    selection_mode, value_handling, preserve_unknown_values, description,
    is_system, is_enabled, priority)
VALUES (
    $sourceKey, $categoryId, $displayName, $tableName, $columnName, $editorKind,
    $selectionMode, $valueHandling, $preserveUnknown, $description,
    0, $enabled, $priority);
SELECT last_insert_rowid();";
            AddDefinitionParameters(command, definition, createSourceKey: true);
            try
            {
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("The field-data definition could not be created because one of its identifiers is already in use.", exception);
            }
        }

        // Update definition using the field catalog and expansion metadata.
        public async Task UpdateDefinitionAsync(FieldDataDefinitionRecord definition, CancellationToken cancellationToken)
        {
            NormalizeDefinition(definition);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await EnsureCategoryExistsAsync(connection, definition.CategoryId, cancellationToken);
            await ValidateExistingOptionsForHandlingAsync(
                connection,
                definition.Id,
                definition.SelectionMode,
                definition.ValueHandling,
                cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE field_definitions
SET category_id = $categoryId,
    display_name = $displayName,
    table_name = $tableName,
    column_name = $columnName,
    editor_kind = $editorKind,
    selection_mode = $selectionMode,
    value_handling = $valueHandling,
    preserve_unknown_values = $preserveUnknown,
    description = $description,
    is_enabled = $enabled,
    priority = $priority
WHERE id = $id;";
            AddDefinitionParameters(command, definition, createSourceKey: false);
            command.Parameters.AddWithValue("$id", definition.Id);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException("The field-data definition no longer exists.");
            }
        }

        // Delete definition using the field catalog and expansion metadata.
        public async Task DeleteDefinitionAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM field_definitions WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Read variants using the field catalog and expansion metadata.
        public async Task<IReadOnlyList<FieldDataVariantRecord>> GetVariantsAsync(long definitionId, CancellationToken cancellationToken)
        {
            List<FieldDataVariantRecord> variants = new();
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, field_definition_id, variant_key, description
FROM field_variants
WHERE field_definition_id = $definitionId
ORDER BY id;";
            command.Parameters.AddWithValue("$definitionId", definitionId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                variants.Add(new FieldDataVariantRecord
                {
                    Id = reader.GetInt64(0),
                    DefinitionId = reader.GetInt64(1),
                    Key = reader.GetString(2),
                    Description = reader.GetString(3)
                });
            }

            await reader.DisposeAsync();
            await PopulateVariantsAsync(connection, variants, cancellationToken);
            return variants;
        }

        // Read variant using the field catalog and expansion metadata.
        public async Task<FieldDataVariantRecord?> GetVariantAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, field_definition_id, variant_key, description
FROM field_variants
WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            FieldDataVariantRecord variant = new()
            {
                Id = reader.GetInt64(0),
                DefinitionId = reader.GetInt64(1),
                Key = reader.GetString(2),
                Description = reader.GetString(3)
            };
            await reader.DisposeAsync();
            List<FieldDataVariantRecord> list = new() { variant };
            await PopulateVariantsAsync(connection, list, cancellationToken);
            return variant;
        }

        // Create variant using the field catalog and expansion metadata.
        public async Task<long> CreateVariantAsync(FieldDataVariantRecord variant, CancellationToken cancellationToken)
        {
            NormalizeVariant(variant);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await ValidateGenerationOverlapAsync(connection, variant.DefinitionId, null, variant.Generations, cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                long id;
                await using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO field_variants(field_definition_id, variant_key, description)
VALUES ($definitionId, $key, $description);
SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("$definitionId", variant.DefinitionId);
                    command.Parameters.AddWithValue("$key", variant.Key);
                    command.Parameters.AddWithValue("$description", variant.Description);
                    object? result = await command.ExecuteScalarAsync(cancellationToken);
                    id = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                }

                await ReplaceGenerationsAsync(connection, transaction, id, variant.Generations, cancellationToken);
                transaction.Commit();
                return id;
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                throw new InvalidOperationException("A data set with that key already exists for this column definition.", exception);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Update variant using the field catalog and expansion metadata.
        public async Task UpdateVariantAsync(FieldDataVariantRecord variant, CancellationToken cancellationToken)
        {
            NormalizeVariant(variant);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await ValidateGenerationOverlapAsync(connection, variant.DefinitionId, variant.Id, variant.Generations, cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                await using (SqliteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
UPDATE field_variants
SET variant_key = $key,
    description = $description
WHERE id = $id AND field_definition_id = $definitionId;";
                    command.Parameters.AddWithValue("$key", variant.Key);
                    command.Parameters.AddWithValue("$description", variant.Description);
                    command.Parameters.AddWithValue("$id", variant.Id);
                    command.Parameters.AddWithValue("$definitionId", variant.DefinitionId);
                    if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                    {
                        throw new InvalidOperationException("The expansion data set no longer exists.");
                    }
                }

                await ReplaceGenerationsAsync(connection, transaction, variant.Id, variant.Generations, cancellationToken);
                transaction.Commit();
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                throw new InvalidOperationException("A data set with that key already exists for this column definition.", exception);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Delete variant using the field catalog and expansion metadata.
        public async Task DeleteVariantAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM field_variants WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create option using the field catalog and expansion metadata.
        public async Task<long> CreateOptionAsync(FieldDataOptionRecord option, CancellationToken cancellationToken)
        {
            NormalizeOption(option);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await ValidateOptionForDefinitionAsync(connection, option.VariantId, option.Value, cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO field_options(field_variant_id, value, label, description, sort_order)
VALUES ($variantId, $value, $label, $description, $sortOrder);
SELECT last_insert_rowid();";
            AddOptionParameters(command, option);
            try
            {
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("That value already exists in this expansion data set.", exception);
            }
        }

        // Update option using the field catalog and expansion metadata.
        public async Task UpdateOptionAsync(FieldDataOptionRecord option, CancellationToken cancellationToken)
        {
            NormalizeOption(option);
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await ValidateOptionForDefinitionAsync(connection, option.VariantId, option.Value, cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE field_options
SET value = $value,
    label = $label,
    description = $description,
    sort_order = $sortOrder
WHERE id = $id AND field_variant_id = $variantId;";
            AddOptionParameters(command, option);
            command.Parameters.AddWithValue("$id", option.Id);
            try
            {
                if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    throw new InvalidOperationException("The field-data entry no longer exists.");
                }
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("That value already exists in this expansion data set.", exception);
            }
        }

        // Delete option using the field catalog and expansion metadata.
        public async Task DeleteOptionAsync(long id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM field_options WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Read category using the field catalog and expansion metadata.
        private static FieldDataCategoryRecord ReadCategory(SqliteDataReader reader)
        {
            return new FieldDataCategoryRecord
            {
                Id = reader.GetInt64(0),
                Key = reader.GetString(1),
                Name = reader.GetString(2),
                Description = reader.GetString(3),
                Icon = reader.GetString(4),
                SortOrder = reader.GetInt32(5),
                IsEnabled = reader.GetInt64(6) != 0,
                DefinitionCount = checked((int)reader.GetInt64(7)),
                VariantCount = checked((int)reader.GetInt64(8)),
                OptionCount = checked((int)reader.GetInt64(9))
            };
        }

        // Read definition using the field catalog and expansion metadata.
        private static FieldDataDefinitionRecord ReadDefinition(SqliteDataReader reader)
        {
            Enum.TryParse(reader.GetString(7), true, out MangosFieldSelectionMode selectionMode);
            Enum.TryParse(reader.GetString(8), true, out MangosFieldValueHandling valueHandling);
            return new FieldDataDefinitionRecord
            {
                Id = reader.GetInt64(0),
                CategoryId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                CategoryName = reader.GetString(2),
                SourceKey = reader.GetString(3),
                DisplayName = reader.GetString(4),
                TableName = reader.GetString(5),
                ColumnName = reader.GetString(6),
                SelectionMode = selectionMode,
                ValueHandling = valueHandling,
                PreserveUnknownValues = reader.GetInt64(9) != 0,
                Description = reader.GetString(10),
                IsSystem = reader.GetInt64(11) != 0,
                IsEnabled = reader.GetInt64(12) != 0,
                Priority = reader.GetInt32(13),
                VariantCount = checked((int)reader.GetInt64(14)),
                OptionCount = checked((int)reader.GetInt64(15))
            };
        }

        // Handle populate definition generations async within the sqlite field data definition store workflow.
        private static async Task PopulateDefinitionGenerationsAsync(
            SqliteConnection connection,
            List<FieldDataDefinitionRecord> definitions,
            CancellationToken cancellationToken)
        {
            if (definitions.Count == 0)
            {
                return;
            }

            Dictionary<long, List<int>> values = definitions.ToDictionary(item => item.Id, _ => new List<int>());
            string placeholders = string.Join(",", definitions.Select((_, index) => "$id" + index.ToString(CultureInfo.InvariantCulture)));
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT v.field_definition_id, g.generation
FROM field_variants AS v
INNER JOIN field_variant_generations AS g ON g.field_variant_id = v.id
WHERE v.field_definition_id IN (" + placeholders + @")
ORDER BY v.field_definition_id, g.generation;";
            for (int index = 0; index < definitions.Count; index++)
            {
                command.Parameters.AddWithValue("$id" + index.ToString(CultureInfo.InvariantCulture), definitions[index].Id);
            }

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long id = reader.GetInt64(0);
                int generation = reader.GetInt32(1);
                if (values.TryGetValue(id, out List<int>? list) && !list.Contains(generation))
                {
                    list.Add(generation);
                }
            }

            foreach (FieldDataDefinitionRecord definition in definitions)
            {
                definition.Generations = values[definition.Id];
            }
        }

        // Handle populate variants async within the sqlite field data definition store workflow.
        private static async Task PopulateVariantsAsync(
            SqliteConnection connection,
            IReadOnlyList<FieldDataVariantRecord> variants,
            CancellationToken cancellationToken)
        {
            foreach (FieldDataVariantRecord variant in variants)
            {
                List<int> generations = new();
                await using (SqliteCommand generationCommand = connection.CreateCommand())
                {
                    generationCommand.CommandText = @"
SELECT generation
FROM field_variant_generations
WHERE field_variant_id = $variantId
ORDER BY generation;";
                    generationCommand.Parameters.AddWithValue("$variantId", variant.Id);
                    await using SqliteDataReader generationReader = await generationCommand.ExecuteReaderAsync(cancellationToken);
                    while (await generationReader.ReadAsync(cancellationToken))
                    {
                        generations.Add(generationReader.GetInt32(0));
                    }
                }

                List<FieldDataOptionRecord> options = new();
                await using (SqliteCommand optionCommand = connection.CreateCommand())
                {
                    optionCommand.CommandText = @"
SELECT id, field_variant_id, value, label, description, sort_order
FROM field_options
WHERE field_variant_id = $variantId
ORDER BY sort_order, id;";
                    optionCommand.Parameters.AddWithValue("$variantId", variant.Id);
                    await using SqliteDataReader optionReader = await optionCommand.ExecuteReaderAsync(cancellationToken);
                    while (await optionReader.ReadAsync(cancellationToken))
                    {
                        options.Add(new FieldDataOptionRecord
                        {
                            Id = optionReader.GetInt64(0),
                            VariantId = optionReader.GetInt64(1),
                            Value = optionReader.GetString(2),
                            Label = optionReader.GetString(3),
                            Description = optionReader.GetString(4),
                            SortOrder = optionReader.GetInt32(5)
                        });
                    }
                }

                variant.Generations = generations;
                variant.Options = options;
            }
        }

        // Validate generation overlap using the field catalog and expansion metadata.
        private static async Task ValidateGenerationOverlapAsync(
            SqliteConnection connection,
            long definitionId,
            long? variantId,
            IReadOnlyList<int> generations,
            CancellationToken cancellationToken)
        {
            if (generations.Count == 0)
            {
                throw new InvalidOperationException("Select at least one expansion for the data set.");
            }

            foreach (int generation in generations)
            {
                if (generation < (int)MangosProjectGeneration.Generation0 ||
                    generation > (int)MangosProjectGeneration.Generation4)
                {
                    throw new InvalidOperationException("The selected expansion is not supported by the field-data catalog.");
                }
            }

            string placeholders = string.Join(",", generations.Select((_, index) => "$generation" + index.ToString(CultureInfo.InvariantCulture)));
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*)
FROM field_variants AS v
INNER JOIN field_variant_generations AS g ON g.field_variant_id = v.id
WHERE v.field_definition_id = $definitionId
  AND ($variantId IS NULL OR v.id <> $variantId)
  AND g.generation IN (" + placeholders + ");";
            command.Parameters.AddWithValue("$definitionId", definitionId);
            command.Parameters.AddWithValue("$variantId", variantId.HasValue ? (object)variantId.Value : DBNull.Value);
            for (int index = 0; index < generations.Count; index++)
            {
                command.Parameters.AddWithValue("$generation" + index.ToString(CultureInfo.InvariantCulture), generations[index]);
            }

            long overlap = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (overlap > 0)
            {
                throw new InvalidOperationException("An expansion can only belong to one data set for the same column definition.");
            }
        }

        // Handle replace generations async within the sqlite field data definition store workflow.
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
                delete.CommandText = "DELETE FROM field_variant_generations WHERE field_variant_id = $variantId;";
                delete.Parameters.AddWithValue("$variantId", variantId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (int generation in generations.Distinct().Order())
            {
                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT INTO field_variant_generations(field_variant_id, generation)
VALUES ($variantId, $generation);";
                insert.Parameters.AddWithValue("$variantId", variantId);
                insert.Parameters.AddWithValue("$generation", generation);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // Validate option for definition using the field catalog and expansion metadata.
        private static async Task ValidateOptionForDefinitionAsync(
            SqliteConnection connection,
            long variantId,
            string value,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT d.selection_mode, d.value_handling
FROM field_variants AS v
INNER JOIN field_definitions AS d ON d.id = v.field_definition_id
WHERE v.id = $variantId;";
            command.Parameters.AddWithValue("$variantId", variantId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The expansion data set no longer exists.");
            }

            Enum.TryParse(reader.GetString(0), true, out MangosFieldSelectionMode selectionMode);
            Enum.TryParse(reader.GetString(1), true, out MangosFieldValueHandling valueHandling);
            ValidateOptionValue(selectionMode, valueHandling, value);
        }

        // Validate existing options for handling using the field catalog and expansion metadata.
        private static async Task ValidateExistingOptionsForHandlingAsync(
            SqliteConnection connection,
            long definitionId,
            MangosFieldSelectionMode selectionMode,
            MangosFieldValueHandling valueHandling,
            CancellationToken cancellationToken)
        {
            if (selectionMode == MangosFieldSelectionMode.Single && valueHandling == MangosFieldValueHandling.Direct)
            {
                return;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT o.value
FROM field_variants AS v
INNER JOIN field_options AS o ON o.field_variant_id = v.id
WHERE v.field_definition_id = $definitionId
ORDER BY v.id, o.sort_order, o.id;";
            command.Parameters.AddWithValue("$definitionId", definitionId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ValidateOptionValue(selectionMode, valueHandling, reader.GetString(0));
            }
        }

        // Validate option value using the field catalog and expansion metadata.
        private static void ValidateOptionValue(
            MangosFieldSelectionMode selectionMode,
            MangosFieldValueHandling valueHandling,
            string value)
        {
            if (selectionMode != MangosFieldSelectionMode.Multiple && valueHandling == MangosFieldValueHandling.Direct)
            {
                return;
            }

            if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong numericValue))
            {
                throw new InvalidOperationException("Multi-select entry values must be unsigned whole numbers.");
            }

            if (valueHandling == MangosFieldValueHandling.BitwiseOr &&
                (numericValue == 0 || (numericValue & (numericValue - 1)) != 0))
            {
                throw new InvalidOperationException("Bitwise OR entries must contain exactly one non-zero bit (1, 2, 4, 8, ...).");
            }
        }

        // Ensure category exists using the field catalog and expansion metadata.
        private static async Task EnsureCategoryExistsAsync(SqliteConnection connection, long? categoryId, CancellationToken cancellationToken)
        {
            if (!categoryId.HasValue)
            {
                return;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM field_categories WHERE id = $id);";
            command.Parameters.AddWithValue("$id", categoryId.Value);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
            {
                throw new InvalidOperationException("The selected field-data category no longer exists.");
            }
        }

        // Normalize category using the field catalog and expansion metadata.
        private static void NormalizeCategory(FieldDataCategoryRecord category)
        {
            category.Key = (category.Key ?? string.Empty).Trim().ToLowerInvariant();
            category.Name = (category.Name ?? string.Empty).Trim();
            category.Description = (category.Description ?? string.Empty).Trim();
            category.Icon = string.IsNullOrWhiteSpace(category.Icon) ? "bi-collection" : category.Icon.Trim();
            if (category.Key.Length == 0 || category.Name.Length == 0)
            {
                throw new InvalidOperationException("Category key and name are required.");
            }
        }

        // Normalize definition using the field catalog and expansion metadata.
        private static void NormalizeDefinition(FieldDataDefinitionRecord definition)
        {
            definition.DisplayName = (definition.DisplayName ?? string.Empty).Trim();
            definition.TableName = (definition.TableName ?? string.Empty).Trim();
            definition.ColumnName = (definition.ColumnName ?? string.Empty).Trim();
            definition.Description = (definition.Description ?? string.Empty).Trim();
            if (definition.TableName.Length == 0 || definition.ColumnName.Length == 0)
            {
                throw new InvalidOperationException("Table and column are required.");
            }

            if (definition.DisplayName.Length == 0)
            {
                definition.DisplayName = definition.ColumnName;
            }

            if (definition.SelectionMode == MangosFieldSelectionMode.Single)
            {
                definition.ValueHandling = MangosFieldValueHandling.Direct;
                definition.PreserveUnknownValues = false;
            }
            else if (definition.ValueHandling == MangosFieldValueHandling.Direct)
            {
                throw new InvalidOperationException("Multiple selection requires Bitwise OR or Additive value handling.");
            }
        }

        // Normalize variant using the field catalog and expansion metadata.
        private static void NormalizeVariant(FieldDataVariantRecord variant)
        {
            variant.Key = (variant.Key ?? string.Empty).Trim();
            variant.Description = (variant.Description ?? string.Empty).Trim();
            variant.Generations = (variant.Generations ?? Array.Empty<int>()).Distinct().Order().ToArray();
            if (variant.DefinitionId <= 0 || variant.Key.Length == 0)
            {
                throw new InvalidOperationException("A definition and data-set key are required.");
            }
        }

        // Normalize option using the field catalog and expansion metadata.
        private static void NormalizeOption(FieldDataOptionRecord option)
        {
            option.Value = (option.Value ?? string.Empty).Trim();
            option.Label = (option.Label ?? string.Empty).Trim();
            option.Description = (option.Description ?? string.Empty).Trim();
            if (option.VariantId <= 0 || option.Label.Length == 0)
            {
                throw new InvalidOperationException("A data set and entry label are required.");
            }
        }

        // Add category parameters using the field catalog and expansion metadata.
        private static void AddCategoryParameters(SqliteCommand command, FieldDataCategoryRecord category)
        {
            command.Parameters.AddWithValue("$key", category.Key);
            command.Parameters.AddWithValue("$name", category.Name);
            command.Parameters.AddWithValue("$description", category.Description);
            command.Parameters.AddWithValue("$icon", category.Icon);
            command.Parameters.AddWithValue("$sortOrder", category.SortOrder);
            command.Parameters.AddWithValue("$enabled", category.IsEnabled ? 1 : 0);
        }

        // Add definition parameters using the field catalog and expansion metadata.
        private static void AddDefinitionParameters(SqliteCommand command, FieldDataDefinitionRecord definition, bool createSourceKey)
        {
            if (createSourceKey)
            {
                command.Parameters.AddWithValue("$sourceKey", "custom." + Guid.NewGuid().ToString("N"));
            }

            command.Parameters.AddWithValue("$categoryId", definition.CategoryId.HasValue ? (object)definition.CategoryId.Value : DBNull.Value);
            command.Parameters.AddWithValue("$displayName", definition.DisplayName);
            command.Parameters.AddWithValue("$tableName", definition.TableName);
            command.Parameters.AddWithValue("$columnName", definition.ColumnName);
            command.Parameters.AddWithValue("$editorKind", ResolveEditorKind(definition).ToString());
            command.Parameters.AddWithValue("$selectionMode", definition.SelectionMode.ToString());
            command.Parameters.AddWithValue("$valueHandling", definition.ValueHandling.ToString());
            command.Parameters.AddWithValue("$preserveUnknown", definition.PreserveUnknownValues ? 1 : 0);
            command.Parameters.AddWithValue("$description", definition.Description);
            command.Parameters.AddWithValue("$enabled", definition.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$priority", definition.Priority);
        }

        // Resolve editor kind using the field catalog and expansion metadata.
        private static MangosFieldEditorKind ResolveEditorKind(FieldDataDefinitionRecord definition)
        {
            if (definition.SelectionMode == MangosFieldSelectionMode.Single)
            {
                return MangosFieldEditorKind.Select;
            }

            return definition.ValueHandling == MangosFieldValueHandling.BitwiseOr
                ? MangosFieldEditorKind.BitMask
                : MangosFieldEditorKind.MultiSelect;
        }

        // Add option parameters using the field catalog and expansion metadata.
        private static void AddOptionParameters(SqliteCommand command, FieldDataOptionRecord option)
        {
            command.Parameters.AddWithValue("$variantId", option.VariantId);
            command.Parameters.AddWithValue("$value", option.Value);
            command.Parameters.AddWithValue("$label", option.Label);
            command.Parameters.AddWithValue("$description", option.Description);
            command.Parameters.AddWithValue("$sortOrder", option.SortOrder);
        }
    }
}
