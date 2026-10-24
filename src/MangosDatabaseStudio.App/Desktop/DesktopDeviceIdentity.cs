// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Native desktop startup, session isolation, or optional device-identity support.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;

namespace MangosDatabaseStudio.App.Desktop
{
    // Reads a firmware-provided hardware identifier for desktop-mode device authorization.
    // No random or application-generated fallback is used because the future server must be
    // able to compare the identifier to an administrator-managed device allowlist.
    internal sealed class DesktopDeviceIdentity
    {
        public bool IsEnabled { get; private set; } = true;
        public bool IsAvailable => IsEnabled && !string.IsNullOrWhiteSpace(Identifier);
        public string? Identifier
        {
            get; private set;
        }

        public string? Source
        {
            get; private set;
        }

        public string Platform { get; } = RuntimeInformation.OSDescription.Trim();
        public string? Manufacturer
        {
            get; private set;
        }

        public string? Model
        {
            get; private set;
        }

        public string? StatusMessage
        {
            get; private set;
        }

        public DateTimeOffset CollectedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

        // Represent a successful device-information lookup result.
        public static DesktopDeviceIdentity Available(
            string identifier,
            string source,
            string? manufacturer,
            string? model,
            string? statusMessage = null,
            DateTimeOffset? collectedAtUtc = null)
        {
            return new DesktopDeviceIdentity
            {
                IsEnabled = true,
                Identifier = NormalizeIdentifier(identifier),
                Source = source,
                Manufacturer = CleanDisplayValue(manufacturer),
                Model = CleanDisplayValue(model),
                StatusMessage = statusMessage ?? "A firmware-provided hardware identifier is available.",
                CollectedAtUtc = collectedAtUtc ?? DateTimeOffset.UtcNow
            };
        }

        // Represent a device-information lookup that returned no usable value.
        public static DesktopDeviceIdentity Unavailable(
            string message,
            string? manufacturer = null,
            string? model = null,
            DateTimeOffset? collectedAtUtc = null)
        {
            return new DesktopDeviceIdentity
            {
                IsEnabled = true,
                Manufacturer = CleanDisplayValue(manufacturer),
                Model = CleanDisplayValue(model),
                StatusMessage = message,
                CollectedAtUtc = collectedAtUtc ?? DateTimeOffset.UtcNow
            };
        }

        // Handle disabled within the desktop device identity workflow.
        public static DesktopDeviceIdentity Disabled()
        {
            return new DesktopDeviceIdentity
            {
                IsEnabled = false,
                StatusMessage = "Device Identity is disabled. Restart Mangos Database Studio with --device-identity to enable it.",
                CollectedAtUtc = DateTimeOffset.UtcNow
            };
        }

        // Normalize identifier as part of the local desktop lifecycle.
        private static string NormalizeIdentifier(string value)
        {
            string cleaned = value.Trim();
            return Guid.TryParse(cleaned, out Guid guid)
                ? guid.ToString("D").ToUpperInvariant()
                : cleaned.ToUpperInvariant();
        }

        // Normalize firmware metadata to a safe display value.
        private static string? CleanDisplayValue(string? value)
        {
            string? cleaned = value?.Trim();
            return IsUsableHardwareValue(cleaned) ? cleaned : null;
        }

        // Determine whether usable hardware value as part of the local desktop lifecycle.
        internal static bool IsUsableHardwareValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            string compact = normalized.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("{", string.Empty, StringComparison.Ordinal)
                .Replace("}", string.Empty, StringComparison.Ordinal);

            if (compact.Length > 0 && (IsRepeated(compact, '0') || IsRepeated(compact, 'F')))
            {
                return false;
            }

