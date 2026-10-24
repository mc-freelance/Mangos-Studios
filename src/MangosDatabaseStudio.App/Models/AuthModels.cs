// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System.ComponentModel.DataAnnotations;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe
        {
            get; set;
        }

        public string? ReturnUrl
        {
            get; set;
        }
    }

    public sealed class SetupViewModel
    {
        [Required]
        [StringLength(64, MinimumLength = 3)]
        public string Username { get; set; } = "admin";

        [Required]
        [DataType(DataType.Password)]
        [StringLength(128, MinimumLength = 12)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
