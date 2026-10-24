// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;

namespace MangosDatabaseStudio.App.Services
{
    public static class UserThemeModes
    {
        public const string System = "system";
        public const string Light = "light";
        public const string Dark = "dark";

        // Determine whether valid according to the available model or service contract.
        public static bool IsValid(string? value)
        {
            return string.Equals(value, System, StringComparison.Ordinal) ||
                   string.Equals(value, Light, StringComparison.Ordinal) ||
                   string.Equals(value, Dark, StringComparison.Ordinal);
        }
    }

    public static class UserAvatarModes
    {
        public const string Initials = "initials";
        public const string Gravatar = "gravatar";
        public const string Upload = "upload";

        // Determine whether valid according to the available model or service contract.
        public static bool IsValid(string? value)
        {
            return string.Equals(value, Initials, StringComparison.Ordinal) ||
                   string.Equals(value, Gravatar, StringComparison.Ordinal) ||
                   string.Equals(value, Upload, StringComparison.Ordinal);
        }
    }

    public static class DesktopZoomPreferences
    {
        public const int MinimumPercent = 50;
        public const int MaximumPercent = 125;
        public const int DefaultPercent = 100;
        public const int Step = 5;

        // Determine whether supported according to the available model or service contract.
        public static bool IsSupported(int value)
        {
            return value >= MinimumPercent &&
                   value <= MaximumPercent &&
                   value % Step == 0;
        }

        // Normalize a stored value for stable comparisons and display.
        public static int Normalize(int value)
        {
            if (!IsSupported(value))
            {
                return DefaultPercent;
            }

            return value;
        }
    }

    public sealed class UserPreferences
    {
        public int DefaultPageSize { get; set; } = 20;
        public int BrowserColumnCount { get; set; } = 9;
        public int CellPreviewLength { get; set; } = 80;
        public bool ShowTechnicalColumnNames { get; set; } = true;
        public string Theme { get; set; } = UserThemeModes.System;
        public int DesktopZoomPercent { get; set; } = DesktopZoomPreferences.DefaultPercent;
        public bool DesktopAutoScale { get; set; } = true;

        // Normalize a stored value for stable comparisons and display.
        public static UserPreferences Normalize(UserPreferences? preferences)
        {
            UserPreferences source = preferences ?? new UserPreferences();
            return new UserPreferences
            {
                DefaultPageSize = source.DefaultPageSize switch
                {
                    10 => 10,
                    20 => 20,
                    50 => 50,
                    100 => 100,
                    200 => 200,
                    _ => 20
                },
                BrowserColumnCount = Math.Clamp(source.BrowserColumnCount, 5, 9),
                CellPreviewLength = source.CellPreviewLength switch
                {
                    40 => 40,
                    80 => 80,
                    120 => 120,
                    _ => 80
                },
                ShowTechnicalColumnNames = source.ShowTechnicalColumnNames,
                Theme = UserThemeModes.IsValid(source.Theme) ? source.Theme : UserThemeModes.System,
                DesktopZoomPercent = DesktopZoomPreferences.Normalize(source.DesktopZoomPercent),
                DesktopAutoScale = source.DesktopAutoScale
            };
        }
    }
}
