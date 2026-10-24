// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Native desktop startup, session isolation, or optional device-identity support.

using System;
using MangosDatabaseStudio.Application.Platform;

namespace MangosDatabaseStudio.App.Desktop
{
    internal static class DesktopPrivilegeGuard
    {
        // Ensure main process is unprivileged as part of the local desktop lifecycle.
        public static void EnsureMainProcessIsUnprivileged()
        {
            if (OperatingSystem.IsLinux() && LinuxUserInfo.GetEffectiveUserId() == 0)
            {
                throw new InvalidOperationException(
                    "Do not run Mangos Database Studio with sudo. Start it as your normal user; the restricted Polkit helper requests administrator authorization separately.");
            }
        }
    }
}
