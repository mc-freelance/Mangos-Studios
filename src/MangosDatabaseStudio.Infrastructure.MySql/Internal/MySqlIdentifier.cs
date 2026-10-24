// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Quote SQL identifiers safely for schema-aware queries.

using System;
using System.Text.RegularExpressions;

namespace MangosDatabaseStudio.Infrastructure.MySql.Internal
{
    internal static class MySqlIdentifier
    {
        private static readonly Regex ValidIdentifier = new("^[A-Za-z0-9_$]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Quote a database identifier through the shared quoting helper.
        public static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || !ValidIdentifier.IsMatch(identifier))
            {
                throw new ArgumentException("The MySQL identifier contains unsupported characters.", nameof(identifier));
            }

            return "`" + identifier + "`";
        }
    }
}
