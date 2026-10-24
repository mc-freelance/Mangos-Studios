// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Authorization roles and permission identifiers.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MangosDatabaseStudio.Domain.Security
{
    public sealed class AppPermissionDefinition
    {
        // Construct one named permission definition for access checks.
        public AppPermissionDefinition(
            string name,
            string category,
            string displayName,
            string description,
            bool administratorOnly = false,
            params string[] dependencies)
        {
            Name = name;
            Category = category;
            DisplayName = displayName;
            Description = description;
            AdministratorOnly = administratorOnly;
            Dependencies = dependencies ?? Array.Empty<string>();
        }

        public string Name
        {
            get;
        }

        public string Category
        {
            get;
        }

        public string DisplayName
        {
            get;
        }

        public string Description
        {
            get;
        }

        public bool AdministratorOnly
        {
            get;
        }

        public IReadOnlyList<string> Dependencies
        {
            get;
        }
    }

    public static class AppPermissions
    {
        public const string ConnectionsView = "connections.view";
        public const string ConnectionsSelect = "connections.select";
        public const string ConnectionsTest = "connections.test";
        public const string ConnectionsManage = "connections.manage";

        public const string WorldBrowse = "world.browse";
        public const string WorldCreate = "world.create";
        public const string WorldEdit = "world.edit";
        public const string WorldDelete = "world.delete";

        public const string DatabaseBrowse = "database.browse";
        public const string DatabaseStructureView = "database-structure.view";
        public const string DatabaseStructureModify = "database-structure.modify";
        public const string DatabaseStructureCreate = "database-structure.create";
        public const string DatabaseStructureDelete = "database-structure.delete";
        public const string DatabaseStructureIndexes = "database-structure.indexes";
        public const string DatabaseStructureTableProperties = "database-structure.table-properties";

        public const string SqlConsoleView = "sql-console.view";
        public const string SqlConsoleExecute = "sql-console.execute";
        public const string SqlConsoleDestructive = "sql-console.destructive";

        public const string SqlImportView = "sql-import.view";
        public const string SqlImportUpload = "sql-import.upload";
        public const string SqlImportExecute = "sql-import.execute";

        public const string SqlExportView = "sql-export.view";
        public const string SqlExportExecute = "sql-export.execute";

        public const string DatabaseUpdatesView = "database-updates.view";
        public const string DatabaseUpdatesScan = "database-updates.scan";
        public const string DatabaseUpdatesExecute = "database-updates.execute";
        public const string DatabaseUpdatesConfigure = "database-updates.configure";

        public const string ChangeStorageView = "change-storage.view";
        public const string ChangeStorageReview = "change-storage.review";
        public const string ChangeStorageContribution = "change-storage.contribution";

        public const string FieldDataView = "field-data.view";
        public const string FieldDataManage = "field-data.manage";

        public const string UsersView = "users.view";
        public const string UsersCreate = "users.create";
        public const string UsersEdit = "users.edit";
        public const string UsersDelete = "users.delete";

        public const string RolesManage = "roles.manage";

        private static readonly IReadOnlyList<AppPermissionDefinition> PermissionList = new[]
        {
            new AppPermissionDefinition(ConnectionsView, "Connections", "View connections", "See saved connection profiles without revealing stored passwords."),
            new AppPermissionDefinition(ConnectionsSelect, "Connections", "Select active connection", "Choose which saved server connection is active for the current session.", false, ConnectionsView),
            new AppPermissionDefinition(ConnectionsTest, "Connections", "Test connections", "Test an existing saved connection against its MySQL or MariaDB server.", false, ConnectionsView),
            new AppPermissionDefinition(ConnectionsManage, "Connections", "Manage connection details", "Create, edit, and delete saved connection profiles. This remains administrator-only.", true, ConnectionsView),

            new AppPermissionDefinition(WorldBrowse, "World Editor", "Browse world data", "Open the schema-aware World Editor and browse records.", false, ConnectionsSelect),
            new AppPermissionDefinition(WorldCreate, "World Editor", "Create world records", "Add new records through the World Editor.", false, WorldBrowse),
            new AppPermissionDefinition(WorldEdit, "World Editor", "Edit world records", "Update existing records through the World Editor.", false, WorldBrowse),
            new AppPermissionDefinition(WorldDelete, "World Editor", "Delete world records", "Delete existing records through the World Editor.", false, WorldBrowse),

            new AppPermissionDefinition(DatabaseBrowse, "Database Administration", "Browse databases and tables", "View visible databases, table lists, and storage metadata.", false, ConnectionsSelect),
            new AppPermissionDefinition(DatabaseStructureView, "Database Administration", "View table structure", "Open detailed table structure, column, index, and table-property metadata.", false, DatabaseBrowse),
            new AppPermissionDefinition(DatabaseStructureModify, "Database Administration", "Modify table columns", "Modify existing table columns through the guarded schema editor.", false, DatabaseStructureView),
            new AppPermissionDefinition(DatabaseStructureCreate, "Database Administration", "Add table columns", "Add new table columns through the guarded schema editor.", false, DatabaseStructureView),
            new AppPermissionDefinition(DatabaseStructureDelete, "Database Administration", "Drop table columns", "Permanently drop table columns after destructive-change confirmation.", false, DatabaseStructureView),
            new AppPermissionDefinition(DatabaseStructureIndexes, "Database Administration", "Manage table indexes", "Add or remove primary, unique, full-text, and standard indexes.", false, DatabaseStructureView),
            new AppPermissionDefinition(DatabaseStructureTableProperties, "Database Administration", "Manage table properties", "Change supported storage-engine, collation, and table-comment properties.", false, DatabaseStructureView),

            new AppPermissionDefinition(SqlConsoleView, "SQL Console", "Open SQL Console", "Open the SQL Console and view its query interface.", false, ConnectionsSelect),
            new AppPermissionDefinition(SqlConsoleExecute, "SQL Console", "Execute SQL", "Execute non-destructive SQL statements from the SQL Console.", false, SqlConsoleView),
            new AppPermissionDefinition(SqlConsoleDestructive, "SQL Console", "Execute destructive SQL", "Approve and execute destructive or privilege-changing SQL statements.", false, SqlConsoleExecute, SqlConsoleView),

            new AppPermissionDefinition(SqlImportView, "SQL Import", "Open SQL Import", "Open the SQL import workflow and review its safeguards.", false, ConnectionsSelect),
            new AppPermissionDefinition(SqlImportUpload, "SQL Import", "Upload SQL files", "Upload and analyze .sql or .sql.gz files.", false, SqlImportView),
            new AppPermissionDefinition(SqlImportExecute, "SQL Import", "Execute SQL imports", "Confirm and execute previously analyzed SQL imports.", false, SqlImportView),

            new AppPermissionDefinition(SqlExportView, "SQL Export", "Open SQL Export", "Choose database objects and export options.", false, ConnectionsSelect),
            new AppPermissionDefinition(SqlExportExecute, "SQL Export", "Create SQL exports", "Generate and download selected database structure and data.", false, SqlExportView),

            new AppPermissionDefinition(DatabaseUpdatesView, "Database Updates", "Open Database Updates", "Open the MaNGOS-aware database update planner.", false, ConnectionsSelect),
            new AppPermissionDefinition(DatabaseUpdatesScan, "Database Updates", "Scan update sources", "Read configured SQL update folders and build a version-aware update plan.", false, DatabaseUpdatesView),
            new AppPermissionDefinition(DatabaseUpdatesExecute, "Database Updates", "Apply database updates", "Execute a validated MaNGOS update chain and verify db_version after every patch.", false, DatabaseUpdatesScan, DatabaseUpdatesView),
            new AppPermissionDefinition(DatabaseUpdatesConfigure, "Database Updates", "Configure update sources", "Change the server-side SQL update folder for saved connection profiles. This remains administrator-only.", true, DatabaseUpdatesView),

            new AppPermissionDefinition(ChangeStorageView, "Change Storage", "View stored database changes", "Review the history of successful MaNGOS database mutations captured by Database Studio.", false, ConnectionsSelect),
            new AppPermissionDefinition(ChangeStorageReview, "Change Storage", "Review stored changes", "Review, delete, and automatically revert eligible pending database changes.", false, ChangeStorageView),
            new AppPermissionDefinition(ChangeStorageContribution, "Change Storage", "Build MaNGOS contributions", "Generate MaNGOS contribution SQL from approved reusable changes.", false, ChangeStorageReview, ChangeStorageView),

            new AppPermissionDefinition(FieldDataView, "Field Data", "View field-data catalog", "Open the expansion-aware field option and editor-definition catalog."),
            new AppPermissionDefinition(FieldDataManage, "Field Data", "Manage field-data catalog", "Add, edit, and remove field categories, column rules, expansion data sets, and entries. This remains administrator-only for now.", true, FieldDataView),

            new AppPermissionDefinition(UsersView, "Users", "View users", "View application user accounts and assigned roles."),
            new AppPermissionDefinition(UsersCreate, "Users", "Create users", "Create non-administrator application accounts.", false, UsersView),
            new AppPermissionDefinition(UsersEdit, "Users", "Edit users", "Edit non-administrator application accounts and assign existing non-administrator roles.", false, UsersView),
            new AppPermissionDefinition(UsersDelete, "Users", "Delete users", "Delete non-administrator application accounts.", false, UsersView),

            new AppPermissionDefinition(RolesManage, "Roles & Permissions", "Manage role permissions", "Configure RBAC permissions for application roles. This remains administrator-only.", true)
        };

        public static IReadOnlyList<AppPermissionDefinition> All { get; } = PermissionList;

        private static readonly Dictionary<string, AppPermissionDefinition> PermissionLookup =
            PermissionList.ToDictionary(permission => permission.Name, StringComparer.Ordinal);

        // Determine whether valid according to the available model or service contract.
        public static bool IsValid(string? permission)
        {
            return !string.IsNullOrWhiteSpace(permission) && PermissionLookup.ContainsKey(permission);
        }

        // Locate a compatible column from the available schema metadata.
        public static AppPermissionDefinition? Find(string permission)
        {
            return PermissionLookup.TryGetValue(permission, out AppPermissionDefinition? definition) ? definition : null;
        }

        // Read default permissions according to the available model or service contract.
        public static IReadOnlyCollection<string> GetDefaultPermissions(string role)
        {
            if (string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal))
            {
                return All.Select(permission => permission.Name).ToArray();
            }

            HashSet<string> permissions = new(StringComparer.Ordinal)
            {
                ConnectionsView,
                ConnectionsSelect,
                ConnectionsTest,
                WorldBrowse,
                DatabaseBrowse
            };

            if (string.Equals(role, AppRoles.WorldEditor, StringComparison.Ordinal) ||
                string.Equals(role, AppRoles.DatabaseOperator, StringComparison.Ordinal))
            {
                permissions.Add(WorldCreate);
                permissions.Add(WorldEdit);
                permissions.Add(WorldDelete);
                permissions.Add(ChangeStorageView);
                permissions.Add(ChangeStorageReview);
                permissions.Add(ChangeStorageContribution);
            }

            if (string.Equals(role, AppRoles.DatabaseOperator, StringComparison.Ordinal))
            {
                permissions.Add(SqlConsoleView);
                permissions.Add(SqlConsoleExecute);
                permissions.Add(SqlConsoleDestructive);
                permissions.Add(SqlImportView);
                permissions.Add(SqlImportUpload);
                permissions.Add(SqlImportExecute);
                permissions.Add(SqlExportView);
                permissions.Add(SqlExportExecute);
                permissions.Add(DatabaseUpdatesView);
                permissions.Add(DatabaseUpdatesScan);
                permissions.Add(DatabaseUpdatesExecute);
            }

            return NormalizeForRole(role, permissions);
        }

        // Normalize for role according to the available model or service contract.
        public static IReadOnlyCollection<string> NormalizeForRole(string role, IEnumerable<string>? permissions)
        {
            if (!AppRoles.IsValid(role))
            {
                throw new ArgumentException("A valid application role is required.", nameof(role));
            }

            if (string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal))
            {
                return All.Select(permission => permission.Name).ToArray();
            }

            HashSet<string> normalized = new(StringComparer.Ordinal);
            foreach (string permission in permissions ?? Array.Empty<string>())
            {
                if (!PermissionLookup.TryGetValue(permission, out AppPermissionDefinition? definition) || definition.AdministratorOnly)
                {
                    continue;
                }

                normalized.Add(permission);
            }

            bool changed;
            do
            {
                changed = false;
                foreach (string permission in normalized.ToArray())
                {
                    AppPermissionDefinition definition = PermissionLookup[permission];
                    foreach (string dependency in definition.Dependencies)
                    {
                        if (PermissionLookup.TryGetValue(dependency, out AppPermissionDefinition? dependencyDefinition) &&
                            !dependencyDefinition.AdministratorOnly && normalized.Add(dependency))
                        {
                            changed = true;
                        }
                    }
                }
            }
            while (changed);

            return normalized.OrderBy(permission => permission, StringComparer.Ordinal).ToArray();
        }
    }
}
