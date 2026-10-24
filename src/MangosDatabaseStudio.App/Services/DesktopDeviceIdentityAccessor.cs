// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;

namespace MangosDatabaseStudio.App.Services
{
    // Provides device metadata collected by the native desktop executable.
    // The embedded desktop host reads the values collected by the main application process.
    public interface IDesktopDeviceIdentityAccessor
    {
        DesktopDeviceIdentitySnapshot Current
        {
            get;
        }
    }

    public sealed class DesktopDeviceIdentitySnapshot
    {
        public bool IsEnabled
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public string? Identifier
        {
            get; set;
        }

        public string? Source
        {
            get; set;
        }

        public string? Platform
        {
            get; set;
        }

        public string? Manufacturer
        {
            get; set;
        }

        public string? Model
        {
            get; set;
        }

        public string? StatusMessage
        {
            get; set;
        }

        public DateTimeOffset? CollectedAtUtc
        {
            get; set;
        }
    }

    public sealed class EnvironmentDesktopDeviceIdentityAccessor : IDesktopDeviceIdentityAccessor
    {
        // Handle environment desktop device identity accessor within the desktop device identity accessor workflow.
        public EnvironmentDesktopDeviceIdentityAccessor()
        {
            Current = ReadCurrent();
        }

        public DesktopDeviceIdentitySnapshot Current
        {
            get;
        }

        // Read current according to the available model or service contract.
        private static DesktopDeviceIdentitySnapshot ReadCurrent()
        {
            string? collectedAtValue = Environment.GetEnvironmentVariable("MDS_DEVICE_COLLECTED_AT_UTC");
            DateTimeOffset? collectedAt = DateTimeOffset.TryParse(collectedAtValue, out DateTimeOffset parsed)
                ? parsed
                : null;

            return new DesktopDeviceIdentitySnapshot
            {
                IsEnabled = string.Equals(
                    Environment.GetEnvironmentVariable("MDS_DEVICE_IDENTITY_ENABLED"),
                    "1",
                    StringComparison.Ordinal),
                IsAvailable = string.Equals(
                    Environment.GetEnvironmentVariable("MDS_DEVICE_AVAILABLE"),
                    "1",
                    StringComparison.Ordinal),
                Identifier = ReadValue("MDS_DEVICE_IDENTIFIER"),
                Source = ReadValue("MDS_DEVICE_IDENTIFIER_SOURCE"),
                Platform = ReadValue("MDS_DEVICE_PLATFORM"),
                Manufacturer = ReadValue("MDS_DEVICE_MANUFACTURER"),
                Model = ReadValue("MDS_DEVICE_MODEL"),
                StatusMessage = ReadValue("MDS_DEVICE_STATUS"),
                CollectedAtUtc = collectedAt
            };
        }

        // Read value according to the available model or service contract.
        private static string? ReadValue(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name)?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
