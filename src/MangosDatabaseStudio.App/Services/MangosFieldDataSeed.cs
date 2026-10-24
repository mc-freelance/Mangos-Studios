// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.App.Services.FieldData;
using MangosDatabaseStudio.App.Services.FieldData.CreatureTemplate;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    internal static class MangosFieldDataSeed
    {
        private const string CatalogVersionMetadataKey = "field_catalog_version";
        private const int CurrentCatalogVersion = 13;

        // Apply  according to the available model or service contract.
        public static async Task ApplyAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            int installedVersion = await GetInstalledVersionAsync(connection, cancellationToken);
            if (installedVersion >= CurrentCatalogVersion)
            {
                return;
            }

            IReadOnlyList<FieldDefinitionSeed> definitions = CreatureTemplateFieldData.Definitions;
            ValidateDefinitions(definitions);

            bool hasDefinitions = await HasDefinitionsAsync(connection, cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                if (!hasDefinitions && installedVersion <= 0)
                {
                    // A genuinely new catalog receives the latest audited built-in baseline.
                    // An initialized catalog whose administrator deleted every definition has a
                    // catalog version already and must remain empty.
                    foreach (FieldDefinitionSeed definition in definitions)
                    {
                        await InsertDefinitionAsync(connection, transaction, definition, cancellationToken);
                    }
                }
                else if (hasDefinitions && installedVersion < CurrentCatalogVersion)
                {
                    // The field-data source is intentionally consolidated: each field's Create()
                    // method is the single built-in definition for every supported generation.
                    // Synchronize existing SQLite catalogs with the current coded source.
                    // Built-in rows are source-authoritative; user-created rows are retained.
                    FieldDataBuiltInMergeResult merge = await FieldDataBuiltInMerge.ApplyCurrentAsync(
                        connection,
                        transaction,
                        definitions,
                        cancellationToken);

                    await SetMetadataAsync(
                        connection,
                        transaction,
                        "field_catalog_last_update_summary",
                        merge.ToString(),
                        cancellationToken);
                }

                await SetMetadataAsync(
                    connection,
                    transaction,
                    CatalogVersionMetadataKey,
                    CurrentCatalogVersion.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Validate definitions according to the available model or service contract.
        private static void ValidateDefinitions(IReadOnlyList<FieldDefinitionSeed> definitions)
        {
            HashSet<string> sourceKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> fields = new(StringComparer.OrdinalIgnoreCase);

            foreach (FieldDefinitionSeed definition in definitions)
            {
                if (!sourceKeys.Add(definition.SourceKey))
                {
                    throw new InvalidOperationException(
                        "Duplicate field catalog source key: " + definition.SourceKey);
                }

                string fieldKey = definition.TableName + "." + definition.ColumnName;
                if (!fields.Add(fieldKey))
                {
                    throw new InvalidOperationException(
                        "Duplicate built-in field catalog definition: " + fieldKey);
                }

                HashSet<int> mappedGenerations = new();
                foreach (FieldVariantSeed variant in definition.Variants)
                {
                    if (variant.Generations.Count == 0)
                    {
                        throw new InvalidOperationException(
                            fieldKey + " contains a variant without a generation mapping.");
                    }

                    foreach (int generation in variant.Generations)
                    {
                        if (generation < (int)MangosProjectGeneration.Generation0 ||
                            generation > (int)MangosProjectGeneration.Generation4)
                        {
                            throw new InvalidOperationException(
                                fieldKey + " contains unsupported generation " +
                                generation.ToString(CultureInfo.InvariantCulture) + ".");
                        }

                        if (!mappedGenerations.Add(generation))
                        {
                            throw new InvalidOperationException(
                                fieldKey + " has overlapping built-in variants for generation " +
                                generation.ToString(CultureInfo.InvariantCulture) + ".");
                        }
                    }

                    HashSet<string> optionValues = new(StringComparer.OrdinalIgnoreCase);
                    foreach (FieldOptionSeed option in variant.Options)
                    {
                        if (!optionValues.Add(option.Value))
                        {
                            throw new InvalidOperationException(
                                fieldKey + " contains duplicate option value " + option.Value + ".");
                        }

                        if (definition.EditorKind == MangosFieldEditorKind.BitMask)
                        {
                            if (!ulong.TryParse(
                                    option.Value,
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out ulong bitValue) ||
                                bitValue == 0 ||
                                (bitValue & (bitValue - 1)) != 0)
                            {
                                throw new InvalidOperationException(
                                    fieldKey + " contains invalid bitmask option " + option.Value +
                                    ". Bitmask options must contain one non-zero bit.");
                            }
                        }
                    }
                }
            }
        }

        // Read installed version according to the available model or service contract.
        private static async Task<int> GetInstalledVersionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT value FROM application_metadata WHERE name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", CatalogVersionMetadataKey);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return int.TryParse(
                Convert.ToString(result, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int version)
                ? version
                : 0;
        }

        // Determine whether definitions according to the available model or service contract.
        private static async Task<bool> HasDefinitionsAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM field_definitions LIMIT 1);";
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
        }

        // Set metadata according to the available model or service contract.
        private static async Task SetMetadataAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string name,
            string value,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO application_metadata(name, value)
