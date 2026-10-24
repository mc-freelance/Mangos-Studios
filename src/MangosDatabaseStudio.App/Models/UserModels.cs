// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.App.Services;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class UsersIndexViewModel
    {
        public IReadOnlyList<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
        public Guid? CurrentUserId
        {
            get; set;
        }

        public bool CanCreate
        {
            get; set;
        }

        public bool CanEdit
        {
            get; set;
        }

        public bool CanDelete
        {
            get; set;
        }

        public bool CanManageRoles
        {
            get; set;
        }

        public bool IsAdministrator
        {
            get; set;
        }

        public IReadOnlyCollection<Guid> ManageableUserIds { get; set; } = new List<Guid>();
    }

    public sealed class UserEditViewModel
    {
        public Guid Id
        {
            get; set;
        }

        [Required]
        [StringLength(64, MinimumLength = 3)]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Role")]
        public string Role { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password
        {
            get; set;
        }

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password confirmation does not match.")]
        [Display(Name = "Confirm password")]
        public string? ConfirmPassword
        {
            get; set;
        }

        public bool IsCreate
        {
            get; set;
        }

        public bool IsCurrentUser
        {
            get; set;
        }

        public IReadOnlyList<string> AvailableRoles { get; set; } = new List<string>();
    }

    public sealed class RolePermissionsIndexViewModel
    {
        public IReadOnlyList<RolePermissionSummaryViewModel> Roles { get; set; } = new List<RolePermissionSummaryViewModel>();
    }

    public sealed class RolePermissionSummaryViewModel
    {
        public string Role { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int GrantedPermissionCount
        {
            get; set;
        }

        public int TotalPermissionCount
        {
            get; set;
        }

        public bool IsAdministrator
        {
            get; set;
        }
    }

    public sealed class RolePermissionsEditViewModel
    {
        [Required]
        public string Role { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsAdministrator
        {
            get; set;
        }

        public IReadOnlyList<PermissionGroupViewModel> Groups { get; set; } = new List<PermissionGroupViewModel>();
        public List<string> Permissions { get; set; } = new List<string>();
    }

    public sealed class PermissionGroupViewModel
    {
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<PermissionOptionViewModel> Permissions { get; set; } = new List<PermissionOptionViewModel>();
    }

    public sealed class PermissionOptionViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Selected
        {
            get; set;
        }

        public bool AdministratorOnly
        {
            get; set;
        }

        public IReadOnlyList<string> Dependencies { get; set; } = new List<string>();
    }
}
