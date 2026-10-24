// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.App.Models;

namespace MangosDatabaseStudio.App.Services
{
    public static class WorldEditorPresentation
    {
        private static readonly IReadOnlyList<FieldGroupDefinition> GroupDefinitions = new List<FieldGroupDefinition>
        {
            new("identity", "Identity & General", "bi-fingerprint", "IDs, names, and core values."),
            new("position", "Location & Movement", "bi-geo-alt", "Map, position, spawn, and movement."),
            new("appearance", "Appearance & Display", "bi-person-bounding-box", "Models, scale, equipment, and visuals."),
            new("combat", "Statistics & Combat", "bi-shield-shaded", "Levels, stats, damage, and combat."),
            new("flags", "Flags & Rules", "bi-toggles", "Flags, masks, factions, and rules."),
            new("economy", "Loot & Economy", "bi-coin", "Loot, prices, inventory, and rewards."),
            new("script", "Spells, AI & Scripts", "bi-code-square", "Spells, AI, events, and scripts."),
            new("text", "Text & Localization", "bi-card-text", "Text, language, gossip, and locales."),
            new("advanced", "Advanced Fields", "bi-sliders", "Other available fields.")
        };

        private static readonly string[] PreviewPriority =
        {
            "name", "title", "entry", "guid", "id", "level", "minlevel", "maxlevel", "class", "race",
            "faction", "displayid", "modelid", "map", "zoneid", "area", "type", "quality", "scriptname"
        };

        // Handle humanize within the world editor presentation workflow.
        public static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace('_', ' ').Replace('-', ' ');
            StringBuilder builder = new(normalized.Length + 8);
            char previous = '\0';

            foreach (char character in normalized)
            {
                if (builder.Length > 0 && char.IsUpper(character) && char.IsLower(previous) && previous != ' ')
                {
                    builder.Append(' ');
                }

                builder.Append(character);
                previous = character;
            }

            string[] words = builder.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return string.Join(" ", words.Select(FormatWord));
        }

        // Handle simplify description within the world editor presentation workflow.
        public static string SimplifyDescription(string? value, int maxLength = 110)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = string.Join(
                " ",
                value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            int sentenceEnd = normalized.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceEnd >= 0)
            {
                normalized = normalized[..(sentenceEnd + 1)];
            }

            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            int cut = normalized.LastIndexOf(' ', maxLength - 1);
            if (cut < Math.Max(20, maxLength / 2))
            {
                cut = maxLength - 1;
            }

