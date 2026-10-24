// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Native desktop startup, session isolation, or optional device-identity support.

using System;
using System.Globalization;

namespace MangosDatabaseStudio.App.Desktop
{
    internal sealed class DesktopOptions
    {
        public int? PreferredPort
        {
            get; private set;
        }

        public string EnvironmentName { get; private set; } = "Development";
        public bool DeviceIdentityEnabled
        {
            get; private set;
        }

        public bool ShowHelp
        {
            get; private set;
        }

        // Handle parse within the desktop options workflow.
        public static DesktopOptions Parse(string[] args)
        {
            DesktopOptions options = new();

            string? environmentPort = Environment.GetEnvironmentVariable("MDS_PORT");
            if (!string.IsNullOrWhiteSpace(environmentPort))
            {
                if (!int.TryParse(environmentPort, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort))
                {
                    throw new ArgumentException("MDS_PORT must be a number.");
                }

                options.PreferredPort = ValidatePort(parsedPort);
            }

            string? environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                options.EnvironmentName = environmentName.Trim();
            }

            string? environmentDeviceIdentity = Environment.GetEnvironmentVariable("MDS_DEVICE_IDENTITY_ENABLED");
            if (!string.IsNullOrWhiteSpace(environmentDeviceIdentity))
            {
                options.DeviceIdentityEnabled = ParseBoolean(environmentDeviceIdentity, "MDS_DEVICE_IDENTITY_ENABLED");
            }

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--port":
                        string portValue = RequireValue(args, ref index, "--port");
                        if (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                        {
                            throw new ArgumentException("--port must be a number.");
                        }

                        options.PreferredPort = ValidatePort(port);
                        break;
                    case "--environment":
                        options.EnvironmentName = RequireValue(args, ref index, "--environment");
                        break;
                    case "--device-identity":
                        options.DeviceIdentityEnabled = true;
                        break;
                    case "--no-device-identity":
                        options.DeviceIdentityEnabled = false;
                        break;
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + args[index]);
                }
            }

            return options;
        }

        // Handle print usage within the desktop options workflow.
        public static void PrintUsage()
        {
            Console.WriteLine("MaNGOS Database Studio");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  MangosDatabaseStudio [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --port <port>            Optional preferred private loopback port");
            Console.WriteLine("  --environment <name>     Application environment");
            Console.WriteLine("  --device-identity        Enable hardware identity collection");
            Console.WriteLine("  --no-device-identity     Disable hardware identity collection (default)");
            Console.WriteLine("  -h, --help               Show this help");
        }

        // Parse boolean as part of the local desktop lifecycle.
        private static bool ParseBoolean(string value, string option)
        {
            string normalized = value.Trim();
            if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new ArgumentException(option + " must be 1/0, true/false, yes/no, or on/off.");
        }

        // Require value as part of the local desktop lifecycle.
        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException(option + " requires a value.");
            }

            index++;
            return args[index];
        }

        // Validate port as part of the local desktop lifecycle.
        private static int ValidatePort(int port)
        {
            if (port < 1024 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "Private desktop ports must be between 1024 and 65535.");
            }

            return port;
        }
    }
}
