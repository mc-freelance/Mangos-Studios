// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

namespace MangosDatabaseStudio.Application.Schema
{
    public enum MangosProjectGeneration
    {
        Unknown = -1,
        Generation0 = 0,
        Generation1 = 1,
        Generation2 = 2,
        Generation3 = 3,
        Generation4 = 4
    }

    public static class MangosProjectNames
    {
        public const string Generation0 = "MangosZero";
        public const string Generation1 = "MangosOne";
        public const string Generation2 = "MangosTwo";
        public const string Generation3 = "MangosThree";
        public const string Generation4 = "MangosFour";

        // Read name according to the available model or service contract.
        public static string GetName(MangosProjectGeneration generation)
        {
            return generation switch
            {
                MangosProjectGeneration.Generation0 => Generation0,
                MangosProjectGeneration.Generation1 => Generation1,
                MangosProjectGeneration.Generation2 => Generation2,
                MangosProjectGeneration.Generation3 => Generation3,
                MangosProjectGeneration.Generation4 => Generation4,
                _ => "Custom Mangos-compatible"
            };
        }

        // Read schema version according to the available model or service contract.
        public static string GetSchemaVersion(MangosProjectGeneration generation)
        {
            return generation switch
            {
                MangosProjectGeneration.Generation0 => "0.x",
                MangosProjectGeneration.Generation1 => "1.x",
                MangosProjectGeneration.Generation2 => "2.x",
                MangosProjectGeneration.Generation3 => "3.x",
                MangosProjectGeneration.Generation4 => "4.x",
                _ => "Dynamic"
            };
        }

        // Read expansion name according to the available model or service contract.
        public static string GetExpansionName(MangosProjectGeneration generation)
        {
            return generation switch
            {
                MangosProjectGeneration.Generation0 => "Vanilla",
                MangosProjectGeneration.Generation1 => "The Burning Crusade",
                MangosProjectGeneration.Generation2 => "Wrath of the Lich King",
                MangosProjectGeneration.Generation3 => "Cataclysm",
                MangosProjectGeneration.Generation4 => "Mists of Pandaria",
                _ => "Unknown / custom"
            };
        }
    }
}
