// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application startup, dependency injection, and desktop hosting.

using System;
using System.IO;
using System.Text.Json;
using MangosDatabaseStudio.Application.Platform;

namespace MangosDatabaseStudio.HardwareIdentityHelper
{
    internal static partial class Program
    {
        private const string DmiDirectory = "/sys/class/dmi/id";

        // Enter the desktop runtime and report startup failures without leaving a child server running.
        public static int Main(string[] args)
        {
            if (args.Length != 0)
            {
                Console.Error.WriteLine("This helper does not accept command-line arguments.");
                return 64;
            }

            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("The privileged hardware identity helper is supported only on Linux.");
                return 69;
            }

            if (LinuxUserInfo.GetEffectiveUserId() != 0)
            {
                Console.Error.WriteLine("The hardware identity helper must be launched through Polkit as root.");
                return 77;
            }

            HardwareIdentityResult result;
            try
            {
                result = ReadIdentity();
            }
            catch (Exception error)
            {
                result = HardwareIdentityResult.Unavailable(
                    "The privileged helper could not read the firmware identity: " + SanitizeMessage(error.Message));
            }

            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }

        // Read identity according to the available model or service contract.
        private static HardwareIdentityResult ReadIdentity()
        {
            string? manufacturer = ReadDmiValue("sys_vendor") ?? ReadDmiValue("board_vendor");
            string? model = ReadDmiValue("product_name") ?? ReadDmiValue("board_name");

            string? uuid = ReadDmiValue("product_uuid");
            if (IsUsableHardwareValue(uuid))
            {
                return HardwareIdentityResult.Available(
                    NormalizeIdentifier(uuid!),
                    "DMI system UUID",
                    manufacturer,
                    model);
            }

            string? productSerial = ReadDmiValue("product_serial");
            if (IsUsableHardwareValue(productSerial))
            {
                return HardwareIdentityResult.Available(
                    NormalizeIdentifier(productSerial!),
                    "DMI system serial",
                    manufacturer,
                    model);
            }

            string? boardSerial = ReadDmiValue("board_serial");
            if (IsUsableHardwareValue(boardSerial))
            {
                return HardwareIdentityResult.Available(
                    NormalizeIdentifier(boardSerial!),
                    "DMI baseboard serial",
                    manufacturer,
                    model);
            }

            return HardwareIdentityResult.Unavailable(
                "The firmware did not expose a usable DMI system UUID or serial number.",
                manufacturer,
                model);
        }

        // Read dmi value according to the available model or service contract.
        private static string? ReadDmiValue(string fileName)
        {
            string path = Path.Combine(DmiDirectory, fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            string value = File.ReadAllText(path).Trim();
            return IsUsableHardwareValue(value) ? value : null;
        }

        // Normalize identifier according to the available model or service contract.
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

        // Determine whether usable hardware value according to the available model or service contract.
        private static bool IsUsableHardwareValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            string compact = normalized
                .Replace("-", string.Empty, StringComparison.Ordinal)
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

        // Determine whether repeated according to the available model or service contract.
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

        // Remove sensitive or non-displayable details from an error message.
        private static string SanitizeMessage(string message)
        {
            string compact = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return compact.Length <= 300 ? compact : compact[..300];
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private sealed class HardwareIdentityResult
        {
            public bool IsAvailable
            {
                get; init;
            }

            public string? Identifier
            {
                get; init;
            }

            public string? Source
            {
                get; init;
            }

            public string? Manufacturer
            {
                get; init;
            }

            public string? Model
            {
                get; init;
            }

            public string? StatusMessage
            {
                get; init;
            }

            public DateTimeOffset CollectedAtUtc { get; init; } = DateTimeOffset.UtcNow;

            // Represent a successful device-information lookup result.
            public static HardwareIdentityResult Available(
                string identifier,
                string source,
                string? manufacturer,
                string? model)
            {
                return new HardwareIdentityResult
                {
                    IsAvailable = true,
                    Identifier = identifier,
                    Source = source,
                    Manufacturer = CleanDisplayValue(manufacturer),
                    Model = CleanDisplayValue(model),
                    StatusMessage = "The privileged helper read a firmware-provided hardware identifier."
                };
            }

            // Represent a device-information lookup that returned no usable value.
            public static HardwareIdentityResult Unavailable(
                string message,
                string? manufacturer = null,
                string? model = null)
            {
                return new HardwareIdentityResult
                {
                    IsAvailable = false,
                    Manufacturer = CleanDisplayValue(manufacturer),
                    Model = CleanDisplayValue(model),
                    StatusMessage = message
                };
            }
        }
    }
}
