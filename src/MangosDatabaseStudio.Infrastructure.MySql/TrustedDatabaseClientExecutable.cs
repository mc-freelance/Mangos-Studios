// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Validate explicitly configured native database clients before executing a process.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    public static class TrustedDatabaseClientExecutable
    {
        // Database client paths are configuration, never commands or PATH lookups.
        // Keep the import and export command families separate.
        public static string Resolve(string? configuredPath, bool isExport)
        {
            string settingName = isExport ? "MySql:ExportClientPath" : "MySql:ImportClientPath";
            if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath))
            {
                throw new InvalidOperationException(
                    settingName + " must be configured with an absolute path to a trusted MySQL or MariaDB executable. "
                    + "The application does not search PATH for database tools.");
            }

            string suppliedPath = Path.GetFullPath(configuredPath.Trim());
            // Native clients on remote SMB/UNC shares are not a trusted local
            // execution source, even if a server reports an apparently safe ACL.
            if (OperatingSystem.IsWindows() && suppliedPath.StartsWith("\\\\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    settingName + " must point to an executable on a trusted local disk, not a network share.");
            }

            string fileName = Path.GetFileName(suppliedPath);
            string[] acceptedNames = isExport
                ? ["mysqldump", "mariadb-dump", "mysqldump.exe", "mariadb-dump.exe"]
                : ["mysql", "mariadb", "mysql.exe", "mariadb.exe"];

            if (Array.FindIndex(acceptedNames, name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                throw new InvalidOperationException(
                    settingName + " must name a supported MySQL/MariaDB "
                    + (isExport ? "dump" : "import") + " executable.");
            }

            try
            {
                // A symlink may be supplied by a distro package. Validate the link's
                // parent and the resolved target before executing the target itself.
                VerifyParentDirectories(suppliedPath);
                FileInfo original = new(suppliedPath);
                FileSystemInfo? linkTarget = original.ResolveLinkTarget(returnFinalTarget: true);
                string executable = linkTarget == null
                    ? suppliedPath
                    : Path.GetFullPath(linkTarget.FullName);

                VerifyParentDirectories(executable);
                VerifyExecutableFile(executable);
                return executable;
            }
            catch (Exception error) when (
                error is IOException
                || error is UnauthorizedAccessException
                || error is ArgumentException
                || error is NotSupportedException)
            {
                throw new InvalidOperationException(
                    "Cannot verify the configured database client at " + suppliedPath
                    + ". Select a regular executable under a directory protected from other users.", error);
            }
        }

        // Check each parent directory because a replaceable ancestor can redirect
        // execution even when the final executable has safe permissions.
        private static void VerifyParentDirectories(string executable)
        {
            string? directory = Path.GetDirectoryName(executable);
            while (!string.IsNullOrEmpty(directory))
            {
                FileAttributes attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.Directory) == 0
                    || (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "The database client path contains an untrusted directory or directory link: " + directory);
                }

                if (OperatingSystem.IsWindows())
                {
                    // Drive roots can allow creating unrelated directories while still
                    // protecting their existing children. Check their child ACLs instead.
                    if (!string.Equals(Path.GetPathRoot(directory), directory, StringComparison.OrdinalIgnoreCase))
                    {
                        VerifyWindowsAcl(directory, isDirectory: true);
                    }
                }
                else
                {
                    VerifyUnixMode(directory);
                }

                string? parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.Ordinal))
                {
                    break;
                }

                directory = parent;
            }
        }

        // Reject non-files, reparse points and executables that another user can alter.
        private static void VerifyExecutableFile(string executable)
        {
            FileAttributes attributes = File.GetAttributes(executable);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            {
                throw new InvalidOperationException("The database client must be a regular executable file.");
            }

            if (OperatingSystem.IsWindows())
            {
                VerifyWindowsAcl(executable, isDirectory: false);
            }
            else
            {
                VerifyUnixMode(executable);
                UnixFileMode mode = File.GetUnixFileMode(executable);
                if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                {
                    throw new InvalidOperationException("The database client is not marked executable.");
                }
            }
        }

        // Fail closed if any POSIX group or other account can replace the tool
        // or modify one of its enclosing directories.
        [UnsupportedOSPlatform("windows")]
        private static void VerifyUnixMode(string path)
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                throw new InvalidOperationException(
                    "The database client path is writable by other users: " + path);
            }
        }

        // Windows requires checking DACLs, not Unix file modes. Refuse write
        // permissions granted to principals other than the current user, SYSTEM,
        // or administrators; unexpected groups fail closed.
        [SupportedOSPlatform("windows")]
        private static void VerifyWindowsAcl(string path, bool isDirectory)
        {
            FileSystemSecurity security = isDirectory
                ? (FileSystemSecurity)FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(path))
                : FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
            AuthorizationRuleCollection rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier));

            SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
            HashSet<string> trustedPrincipals = new(StringComparer.OrdinalIgnoreCase)
            {
                "S-1-5-18",        // Local SYSTEM.
                "S-1-5-32-544",    // Built-in Administrators.
                "S-1-3-0",         // CREATOR OWNER (inherited placeholder).
                "S-1-3-4"          // OWNER RIGHTS.
            };

            if (currentUser != null)
            {
                trustedPrincipals.Add(currentUser.Value);
            }

            const FileSystemRights riskyRights = FileSystemRights.WriteData
                | FileSystemRights.AppendData
                | FileSystemRights.Delete
                | FileSystemRights.DeleteSubdirectoriesAndFiles
                | FileSystemRights.ChangePermissions
                | FileSystemRights.TakeOwnership
                | FileSystemRights.WriteAttributes
                | FileSystemRights.WriteExtendedAttributes;

            foreach (AuthorizationRule authorization in rules)
            {
                if (authorization is not FileSystemAccessRule rule
                    || rule.AccessControlType != AccessControlType.Allow
                    || (rule.FileSystemRights & riskyRights) == 0)
                {
                    continue;
                }

                if (rule.IdentityReference is not SecurityIdentifier principal
                    || !trustedPrincipals.Contains(principal.Value))
                {
                    throw new InvalidOperationException(
                        "The database client path grants modification rights to another account: " + path);
                }
            }
        }
    }
}
