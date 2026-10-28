// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Baseline expansion-aware map identifiers for creature and gameobject spawn editors.

using System;
using System.Linq;

namespace MangosDatabaseStudio.App.Services.FieldData.MapNames
{
    internal static class MapNamesFieldData
    {
        // Keep a single SQLite definition for both spawn tables. The editor resolver
        // aliases gameobject.map to this shared definition rather than duplicating it.
        public const string CreatureMapSourceKey = "builtin.map_names.creature.map";
        public const string LegacyGameObjectMapSourceKey = "builtin.map_names.gameobject.map";

        // Map identifiers refer to Map.dbc, not zone IDs or the modern UI map IDs.
        // This list is intentionally a starter catalog, not a complete Map.dbc export.
        // Introduced is the first supported MaNGOS generation containing that map.
        private static readonly (ulong Id, string Name, int Introduced)[] KnownMaps =
        {
            (0, "Eastern Kingdoms", 0),
            (1, "Kalimdor", 0),
            (30, "Alterac Valley", 0),
            (33, "Shadowfang Keep", 0),
            (36, "The Deadmines", 0),
            (43, "Wailing Caverns", 0),
            (47, "Razorfen Kraul", 0),
            (48, "Blackfathom Deeps", 0),
            (70, "Uldaman", 0),
            (90, "Gnomeregan", 0),
            (109, "The Temple of Atal'Hakkar", 0),
            (129, "Razorfen Downs", 0),
            (189, "Scarlet Monastery", 0),
            (209, "Zul'Farrak", 0),
            (229, "Blackrock Spire", 0),
            (230, "Blackrock Depths", 0),
            (249, "Onyxia's Lair", 0),
            (289, "Scholomance", 0),
            (329, "Stratholme", 0),
            (389, "Ragefire Chasm", 0),
            (409, "Molten Core", 0),
            (429, "Dire Maul", 0),
            (469, "Blackwing Lair", 0),
            (509, "Ruins of Ahn'Qiraj", 0),
            (531, "Temple of Ahn'Qiraj", 0),
            (530, "Outland", 1),
            (532, "Karazhan", 1),
            (543, "Hellfire Ramparts", 1),
            (564, "Black Temple", 1),
            (580, "Sunwell Plateau", 1),
            (571, "Northrend", 2),
            (574, "Utgarde Keep", 2),
            (603, "Ulduar", 2),
            (631, "Icecrown Citadel", 2),
            (646, "Deepholm", 3),
            (645, "Blackrock Caverns", 3),
            (720, "Firelands", 3),
            (967, "Dragon Soul", 3),
            (860, "The Wandering Isle", 4),
            (870, "Pandaria", 4),
            (960, "Temple of the Jade Serpent", 4),
            (1008, "Mogu'shan Vaults", 4)
        };

        // Bind Map Names to the spawn columns used by the World Database editor.
        // Discovery picks up these Create* methods without a separate registry list.
        public static FieldDefinitionSeed CreateCreatureMap()
        {
            return BuildDefinition();
        }

        // Each generation gets a distinct variant to avoid suggesting maps from newer
        // expansions when the connected server does not support them.
        private static FieldDefinitionSeed BuildDefinition()
        {
            FieldVariantSeed[] variants = Enumerable.Range(0, 5)
                .Select(generation => FieldDataSeedFactory.Variant(
                    "generation-" + generation,
                    new[] { generation },
                    "Known map identifiers available in this expansion. Add Map.dbc entries through Field Data.",
                    KnownMaps
                        .Where(map => map.Introduced <= generation)
                        .OrderBy(map => map.Id)
                        .Select(map => FieldDataSeedFactory.Option(map.Id, map.Name))
                        .ToArray()))
                .ToArray();

            return FieldDataSeedFactory.Select(
                CreatureMapSourceKey,
                "creature",
                "map",
                "Map.dbc identifier for a world spawn. This is a starter list; unknown map values are preserved.",
                variants);
        }
    }
}
