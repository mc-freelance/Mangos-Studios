// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using MangosDatabaseStudio.Domain.Security;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MangosDatabaseStudio.App.Services
{
    public static class ApplicationPermissionClaims
    {
        public const string PermissionClaimType = "mangos:permission";

        // Create principal according to the available model or service contract.
        public static ClaimsPrincipal CreatePrincipal(ApplicationUser user, IEnumerable<string> permissions)
        {
            ClaimsIdentity identity = new(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")));
            identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
            IReadOnlyCollection<string> normalizedPermissions = AppPermissions.NormalizeForRole(user.Role, permissions);
            foreach (string permission in normalizedPermissions)
            {
                identity.AddClaim(new Claim(PermissionClaimType, permission));
            }

            return new ClaimsPrincipal(identity);
        }

        // Handle matches within the application permission claims workflow.
        public static bool Matches(ClaimsPrincipal? principal, ApplicationUser user, IEnumerable<string> permissions)
        {
            if (principal == null || !string.Equals(principal.Identity?.Name, user.Username, StringComparison.Ordinal))
            {
                return false;
            }

            string currentRole = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            if (!string.Equals(currentRole, user.Role, StringComparison.Ordinal))
            {
                return false;
            }

            HashSet<string> expected = new(AppPermissions.NormalizeForRole(user.Role, permissions), StringComparer.Ordinal);
            HashSet<string> current = principal.FindAll(PermissionClaimType)
                .Select(claim => claim.Value)
                .Where(AppPermissions.IsValid)
                .ToHashSet(StringComparer.Ordinal);
            return expected.SetEquals(current);
        }

        // Determine whether permission according to the available model or service contract.
        public static bool HasPermission(this ClaimsPrincipal principal, string permission)
        {
            return principal.HasClaim(PermissionClaimType, permission);
        }

        // Determine whether all permissions according to the available model or service contract.
        public static bool HasAllPermissions(this ClaimsPrincipal principal, IEnumerable<string> permissions)
        {
            return permissions
                .Distinct(StringComparer.Ordinal)
                .All(permission => AppPermissions.IsValid(permission) && principal.HasPermission(permission));
        }
    }
}
