// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Schema;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SqliteMangosFieldDataCatalog : IMangosFieldDataCatalog
    {
        private readonly ApplicationDatabase _database;

        // Initialize the sqlite mangos field data catalog dependencies and state.
        public SqliteMangosFieldDataCatalog(ApplicationDatabase database)
        {
            _database = database;
        }

        // Resolve editors in encrypted local application storage.
        public async Task<IReadOnlyDictionary<string, MangosFieldEditorDefinition>> ResolveEditorsAsync(
            MangosProjectGeneration generation,
            string tableName,
            IEnumerable<string> columnNames,
            CancellationToken cancellationToken)
        {
            if (generation == MangosProjectGeneration.Unknown || string.IsNullOrWhiteSpace(tableName))
            {
                return new Dictionary<string, MangosFieldEditorDefinition>(StringComparer.OrdinalIgnoreCase);
            }

            HashSet<string> requestedColumns = new(
                columnNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            if (requestedColumns.Count == 0)
            {
                return new Dictionary<string, MangosFieldEditorDefinition>(StringComparer.OrdinalIgnoreCase);
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    d.id AS definition_id,
    d.column_name,
    d.selection_mode,
    d.value_handling,
    d.preserve_unknown_values,
    CASE
        WHEN length(trim(v.description)) > 0 THEN v.description
        ELSE d.description
    END AS editor_description,
    o.value,
    o.label,
    o.description AS option_description,
    o.sort_order
FROM field_definitions AS d
INNER JOIN field_variants AS v
    ON v.field_definition_id = d.id
INNER JOIN field_variant_generations AS g
    ON g.field_variant_id = v.id
LEFT JOIN field_options AS o
    ON o.field_variant_id = v.id
LEFT JOIN field_categories AS c
    ON c.id = d.category_id
WHERE d.table_name = $tableName COLLATE NOCASE
  AND d.is_enabled = 1
  AND (c.id IS NULL OR c.is_enabled = 1)
  AND g.generation = $generation
ORDER BY
    d.column_name COLLATE NOCASE,
    d.priority DESC,
    d.is_system ASC,
    d.id,
    o.sort_order,
    o.id;";
            command.Parameters.AddWithValue("$tableName", tableName);
            command.Parameters.AddWithValue("$generation", (int)generation);

            Dictionary<string, EditorBuilder> builders = new(StringComparer.OrdinalIgnoreCase);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string columnName = reader.GetString(1);
                if (!requestedColumns.Contains(columnName))
                {
                    continue;
                }

                long definitionId = reader.GetInt64(0);
                if (builders.TryGetValue(columnName, out EditorBuilder? existing) && existing.DefinitionId != definitionId)
                {
                    // Rows are ordered by priority. The first matching definition for a
                    // column wins, allowing an administrator to create an explicit override.
                    continue;
                }

                if (!builders.TryGetValue(columnName, out EditorBuilder? builder))
                {
                    if (!Enum.TryParse(reader.GetString(2), true, out MangosFieldSelectionMode selectionMode))
                    {
                        selectionMode = MangosFieldSelectionMode.Single;
                    }

                    if (!Enum.TryParse(reader.GetString(3), true, out MangosFieldValueHandling valueHandling))
                    {
                        valueHandling = selectionMode == MangosFieldSelectionMode.Multiple
                            ? MangosFieldValueHandling.BitwiseOr
                            : MangosFieldValueHandling.Direct;
                    }

                    MangosFieldEditorKind editorKind = ResolveEditorKind(selectionMode, valueHandling);
                    builder = new EditorBuilder(
                        definitionId,
                        new MangosFieldEditorDefinition
                        {
                            TableName = tableName,
                            ColumnName = columnName,
                            Kind = editorKind,
                            SelectionMode = selectionMode,
                            ValueHandling = valueHandling,
                            PreserveUnknownValues = !reader.IsDBNull(4) && reader.GetInt64(4) != 0,
                            Description = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                        });
                    builders.Add(columnName, builder);
                }

                if (!reader.IsDBNull(6) && !reader.IsDBNull(7))
                {
                    builder.Options.Add(new MangosFieldOption
                    {
                        Value = reader.GetString(6),
                        Label = reader.GetString(7),
                        Description = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                    });
                }
            }

            Dictionary<string, MangosFieldEditorDefinition> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, EditorBuilder> item in builders)
            {
                if (item.Value.Options.Count == 0)
                {
                    continue;
                }

                item.Value.Definition.Options = item.Value.Options;
                result[item.Key] = item.Value.Definition;
            }

            return result;
        }

        // Resolve editor kind in encrypted local application storage.
        private static MangosFieldEditorKind ResolveEditorKind(MangosFieldSelectionMode selectionMode, MangosFieldValueHandling valueHandling)
        {
            if (selectionMode == MangosFieldSelectionMode.Single)
            {
                return MangosFieldEditorKind.Select;
            }

            return valueHandling == MangosFieldValueHandling.BitwiseOr
                ? MangosFieldEditorKind.BitMask
                : MangosFieldEditorKind.MultiSelect;
        }

        private sealed class EditorBuilder
        {
            // Load and validate the or builder editor request.
            public EditorBuilder(long definitionId, MangosFieldEditorDefinition definition)
            {
                DefinitionId = definitionId;
                Definition = definition;
            }

            public long DefinitionId
            {
                get;
            }

            public MangosFieldEditorDefinition Definition
            {
                get;
            }

            public List<MangosFieldOption> Options { get; } = new List<MangosFieldOption>();
        }
    }
}
