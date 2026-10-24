// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Authorization roles and permission identifiers.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MangosDatabaseStudio.Domain.Security
{
    public static class AppRoles
    {
        public const string Viewer = "Viewer";
        public const string WorldEditor = "WorldEditor";
        public const string DatabaseOperator = "DatabaseOperator";
        public const string Administrator = "Administrator";

        public static IReadOnlyList<string> All
        {
            get;
        } = new[]
        {
            Viewer,
            WorldEditor,
            DatabaseOperator,
            Administrator
        };

        // Determine whether valid according to the available model or service contract.
        public static bool IsValid(string? role)
        {
            return !string.IsNullOrWhiteSpace(role) && All.Any(value => string.Equals(value, role, StringComparison.Ordinal));
        }

        // Read display name according to the available model or service contract.
        public static string GetDisplayName(string role)
        {
            return role switch
            {
                Viewer => "Viewer",
                WorldEditor => "World Editor",
                DatabaseOperator => "Database Operator",
                Administrator => "Administrator",
                _ => role
            };
        }

        // Read description according to the available model or service contract.
        public static string GetDescription(string role)
        {
            return role switch
            {
                Viewer => "Starts with read-only browsing permissions. An administrator can customize this role through RBAC.",
                WorldEditor => "Starts with world browsing and record editing permissions. Its RBAC grants are configurable.",
                DatabaseOperator => "Starts with world editing, SQL Console, SQL import, and SQL export permissions. Its RBAC grants are configurable.",
                Administrator => "Protected full access to every application permission, user account, role, and connection setting.",
                _ => string.Empty
            };
        }
    }
}
