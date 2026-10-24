// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Reflection;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class ApplicationVersionInfo
    {
        // Initialize the application version info dependencies and state.
        public ApplicationVersionInfo(string version, string stage, string informationalVersion)
        {
            Version = NormalizeVersion(version);
            Stage = NormalizeStage(stage);
            InformationalVersion = string.IsNullOrWhiteSpace(informationalVersion)
                ? Version + "-" + Stage.ToLowerInvariant()
                : informationalVersion.Trim();
        }

        public string Version
        {
            get;
        }

        public string Stage
        {
            get;
        }

        public string InformationalVersion
        {
            get;
        }

        public string DisplayText
        {
            get
            {
                return "Application: v." + Version + " (" + Stage + ")";
            }
        }

        // Handle from assembly within the application version info workflow.
        public static ApplicationVersionInfo FromAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            string? displayVersion = GetMetadata(assembly, "ApplicationDisplayVersion");
            string? releaseStage = GetMetadata(assembly, "ApplicationReleaseStage");
            string? informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(displayVersion))
            {
                displayVersion = ExtractVersion(informationalVersion);
            }

            if (string.IsNullOrWhiteSpace(displayVersion))
            {
                displayVersion = assembly.GetName().Version?.ToString(3);
            }

            return new ApplicationVersionInfo(
                displayVersion ?? "0.0.0",
                releaseStage ?? "Development",
                informationalVersion ?? string.Empty);
        }

        // Read metadata according to the available model or service contract.
        private static string? GetMetadata(Assembly assembly, string key)
        {
            foreach (AssemblyMetadataAttribute attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attribute.Key, key, StringComparison.Ordinal))
                {
                    return attribute.Value;
                }
            }

            return null;
        }

        // Handle extract version within the application version info workflow.
        private static string? ExtractVersion(string? informationalVersion)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion))
            {
                return null;
            }

            string value = informationalVersion.Trim();
            int buildMetadataIndex = value.IndexOf('+');
            if (buildMetadataIndex >= 0)
            {
                value = value[..buildMetadataIndex];
            }

            int prereleaseIndex = value.IndexOf('-');
            if (prereleaseIndex >= 0)
            {
                value = value[..prereleaseIndex];
            }

            return value;
        }

        // Normalize version according to the available model or service contract.
        private static string NormalizeVersion(string? version)
        {
            string value = version?.Trim() ?? string.Empty;
            if (value.StartsWith("v.", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..];
            }
            else if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value[1..];
            }

            return value.Length == 0 ? "0.0.0" : value;
        }

        // Normalize stage according to the available model or service contract.
        private static string NormalizeStage(string? stage)
        {
            string value = stage?.Trim() ?? string.Empty;
            return value.Length == 0 ? "Development" : value;
        }
    }
}
