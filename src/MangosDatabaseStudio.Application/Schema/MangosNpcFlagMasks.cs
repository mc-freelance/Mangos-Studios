// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System;

namespace MangosDatabaseStudio.Application.Schema
{
    public static class MangosNpcFlagMasks
    {
        public const ulong Gossip = 0x00000001UL;
        public const ulong LegacyVendor = 0x00000004UL;
        public const ulong StandardVendor = 0x00000080UL;
        public const ulong LegacyTrainer = 0x00000010UL;
        public const ulong StandardTrainer = 0x00000010UL | 0x00000020UL | 0x00000040UL;

        // Resolve vendor mask according to the available model or service contract.
        public static ulong ResolveVendorMask(MangosSchemaProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return profile.Generation == MangosProjectGeneration.Generation0
                ? LegacyVendor
                : StandardVendor;
        }

        // Resolve trainer mask according to the available model or service contract.
        public static ulong ResolveTrainerMask(MangosSchemaProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return profile.Generation == MangosProjectGeneration.Generation0
                ? LegacyTrainer
                : StandardTrainer;
        }

        // Format hex according to the available model or service contract.
        public static string FormatHex(ulong value)
        {
            return "0x" + value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
