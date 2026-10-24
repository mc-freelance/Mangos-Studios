// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Expose and normalize MaNGOS table definitions across expansions.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Schema
{
    public sealed class MangosSchemaCatalog : IMangosSchemaCatalog
    {
        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] FingerprintTables =
        {
            "creature_template",
            "creature_template_classlevelstats",
            "creature_template_spells"
        };

        private readonly CatalogManifest _manifest;

        // Initialize the mangos schema catalog dependencies and state.
        public MangosSchemaCatalog()
        {
            _manifest = LoadManifest<CatalogManifest>(
                "MangosDatabaseStudio.Schema.mangos-tables.json",
                "The Mangos table manifest could not be loaded.");
        }

        public string Name
        {
            get
            {
                return "Adaptive getMaNGOS";
            }
        }

        public string Version
        {
            get
            {
                return "Adaptive";
            }
        }

        public IReadOnlyList<string> StructuralFingerprintTables
        {
            get
            {
                return FingerprintTables;
            }
        }

        // Identify a supported MaNGOS schema or expansion from table metadata.
        public MangosSchemaProfile Detect(IEnumerable<TableMetadata> tables)
        {
            List<TableMetadata> metadata = tables.ToList();
            HashSet<string> available = new(
                metadata.Select(table => table.Name),
                StringComparer.OrdinalIgnoreCase);

            TableMetadata? classLevelStats = metadata.FirstOrDefault(
                table => string.Equals(
                    table.Name,
                    "creature_template_classlevelstats",
                    StringComparison.OrdinalIgnoreCase));

            if (classLevelStats?.Columns.Count > 0)
            {
                MangosProjectGeneration? structuralGeneration = DetectFromExpansionColumns(classLevelStats);
                if (structuralGeneration.HasValue)
                {
                    int expansion = (int)structuralGeneration.Value;
                    return Profile(
                        structuralGeneration.Value,
                        "Detected from the live world-schema expansion structure (Exp0-Exp" + expansion + ").");
                }
            }

            // The class-level stats structure is the authoritative fingerprint for the
            // standard schemas. These secondary checks keep custom/partial databases
            // usable when that table is absent or has not been hydrated.
            TableMetadata? creatureTemplate = FindTable(metadata, "creature_template");
            TableMetadata? creatureSpells = FindTable(metadata, "creature_template_spells");

            if (HasGenerationFourSignature(available))
            {
                return Profile(
                    MangosProjectGeneration.Generation4,
                    "Detected from Mists of Pandaria world-schema table signatures.");
            }

            if (HasColumn(creatureSpells, "spell10"))
            {
                return Profile(
                    MangosProjectGeneration.Generation3,
                    "Detected from the live world-schema creature spell structure.");
            }

            if (HasColumn(creatureSpells, "spell8"))
            {
                return Profile(
                    MangosProjectGeneration.Generation2,
                    "Detected from the live world-schema creature spell structure.");
            }

            if (HasColumn(creatureTemplate, "Expansion"))
            {
                return Profile(
                    MangosProjectGeneration.Generation1,
                    "Detected from the live world-schema creature structure.");
            }

            return DetectFromTableNames(available);
        }

        // Assign a discovered table to a logical schema group.
        public IReadOnlyList<MangosTableCategory> Categorize(IEnumerable<string> tables)
        {
            HashSet<string> available = new(tables, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, CategoryAccumulator> categories = _manifest.Categories.ToDictionary(
                item => item.Id,
                item => new CategoryAccumulator(item.Id, item.Name, item.Icon),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> assigned = new(StringComparer.OrdinalIgnoreCase);

            foreach (CatalogCategory category in _manifest.Categories)
            {
                CategoryAccumulator target = categories[category.Id];
                foreach (string table in category.Tables.Where(available.Contains))
                {
                    target.Tables.Add(table);
                    assigned.Add(table);
                }
            }

            foreach (string table in available.Where(item => !assigned.Contains(item)))
            {
                string? categoryId = ResolveDynamicCategory(table);
                if (categoryId != null && categories.TryGetValue(categoryId, out CategoryAccumulator? target))
                {
                    target.Tables.Add(table);
                    assigned.Add(table);
                }
            }

            List<MangosTableCategory> result = new();
            foreach (CatalogCategory definition in _manifest.Categories)
            {
                CategoryAccumulator category = categories[definition.Id];
                if (category.Tables.Count == 0)
                {
                    continue;
                }

                result.Add(category.ToModel());
            }

            List<string> unknown = available
                .Where(table => !assigned.Contains(table))
                .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unknown.Count > 0)
            {
                result.Add(new MangosTableCategory
                {
                    Id = "unclassified",
                    Name = "Unclassified / Custom Tables",
                    Icon = "bi-puzzle",
                    Tables = unknown
                });
            }

            return result;
        }

        private static TManifest LoadManifest<TManifest>(string resourceName, string errorMessage)
        {
            using Stream stream = typeof(MangosSchemaCatalog).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("The embedded catalog resource was not found: " + resourceName);
            using StreamReader reader = new(stream);
            string json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<TManifest>(json, ManifestJsonOptions)
                ?? throw new InvalidOperationException(errorMessage);
        }

        // Load or update the current user profile from protected application state.
        private static MangosSchemaProfile Profile(MangosProjectGeneration generation, string description)
        {
            return new MangosSchemaProfile
            {
                Name = MangosProjectNames.GetName(generation),
                Version = MangosProjectNames.GetSchemaVersion(generation),
                Description = description,
                Generation = generation,
                IsRecognized = true
            };
        }

        // Detect from table names according to the available model or service contract.
        private static MangosSchemaProfile DetectFromTableNames(ISet<string> available)
        {
            if (HasGenerationFourSignature(available))
            {
                return Profile(
                    MangosProjectGeneration.Generation4,
                    "Detected from Mists of Pandaria world-schema table signatures.");
            }

            if (HasBaseWorldSignature(available))
            {
                return new MangosSchemaProfile
                {
                    Name = MangosProjectNames.GetName(MangosProjectGeneration.Unknown),
                    Version = MangosProjectNames.GetSchemaVersion(MangosProjectGeneration.Unknown),
                    Description = "A Mangos-compatible world schema was found, but its generation needs structural metadata for a safe match.",
                    Generation = MangosProjectGeneration.Unknown,
                    IsRecognized = false
                };
            }

            return new MangosSchemaProfile
            {
                Name = MangosProjectNames.GetName(MangosProjectGeneration.Unknown),
                Version = MangosProjectNames.GetSchemaVersion(MangosProjectGeneration.Unknown),
                Description = "No fixed project signature was found; the editor is using live schema metadata.",
                Generation = MangosProjectGeneration.Unknown,
                IsRecognized = false
            };
        }

        // Detect from expansion columns according to the available model or service contract.
        private static MangosProjectGeneration? DetectFromExpansionColumns(TableMetadata classLevelStats)
        {
            HashSet<string> columns = new(
                classLevelStats.Columns.Select(column => column.Name),
                StringComparer.OrdinalIgnoreCase);

            int highestExpansion = -1;
            for (int expansion = 0; expansion <= 4; expansion++)
            {
                bool hasHealth = columns.Contains("BaseHealthExp" + expansion);
                bool hasDamage = columns.Contains("BaseDamageExp" + expansion);

                if (hasHealth != hasDamage)
                {
                    // An incomplete pair is not a standard project fingerprint.
                    return null;
                }

                if (hasHealth)
                {
                    if (expansion != highestExpansion + 1)
                    {
                        // Standard project schemas expose a continuous Exp0..ExpN series.
                        return null;
                    }

                    highestExpansion = expansion;
                }
            }

            if (highestExpansion < 0)
            {
                return null;
            }

            // Do not classify a future schema as generation four merely because it
            // still contains the Exp0..Exp4 compatibility columns.
            if (columns.Any(column =>
                (TryGetExpansionSuffix(column, "BaseHealthExp", out int healthExpansion) && healthExpansion > 4) ||
                (TryGetExpansionSuffix(column, "BaseDamageExp", out int damageExpansion) && damageExpansion > 4)))
            {
                return null;
            }

            return (MangosProjectGeneration)highestExpansion;
        }

        // Attempt to get expansion suffix according to the available model or service contract.
        private static bool TryGetExpansionSuffix(string column, string prefix, out int value)
        {
            value = -1;
            if (!column.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(column.AsSpan(prefix.Length), out value);
        }

        // Locate table according to the available model or service contract.
        private static TableMetadata? FindTable(IEnumerable<TableMetadata> tables, string name)
        {
            return tables.FirstOrDefault(
                table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // Determine whether column according to the available model or service contract.
        private static bool HasColumn(TableMetadata? table, string columnName)
        {
            return table?.Columns.Any(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)) is true;
        }

        // Determine whether base world signature according to the available model or service contract.
        private static bool HasBaseWorldSignature(ISet<string> available)
        {
            return available.Contains("creature_template") &&
                   available.Contains("item_template") &&
                   available.Contains("quest_template");
        }

        // Determine whether generation four signature according to the available model or service contract.
        private static bool HasGenerationFourSignature(ISet<string> available)
        {
            // Secondary only. The primary detector uses the Exp0..Exp4 structural
            // fingerprint from creature_template_classlevelstats. Requiring multiple
            // MoP-era markers avoids identifying a customized earlier database from a
            // single backported table.
            string[] markers =
            {
                "battle_pet_breed_quality",
                "battle_pet_breed_state",
                "battle_pet_species_state",
                "battle_pet_spawn",
                "challenge_mode",
                "item_upgrade",
                "quest_package_item",
                "scenario_poi",
                "scenario_poi_points",
                "spell_scene",
                "spell_scene_event"
            };

            int matches = markers.Count(available.Contains);
            bool hasFamily = available.Any(table =>
                table.StartsWith("battle_pet_", StringComparison.OrdinalIgnoreCase) ||
                table.StartsWith("pet_battle_", StringComparison.OrdinalIgnoreCase) ||
                table.StartsWith("scenario_", StringComparison.OrdinalIgnoreCase));

            return matches >= 2 || (matches >= 1 && hasFamily);
        }

        // Resolve dynamic category according to the available model or service contract.
        private static string? ResolveDynamicCategory(string tableName)
        {
            string name = tableName.ToLowerInvariant();

            if (StartsOrContains(name, "creature", "pet_", "battle_pet", "pet_battle", "trainer", "npc_vendor", "npc_trainer", "vendor"))
            {
                return "creatures";
            }

            if (StartsOrContains(name, "gameobject"))
            {
                return "gameobjects";
            }

            if (StartsOrContains(name, "item", "item_upgrade", "loot_template", "reference_loot", "mail_loot", "disenchant", "prospecting", "milling"))
            {
                return "items";
            }

            if (StartsOrContains(name, "quest", "gossip", "npc_text", "points_of_interest"))
            {
                return "quests";
            }

            if (StartsOrContains(name, "spell", "skill_"))
            {
                return "spells";
            }

            if (StartsOrContains(name, "game_event", "battleground", "pool_", "achievement", "arena_", "dungeonfinder", "challenge_", "scenario_"))
            {
                return "events";
            }

            if (StartsOrContains(name, "player", "playercreate", "character", "reputation", "exploration_basexp", "mail_level_reward"))
            {
                return "players";
            }

            if (StartsOrContains(name, "locales_"))
            {
                return "locales";
            }

            if (StartsOrContains(name, "script", "smart_", "eventai", "dbscripts", "waypoint_scripts"))
            {
                return "scripts";
            }

            if (StartsOrContains(
                    name,
                    "area",
                    "areatrigger",
                    "graveyard",
                    "instance",
                    "map",
                    "transport",
                    "weather",
                    "phase_",
                    "quest_phase",
                    "scene_",
                    "spell_scene",
                    "vehicle") ||
                string.Equals(name, "world", StringComparison.Ordinal) ||
                name.StartsWith("world_", StringComparison.Ordinal))
            {
                return "world";
            }

            if (StartsOrContains(name, "command", "conditions", "disables", "version", "warden", "db_version", "hotfix"))
            {
                return "system";
            }

            return null;
        }

        // Match a value against configured prefixes or substrings.
        private static bool StartsOrContains(string value, params string[] tokens)
        {
            return tokens.Any(token => value.StartsWith(token, StringComparison.Ordinal) || value.Contains(token, StringComparison.Ordinal));
        }

        private sealed class CategoryAccumulator
        {
            // Handle category accumulator within the mangos schema catalog workflow.
            public CategoryAccumulator(string id, string name, string icon)
            {
                Id = id;
                Name = name;
                Icon = icon;
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

            public HashSet<string> Tables { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Convert collected schema data into a domain metadata model.
            public MangosTableCategory ToModel()
            {
                return new MangosTableCategory
                {
                    Id = Id,
                    Name = Name,
                    Icon = Icon,
                    Tables = Tables.OrderBy(table => table, StringComparer.OrdinalIgnoreCase).ToList()
                };
            }
        }

        private sealed class CatalogManifest
        {
            public List<CatalogCategory> Categories { get; set; } = new List<CatalogCategory>();
        }

        private sealed class CatalogCategory
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public List<string> Tables { get; set; } = new List<string>();
        }

    }
}
