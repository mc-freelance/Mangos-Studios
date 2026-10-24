// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Linq;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.App.Services
{
    // Keeps MySQL and MariaDB internal schemas out of user-facing pickers and browsers.
    // The application and database driver may still use these schemas internally when required.
    public static class DatabaseVisibilityPolicy
    {
        private static readonly HashSet<string> ProtectedDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "information_schema",
            "mysql",
            "performance_schema",
            "sys"
        };

        // Determine whether protected according to the available model or service contract.
        public static bool IsProtected(string? database)
        {
            return !string.IsNullOrWhiteSpace(database) && ProtectedDatabases.Contains(database);
        }

        // Determine whether visible according to the available model or service contract.
        public static bool IsVisible(string? database)
        {
            return !string.IsNullOrWhiteSpace(database) && !IsProtected(database);
        }

        // Filter visible according to the available model or service contract.
        public static IReadOnlyList<DatabaseMetadata> FilterVisible(IEnumerable<DatabaseMetadata> databases)
        {
            return databases
                .Where(database => IsVisible(database.Name))
                .OrderBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Resolve selection according to the available model or service contract.
        public static string ResolveSelection(
            IReadOnlyList<DatabaseMetadata> visibleDatabases,
            string? requestedDatabase,
            string? defaultDatabase)
        {
            if (Contains(visibleDatabases, requestedDatabase))
            {
                return requestedDatabase!;
            }

            if (Contains(visibleDatabases, defaultDatabase))
            {
                return defaultDatabase!;
            }

            return visibleDatabases.FirstOrDefault()?.Name ?? string.Empty;
        }

        // Handle contains within the database visibility policy workflow.
        public static bool Contains(IReadOnlyList<DatabaseMetadata> visibleDatabases, string? database)
        {
            return IsVisible(database) && visibleDatabases.Any(item =>
                string.Equals(item.Name, database, StringComparison.OrdinalIgnoreCase));
        }
    }
}
