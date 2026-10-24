// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Security.Cryptography;
using System.Text;

namespace MangosDatabaseStudio.App.Services
{
    public static class UserAvatarPresentation
    {
        // Read image url according to the available model or service contract.
        public static string? GetImageUrl(ApplicationUser? user)
        {
            if (user == null)
            {
                return null;
            }

            if (string.Equals(user.AvatarMode, UserAvatarModes.Upload, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                return "/profile/avatar/" + user.Id.ToString("D") + "?v=" + user.UpdatedAtUtc.Ticks;
            }

            if (string.Equals(user.AvatarMode, UserAvatarModes.Gravatar, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(user.Email))
            {
                byte[] input = Encoding.UTF8.GetBytes(user.Email.Trim().ToLowerInvariant());
                byte[] hash = MD5.HashData(input);
                string hex = Convert.ToHexString(hash).ToLowerInvariant();
                return "https://www.gravatar.com/avatar/" + hex + "?s=160&d=identicon&r=pg";
            }

            return null;
        }

        // Read initials according to the available model or service contract.
        public static string GetInitials(ApplicationUser? user)
        {
            string username = user?.Username?.Trim() ?? string.Empty;
            if (username.Length == 0)
            {
                return "?";
            }

            string[] parts = username.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();
            }

            return username.Substring(0, Math.Min(2, username.Length)).ToUpperInvariant();
        }
    }
}