VALUES ($name, $value)
ON CONFLICT(name) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert definition according to the available model or service contract.
        private static async Task InsertDefinitionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            FieldDefinitionSeed definition,
            CancellationToken cancellationToken)
        {
            long definitionId;
            await using (SqliteCommand insertDefinition = connection.CreateCommand())
            {
                insertDefinition.Transaction = transaction;
                insertDefinition.CommandText = @"
INSERT INTO field_definitions(
    source_key,
    category_id,
    display_name,
    table_name,
    column_name,
    editor_kind,
    selection_mode,
    value_handling,
    preserve_unknown_values,
    description,
    is_system,
    is_enabled,
    priority)
VALUES (
    $sourceKey,
    (SELECT id FROM field_categories WHERE category_key = 'world.creature-template' LIMIT 1),
    $columnName,
    $tableName,
    $columnName,
    $editorKind,
    $selectionMode,
    $valueHandling,
    1,
    $description,
    1,
    1,
    0);
SELECT last_insert_rowid();";
                insertDefinition.Parameters.AddWithValue("$sourceKey", definition.SourceKey);
                insertDefinition.Parameters.AddWithValue("$tableName", definition.TableName);
                insertDefinition.Parameters.AddWithValue("$columnName", definition.ColumnName);
                insertDefinition.Parameters.AddWithValue(
                    "$editorKind",
                    definition.EditorKind.ToString());
                bool isMultiple = definition.EditorKind == MangosFieldEditorKind.BitMask;
                insertDefinition.Parameters.AddWithValue(
                    "$selectionMode",
                    isMultiple ? nameof(MangosFieldSelectionMode.Multiple) : nameof(MangosFieldSelectionMode.Single));
                insertDefinition.Parameters.AddWithValue(
                    "$valueHandling",
                    isMultiple ? nameof(MangosFieldValueHandling.BitwiseOr) : nameof(MangosFieldValueHandling.Direct));
                insertDefinition.Parameters.AddWithValue("$description", definition.Description);
                object? result = await insertDefinition.ExecuteScalarAsync(cancellationToken);
                definitionId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }

            foreach (FieldVariantSeed variant in definition.Variants)
            {
                long variantId;
                await using (SqliteCommand insertVariant = connection.CreateCommand())
                {
                    insertVariant.Transaction = transaction;
                    insertVariant.CommandText = @"
INSERT INTO field_variants(field_definition_id, variant_key, description)
VALUES ($definitionId, $variantKey, $description);
SELECT last_insert_rowid();";
                    insertVariant.Parameters.AddWithValue("$definitionId", definitionId);
                    insertVariant.Parameters.AddWithValue("$variantKey", variant.Key);
                    insertVariant.Parameters.AddWithValue("$description", variant.Description);
                    object? result = await insertVariant.ExecuteScalarAsync(cancellationToken);
                    variantId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                }

                foreach (int generation in variant.Generations)
                {
                    await using SqliteCommand generationCommand = connection.CreateCommand();
                    generationCommand.Transaction = transaction;
                    generationCommand.CommandText = @"
INSERT INTO field_variant_generations(field_variant_id, generation)
VALUES ($variantId, $generation);";
                    generationCommand.Parameters.AddWithValue("$variantId", variantId);
                    generationCommand.Parameters.AddWithValue("$generation", generation);
                    await generationCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                for (int index = 0; index < variant.Options.Count; index++)
                {
                    FieldOptionSeed option = variant.Options[index];
                    await using SqliteCommand optionCommand = connection.CreateCommand();
                    optionCommand.Transaction = transaction;
                    optionCommand.CommandText = @"
INSERT INTO field_options(
    field_variant_id,
    value,
    label,
    description,
    sort_order)
VALUES (
    $variantId,
    $value,
    $label,
    $description,
    $sortOrder);";
                    optionCommand.Parameters.AddWithValue("$variantId", variantId);
                    optionCommand.Parameters.AddWithValue("$value", option.Value);
                    optionCommand.Parameters.AddWithValue("$label", option.Label);
                    optionCommand.Parameters.AddWithValue("$description", option.Description);
                    optionCommand.Parameters.AddWithValue("$sortOrder", index);
                    await optionCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }
}
