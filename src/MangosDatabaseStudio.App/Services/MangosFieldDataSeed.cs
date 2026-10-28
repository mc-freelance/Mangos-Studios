// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.App.Services.FieldData;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    internal static class MangosFieldDataSeed
    {
        private const string CatalogVersionMetadataKey = "field_catalog_version";
        private const string CatalogFingerprintMetadataKey = "field_catalog_source_sha256";
        private const int CurrentCatalogVersion = 15;

        // Synchronize all discovered C# FieldData sources into the encrypted SQLite catalog.
        public static Task ApplyAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken,
            Action<int, int, string>? reportProgress = null)
        {
            return ApplyAsync(connection, FieldDataSeedRegistry.Discover(), cancellationToken, reportProgress);
        }

        // A source fingerprint makes synchronization automatic after a code change without
        // issuing thousands of SQL queries on every application startup. The overload also
        // permits focused integration tests with small, multi-table field-data catalogs.
        internal static async Task ApplyAsync(
            SqliteConnection connection,
            IReadOnlyList<FieldDefinitionSeed> definitions,
            CancellationToken cancellationToken,
            Action<int, int, string>? reportProgress = null)
        {
            // Report validation separately: an invalid source must fail before SQLite changes.
            reportProgress?.Invoke(0, definitions.Count, "Validating Field Data source definitions...");
            ValidateDefinitions(definitions);

            string fingerprint = ComputeFingerprint(definitions);
            int installedVersion = await GetInstalledVersionAsync(connection, cancellationToken);
            string? installedFingerprint = await GetMetadataAsync(
                connection, CatalogFingerprintMetadataKey, cancellationToken);

            // Older application binaries must not open a catalog from a newer release.
            // Opening a mismatched catalog could silently produce incorrect editor options.
            if (installedVersion > CurrentCatalogVersion)
            {
                throw new InvalidOperationException(
                    "The Field Data catalog was created by a newer Studio release (catalog version " +
                    installedVersion.ToString(CultureInfo.InvariantCulture) +
                    "). Install a compatible Studio version or restore a matching database backup; " +
                    "automatic catalog downgrades are not supported.");
            }

            // The fingerprint only tracks C# changes, not deleted SQLite rows. Always
            // check that each required built-in category and definition still exists.
            // This check is intentionally cheap and does not read every option.
            bool missingBuiltIns = await HasMissingBuiltInsAsync(
                connection, definitions, cancellationToken);
            bool legacyMapDefinition = definitions.Any(definition => string.Equals(
                definition.SourceKey, FieldData.MapNames.MapNamesFieldData.CreatureMapSourceKey,
                StringComparison.OrdinalIgnoreCase)) &&
                await FieldDataBuiltInMerge.HasLegacyMapDefinitionAsync(
                    connection, cancellationToken);

            if (installedVersion == CurrentCatalogVersion &&
                string.Equals(installedFingerprint, fingerprint, StringComparison.Ordinal) &&
                !missingBuiltIns && !legacyMapDefinition)
            {
                reportProgress?.Invoke(definitions.Count, definitions.Count,
                    "Field Data is already up to date; no changes were needed.");
                return;
            }

            await using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                // Restore built-in data even after categories and definitions were deleted.
                // The merge is idempotent and preserves independent administrator additions.
                FieldDataBuiltInMergeResult merge = await FieldDataBuiltInMerge.ApplyCurrentAsync(
                    connection,
                    transaction,
                    definitions,
                    cancellationToken,
                    reportProgress);

                await SetMetadataAsync(
                    connection,
                    transaction,
                    "field_catalog_last_update_summary",
                    merge.ToString(),
                    cancellationToken);

                await SetMetadataAsync(
                    connection,
                    transaction,
                    CatalogVersionMetadataKey,
                    CurrentCatalogVersion.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
                await SetMetadataAsync(
                    connection,
                    transaction,
                    CatalogFingerprintMetadataKey,
                    fingerprint,
                    cancellationToken);

                transaction.Commit();
            }
            catch
            {
                // Never write a new source fingerprint if any catalog row failed.
                transaction.Rollback();
                throw;
            }

            reportProgress?.Invoke(definitions.Count, definitions.Count,
                "Field Data catalog synchronized and saved successfully.");
        }

        // Hash every source field and its ordered variants/options. A change to any built-in
        // dropdown value, bitmask flag, description or generation triggers a database merge.
        private static string ComputeFingerprint(IReadOnlyList<FieldDefinitionSeed> definitions)
        {
            string json = JsonSerializer.Serialize(definitions
                .OrderBy(definition => definition.SourceKey, StringComparer.Ordinal)
                .ToArray());
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash);
        }

        // Reject duplicate built-in definitions, overlapping expansion mappings, and
        // invalid bitmask values before any SQLite transaction can modify the catalog.
        private static void ValidateDefinitions(IReadOnlyList<FieldDefinitionSeed> definitions)
        {
            HashSet<string> sourceKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> fields = new(StringComparer.OrdinalIgnoreCase);

            foreach (FieldDefinitionSeed definition in definitions)
            {
                if (string.IsNullOrWhiteSpace(definition.SourceKey) ||
                    string.IsNullOrWhiteSpace(definition.TableName) ||
                    string.IsNullOrWhiteSpace(definition.ColumnName))
                {
                    throw new InvalidOperationException(
                        "Built-in FieldData source keys, table names and column names cannot be empty.");
                }

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
                HashSet<string> variantKeys = new(StringComparer.OrdinalIgnoreCase);
                foreach (FieldVariantSeed variant in definition.Variants)
                {
                    if (string.IsNullOrWhiteSpace(variant.Key) || !variantKeys.Add(variant.Key))
                    {
                        throw new InvalidOperationException(
                            fieldKey + " contains an empty or duplicate variant key: " + variant.Key);
                    }

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

        // Read the catalog version independently of the C# source fingerprint.
        private static async Task<int> GetInstalledVersionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            string? value = await GetMetadataAsync(
                connection, CatalogVersionMetadataKey, cancellationToken);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                ? version
                : 0;
        }

        // Load a metadata value from the existing application database.
        private static async Task<string?> GetMetadataAsync(
            SqliteConnection connection,
            string name,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT value FROM application_metadata WHERE name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", name);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToString(result, CultureInfo.InvariantCulture);
        }

        // Detect deleted built-in definitions or categories independently of the source hash.
        // Only category keys and definition source keys are inspected, keeping the normal
        // startup path fast even for catalogs with thousands of options.
        private static async Task<bool> HasMissingBuiltInsAsync(
            SqliteConnection connection,
            IReadOnlyList<FieldDefinitionSeed> definitions,
            CancellationToken cancellationToken)
        {
            HashSet<string> categoryKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> definitionKeys = new(StringComparer.OrdinalIgnoreCase);

            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT category_key FROM field_categories;";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    categoryKeys.Add(reader.GetString(0));
                }
            }

            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT source_key FROM field_definitions;";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    definitionKeys.Add(reader.GetString(0));
                }
            }

            foreach (FieldDefinitionSeed definition in definitions)
            {
                string categoryKey = definition.SourceKey.StartsWith(
                    "builtin.map_names.", StringComparison.OrdinalIgnoreCase)
                    ? "reference.map-names"
                    : "world." + definition.TableName.Replace('_', '-').ToLowerInvariant();

                if (!definitionKeys.Contains(definition.SourceKey) ||
                    !categoryKeys.Contains(categoryKey))
                {
                    return true;
                }
            }

            return false;
        }

        // Persist migration state and the source fingerprint atomically with the merge.
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

    }
}