            return normalized[..cut].TrimEnd(' ', '.', ',', ';', ':') + "…";
        }

        // Build field groups according to the available model or service contract.
        public static IReadOnlyList<WorldEditorFieldGroup> BuildFieldGroups(TableMetadata table)
        {
            Dictionary<string, List<ColumnMetadata>> grouped = GroupDefinitions.ToDictionary(
                definition => definition.Id,
                _ => new List<ColumnMetadata>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (ColumnMetadata column in table.Columns.OrderBy(item => item.OrdinalPosition))
            {
                grouped[ResolveGroup(column)].Add(column);
            }

            return GroupDefinitions
                .Where(definition => grouped[definition.Id].Count > 0)
                .Select(definition => new WorldEditorFieldGroup
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Icon = definition.Icon,
                    Description = definition.Description,
                    Columns = grouped[definition.Id]
                })
                .ToList();
        }

        // Build preview fields according to the available model or service contract.
        public static IReadOnlyList<WorldEditorPreviewField> BuildPreviewFields(TableMetadata table, IDictionary<string, string?> values)
        {
            List<ColumnMetadata> selected = new();

            foreach (ColumnMetadata primaryKey in table.PrimaryKeyColumns)
            {
                AddUnique(selected, primaryKey);
            }

            foreach (string preferredName in PreviewPriority)
            {
                ColumnMetadata? column = table.Columns.FirstOrDefault(item =>
                    string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                {
                    AddUnique(selected, column);
                }
            }

            foreach (ColumnMetadata column in table.Columns)
            {
                if (selected.Count >= 10)
                {
                    break;
                }

                values.TryGetValue(column.Name, out string? candidate);
                if (!string.IsNullOrWhiteSpace(candidate) && !column.DataType.Contains("blob", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(selected, column);
                }
            }

            return selected
                .Take(10)
                .Select(column => new WorldEditorPreviewField
                {
                    ColumnName = column.Name,
                    Label = Humanize(column.Name),
                    Value = values.TryGetValue(column.Name, out string? value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : "—"
                })
                .ToList();
        }

        // Resolve record title according to the available model or service contract.
        public static string ResolveRecordTitle(TableMetadata table, IDictionary<string, string?> values, bool isCreate)
        {
            if (isCreate)
            {
                return "New " + Humanize(table.Name);
            }

            string[] candidates = { "name", "title", "description", "comment" };
            foreach (string candidate in candidates)
            {
                KeyValuePair<string, string?> match = values.FirstOrDefault(item =>
                    string.Equals(item.Key, candidate, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match.Value))
                {
                    return TrimPreview(match.Value, 80);
                }
            }

            string identifier = ResolveRecordIdentifier(table, values);
            return string.IsNullOrWhiteSpace(identifier)
                ? Humanize(table.Name) + " entry"
                : Humanize(table.Name) + " " + identifier;
        }

        // Resolve record identifier according to the available model or service contract.
        public static string ResolveRecordIdentifier(TableMetadata table, IDictionary<string, string?> values)
        {
            List<string> parts = new();
            foreach (ColumnMetadata key in table.PrimaryKeyColumns)
            {
                values.TryGetValue(key.Name, out string? value);
                parts.Add(Humanize(key.Name) + ": " + (string.IsNullOrWhiteSpace(value) ? "—" : value));
            }

            return string.Join(" · ", parts);
        }

        // Resolve category according to the available model or service contract.
        public static MangosTableCategory ResolveCategory(IMangosSchemaCatalog catalog, string tableName)
        {
            return catalog.Categorize(new[] { tableName }).FirstOrDefault()
                ?? new MangosTableCategory
                {
                    Id = "unclassified",
                    Name = "Unclassified / Custom Tables",
                    Icon = "bi-puzzle",
                    Tables = new[] { tableName }
                };
        }

        // Resolve group according to the available model or service contract.
        private static string ResolveGroup(ColumnMetadata column)
        {
            string name = column.Name.ToLowerInvariant();

            if (column.IsPrimaryKey || IsExact(name, "entry", "guid", "id", "name", "title") ||
                ContainsAny(name, "template", "parent", "owner", "creator"))
            {
                return "identity";
            }

            if (ContainsAny(name, "position", "coord", "orientation", "map", "zone", "area", "movement", "wander", "spawn", "path", "waypoint", "distance", "radius"))
            {
                return "position";
            }

            if (ContainsAny(name, "display", "model", "scale", "equipment", "skin", "gender", "visual", "sheath", "material", "sound", "animation", "emote"))
            {
                return "appearance";
            }

            if (ContainsAny(name, "level", "health", "mana", "power", "armor", "resist", "damage", "attack", "combat", "stat", "rank", "speed", "school", "mechanic", "dmg"))
            {
                return "combat";
            }

            if (ContainsAny(name, "flag", "mask", "faction", "immune", "allowable", "type", "family", "class", "race", "required", "requirement", "phase"))
            {
                return "flags";
            }

            if (ContainsAny(name, "loot", "money", "gold", "price", "buy", "sell", "count", "stack", "inventory", "reward", "cost", "vendor", "maxcount"))
            {
                return "economy";
            }

            // Textual fields must be checked before script fields.
            // "description" contains the substring "script", so checking scripts first
            // incorrectly places description columns in the script section.
            if (column.DataType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                ContainsAny(
                    name,
                    "text",
                    "description",
                    "comment",
                    "locale",
                    "language",
                    "gossip",
                    "message",
                    "content"))
            {
                return "text";
            }

            if (ContainsAny(
                name,
                "script",
                "spell",
                "event",
                "condition",
                "ai",
                "action",
                "trigger",
                "proc",
                "smart"))
            {
                return "script";
            }

            if (column.OrdinalPosition <= 12)
            {
                return "identity";
            }

            return "advanced";
        }

        // Handle contains any within the world editor presentation workflow.
        private static bool ContainsAny(string value, params string[] fragments)
        {
            return fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
        }

        // Determine whether exact according to the available model or service contract.
        private static bool IsExact(string value, params string[] candidates)
        {
            return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
        }

        // Add unique according to the available model or service contract.
        private static void AddUnique(List<ColumnMetadata> columns, ColumnMetadata column)
        {
            if (!columns.Any(item => string.Equals(item.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
            {
                columns.Add(column);
            }
        }

        // Format word according to the available model or service contract.
        private static string FormatWord(string word)
        {
            if (word.Length == 0)
            {
                return word;
            }

            string lower = word.ToLowerInvariant();
            if (lower == "id" || lower == "ai" || lower == "npc" || lower == "sql" || lower == "db")
            {
                return lower.ToUpperInvariant();
            }

            if (int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return word;
            }

            return char.ToUpperInvariant(lower[0]) + lower[1..];
        }

        // Handle trim preview within the world editor presentation workflow.
        private static string TrimPreview(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
        }

        private sealed class FieldGroupDefinition
        {
            // Handle field group definition within the world editor presentation workflow.
            public FieldGroupDefinition(string id, string name, string icon, string description)
            {
                Id = id;
                Name = name;
                Icon = icon;
                Description = description;
            }

            public string Id
            {
                get;
            }

            public string Name
            {
                get;
            }

            public string Icon
            {
                get;
            }

            public string Description
            {
                get;
            }
        }
    }
}
