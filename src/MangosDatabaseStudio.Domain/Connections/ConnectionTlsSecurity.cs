// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Validate trusted TLS settings for application and external MySQL connections.

using System;
using System.IO;

namespace MangosDatabaseStudio.Domain.Connections
{
    public static class ConnectionTlsSecurity
    {
        // Recognize only supported names. Numeric enum values and unknown modes fail closed.
        public static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode, "VerifyFull", StringComparison.OrdinalIgnoreCase))
            {
                return "VerifyFull";
            }

            if (string.Equals(mode, "VerifyCA", StringComparison.OrdinalIgnoreCase))
            {
                return "VerifyCA";
            }

            if (string.Equals(mode, "Required", StringComparison.OrdinalIgnoreCase))
            {
                return "Required";
            }

            if (string.Equals(mode, "Preferred", StringComparison.OrdinalIgnoreCase))
            {
                return "Preferred";
            }

            if (string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase))
            {
                return "Disabled";
            }

            throw new InvalidOperationException("Unsupported TLS mode. Reopen the connection profile and choose a valid TLS mode.");
        }

        // Both supported no-TLS names must bypass certificate settings.
        public static bool IsTlsDisabled(string? mode)
        {
            return string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase);
        }

        // The secure default validates the CA and server hostname.
        public static bool ValidatesServerIdentity(string? mode)
        {
            return string.Equals(mode, "VerifyFull", StringComparison.OrdinalIgnoreCase);
        }

        // Explicitly distinguish modes that do not authenticate the server hostname.
        public static bool RequiresRiskAcknowledgement(string? mode)
        {
            return !ValidatesServerIdentity(mode);
        }

        // Require a trusted CA usable by both MySqlConnector and command-line clients.
        public static void ValidateProfile(ConnectionProfile profile)
        {
            string mode = NormalizeMode(profile.SslMode);
            string caPath = profile.SslCa?.Trim() ?? string.Empty;

            if (mode == "VerifyFull" || mode == "VerifyCA")
            {
                if (string.IsNullOrWhiteSpace(caPath))
                {
                    throw new InvalidOperationException("Verified TLS requires a trusted CA certificate (PEM) path in the connection profile.");
                }
            }

            // No-TLS connections must not depend on a stale or missing CA path.
            if (mode != "Disabled" && !string.IsNullOrEmpty(caPath))
            {
                if (!Path.IsPathFullyQualified(caPath) || !File.Exists(caPath))
                {
                    throw new InvalidOperationException("The TLS CA file must be an existing absolute path on this computer.");
                }
            }
        }
    }
}
