// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MangosDatabaseStudio.App.Services;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class ProfileViewModel
    {
        public Guid Id
        {
            get; set;
        }

        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        [StringLength(254)]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Avatar source")]
        public string AvatarMode { get; set; } = string.Empty;

        [Display(Name = "Avatar image")]
        public IFormFile? AvatarFile
        {
            get; set;
        }

        public bool HasUploadedAvatar
        {
            get; set;
        }

        public string? AvatarUrl
        {
            get; set;
        }

        public string Initials { get; set; } = string.Empty;
        public DesktopDeviceIdentityViewModel? DeviceIdentity
        {
            get; set;
        }
    }

    public sealed class DesktopDeviceIdentityViewModel
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

    public sealed class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 12)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The new password confirmation does not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class DesktopZoomUpdateViewModel
    {
        [Range(DesktopZoomPreferences.MinimumPercent, DesktopZoomPreferences.MaximumPercent)]
        public int ZoomPercent { get; set; } = DesktopZoomPreferences.DefaultPercent;

        public bool AutoScale { get; set; } = true;
    }

    public sealed class UserSettingsViewModel
    {
        [Display(Name = "Default rows per page")]
        public int DefaultPageSize { get; set; } = 20;

        [Range(5, 9)]
        [Display(Name = "Columns shown in record browser")]
        public int BrowserColumnCount { get; set; } = 9;

        [Display(Name = "Maximum cell preview length")]
        public int CellPreviewLength { get; set; } = 80;

        [Display(Name = "Show technical database column names")]
        public bool ShowTechnicalColumnNames { get; set; } = true;

        [Required]
        [Display(Name = "Appearance")]
        public string Theme { get; set; } = string.Empty;

        [Range(DesktopZoomPreferences.MinimumPercent, DesktopZoomPreferences.MaximumPercent)]
        [Display(Name = "Desktop zoom")]
        public int DesktopZoomPercent { get; set; } = DesktopZoomPreferences.DefaultPercent;

        [Display(Name = "Automatically fit smaller screens")]
        public bool DesktopAutoScale { get; set; } = true;
    }
}