            return !string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "Unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "Not Specified", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "Default string", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "System Serial Number", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalized, "To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase);
        }

        // Determine whether repeated as part of the local desktop lifecycle.
        private static bool IsRepeated(string value, char character)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.ToUpperInvariant(value[index]) != character)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static class DesktopDeviceIdentityProvider
    {
        private const string LinuxPkexecPath = "/usr/bin/pkexec";
        private const string LinuxHelperPath = "/usr/lib/mangos-database-studio/hardware-identity/mangos-hwid-helper";
        private const string LinuxPolicyPath = "/usr/share/polkit-1/actions/org.mangosdatabasestudio.deviceidentity.policy";
        private const int HelperTimeoutMilliseconds = 120_000;
        private const int MaximumHelperOutputLength = 32_768;

        private static readonly JsonSerializerOptions HelperJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Handle collect within the desktop device identity workflow.
        public static DesktopDeviceIdentity Collect()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return CollectWindows();
                }

                if (OperatingSystem.IsLinux())
                {
                    return CollectLinux();
                }

                return DesktopDeviceIdentity.Unavailable(
                    "Hardware identifier collection is not implemented for this desktop operating system.");
            }
            catch (Exception error)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The hardware identifier could not be read: " + SanitizeMessage(error.Message));
            }
        }

        [SupportedOSPlatform("windows")]
        // Collect windows as part of the local desktop lifecycle.
        private static DesktopDeviceIdentity CollectWindows()
        {
            string? uuid = null;
            string? serial = null;
            string? manufacturer = null;
            string? model = null;

            using (ManagementObjectSearcher productSearcher = new(
                "SELECT UUID, Vendor, Name, IdentifyingNumber FROM Win32_ComputerSystemProduct"))
            using (ManagementObjectCollection products = productSearcher.Get())
            {
                foreach (ManagementObject product in products.Cast<ManagementObject>())
                {
                    using (product)
                    {
                        uuid = ReadManagementValue(product, "UUID");
                        serial = ReadManagementValue(product, "IdentifyingNumber");
                        manufacturer = ReadManagementValue(product, "Vendor");
                        model = ReadManagementValue(product, "Name");
                        break;
                    }
                }
            }

            if (DesktopDeviceIdentity.IsUsableHardwareValue(uuid))
            {
                return DesktopDeviceIdentity.Available(uuid!, "SMBIOS system UUID", manufacturer, model);
            }

            if (DesktopDeviceIdentity.IsUsableHardwareValue(serial))
            {
                return DesktopDeviceIdentity.Available(serial!, "SMBIOS system serial", manufacturer, model);
            }

            string? boardSerial = null;
            string? boardManufacturer = null;
            string? boardModel = null;
            using (ManagementObjectSearcher boardSearcher = new(
                "SELECT SerialNumber, Manufacturer, Product FROM Win32_BaseBoard"))
            using (ManagementObjectCollection boards = boardSearcher.Get())
            {
                foreach (ManagementObject board in boards.Cast<ManagementObject>())
                {
                    using (board)
                    {
                        boardSerial = ReadManagementValue(board, "SerialNumber");
                        boardManufacturer = ReadManagementValue(board, "Manufacturer");
                        boardModel = ReadManagementValue(board, "Product");
                        break;
                    }
                }
            }

            if (DesktopDeviceIdentity.IsUsableHardwareValue(boardSerial))
            {
                return DesktopDeviceIdentity.Available(
                    boardSerial!,
                    "SMBIOS baseboard serial",
                    manufacturer ?? boardManufacturer,
                    model ?? boardModel);
            }

            return DesktopDeviceIdentity.Unavailable(
                "Windows did not expose a usable SMBIOS hardware identifier.",
                manufacturer ?? boardManufacturer,
                model ?? boardModel);
        }

        [SupportedOSPlatform("linux")]
        // Collect linux as part of the local desktop lifecycle.
        private static DesktopDeviceIdentity CollectLinux()
        {
            string? manufacturer = ReadLinuxDisplayValue("sys_vendor") ?? ReadLinuxDisplayValue("board_vendor");
            string? model = ReadLinuxDisplayValue("product_name") ?? ReadLinuxDisplayValue("board_name");

            if (!File.Exists(LinuxPkexecPath))
            {
                return DesktopDeviceIdentity.Unavailable(
                    "Polkit is not available. Install pkexec to authorize hardware identity collection.",
                    manufacturer,
                    model);
            }

            if (!File.Exists(LinuxHelperPath) || !File.Exists(LinuxPolicyPath))
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged hardware identity helper or its Polkit policy is not installed. Run install-hardware-helper.sh, then restart the desktop application.",
                    manufacturer,
                    model);
            }

            FileInfo helperFile = new(LinuxHelperPath);
            if (!string.IsNullOrWhiteSpace(helperFile.LinkTarget))
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged hardware identity helper failed its integrity check because symbolic links are not allowed.",
                    manufacturer,
                    model);
            }

            UnixFileMode mode = File.GetUnixFileMode(LinuxHelperPath);
            const UnixFileMode unsafeWriteModes = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
            if ((mode & unsafeWriteModes) != 0)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged hardware identity helper has unsafe file permissions and was not executed.",
                    manufacturer,
                    model);
            }

            ProcessStartInfo startInfo = new(LinuxPkexecPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(LinuxHelperPath);

            using Process process = new()
            {
                StartInfo = startInfo
            };
            if (!process.Start())
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The administrator authorization request could not be started.",
                    manufacturer,
                    model);
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(HelperTimeoutMilliseconds))
            {
                TryKill(process);
                return DesktopDeviceIdentity.Unavailable(
                    "Administrator authorization timed out before the hardware identifier could be read.",
                    manufacturer,
                    model);
            }

            string output = outputTask.GetAwaiter().GetResult();
            string errorOutput = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode == 126)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "Device identifier collection was cancelled by the user.",
                    manufacturer,
                    model);
            }

            if (process.ExitCode == 127)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "Administrator authorization was denied or could not be completed.",
                    manufacturer,
                    model);
            }

            if (process.ExitCode != 0)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged helper failed with exit code " + process.ExitCode + ": " + SanitizeMessage(errorOutput),
                    manufacturer,
                    model);
            }

            if (string.IsNullOrWhiteSpace(output) || output.Length > MaximumHelperOutputLength)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged helper returned an invalid response.",
                    manufacturer,
                    model);
            }

            LinuxHardwareIdentityResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<LinuxHardwareIdentityResponse>(output, HelperJsonOptions);
            }
            catch (JsonException)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged helper returned malformed device identity data.",
                    manufacturer,
                    model);
            }

            if (response == null)
            {
                return DesktopDeviceIdentity.Unavailable(
                    "The privileged helper did not return device identity data.",
                    manufacturer,
                    model);
            }

            if (response.IsAvailable && DesktopDeviceIdentity.IsUsableHardwareValue(response.Identifier))
            {
                return DesktopDeviceIdentity.Available(
                    response.Identifier!,
                    (response.Source ?? "DMI hardware identifier") + " via privileged helper",
                    response.Manufacturer ?? manufacturer,
                    response.Model ?? model,
                    response.StatusMessage ?? "Administrator-authorized DMI identity collection completed.",
                    response.CollectedAtUtc);
            }

            return DesktopDeviceIdentity.Unavailable(
                response.StatusMessage ?? "The privileged helper did not find a usable DMI hardware identifier.",
                response.Manufacturer ?? manufacturer,
                response.Model ?? model,
                response.CollectedAtUtc);
        }

        // Read management value as part of the local desktop lifecycle.
        private static string? ReadManagementValue(ManagementBaseObject source, string propertyName)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            object? value = source[propertyName];
            return value?.ToString()?.Trim();
        }

        // Read linux display value as part of the local desktop lifecycle.
        private static string? ReadLinuxDisplayValue(string fileName)
        {
            string path = Path.Combine("/sys/class/dmi/id", fileName);
            try
            {
                string? value = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
                return DesktopDeviceIdentity.IsUsableHardwareValue(value) ? value : null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        // Attempt to kill as part of the local desktop lifecycle.
        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while timeout cleanup was running.
            }
        }

        // Remove sensitive or non-displayable details from an error message.
        private static string SanitizeMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "No additional error information was provided.";
            }

            string compact = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return compact.Length <= 300 ? compact : compact[..300];
        }

        private sealed class LinuxHardwareIdentityResponse
        {
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
    }
}
