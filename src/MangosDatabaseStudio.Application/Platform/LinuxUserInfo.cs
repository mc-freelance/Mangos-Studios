// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Platform-specific operating-system interoperability.

using System;
using System.Diagnostics;

namespace MangosDatabaseStudio.Application.Platform
{
    public static class LinuxUserInfo
    {
        // Read effective user id according to the available model or service contract.
        public static uint GetEffectiveUserId()
        {
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/id",
                    Arguments = "-u",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                if (!process.Start())
                {
                    return uint.MaxValue;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 || !int.TryParse(output.Trim(), out int uid))
                {
                    return uint.MaxValue;
                }

                return (uint)uid;
            }
            catch (Exception)
            {
                return uint.MaxValue;
            }
        }
    }
}
