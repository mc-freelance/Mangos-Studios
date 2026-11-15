// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Apply restrictive browser protections to the private desktop host and its startup screens.

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Desktop
{
    internal static class DesktopSecurityHeaders
    {
        private const string NonceItemKey = "MangosDatabaseStudio.CspNonce";

        // Generate a new cryptographically random nonce for every request, including error responses.
        public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            context.Items[NonceItemKey] = nonce;

            // Add headers as late as possible so redirects and framework errors are also covered.
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers["Content-Security-Policy"] = BuildContentSecurityPolicy(nonce);
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                return Task.CompletedTask;
            });

            await next(context).ConfigureAwait(false);
        }

        // Render the startup page's trusted scripts and styles without allowing all inline code.
        public static string GetNonce(HttpContext context)
        {
            if (context.Items.TryGetValue(NonceItemKey, out object? value) && value is string nonce)
            {
                return nonce;
            }

            throw new InvalidOperationException("CSP nonce was not initialized for the request.");
        }

        // Allow the existing pinned Bootstrap CDN plus application assets, but block injected scripts.
        private static string BuildContentSecurityPolicy(string nonce)
        {
            string nonceSource = "'nonce-" + nonce + "'";
            return string.Join("; ", new[]
            {
                "default-src 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "frame-ancestors 'none'",
                "object-src 'none'",
                "frame-src 'none'",
                "script-src 'self' https://cdn.jsdelivr.net " + nonceSource,
                "script-src-attr 'none'",
                "style-src 'self' https://cdn.jsdelivr.net " + nonceSource,
                // Layout data and desktop zoom use element-level inline CSS custom properties.
                "style-src-attr 'unsafe-inline'",
                "img-src 'self' data: blob:",
                "font-src 'self' https://cdn.jsdelivr.net data:",
                "connect-src 'self'",
                "media-src 'self' blob:"
            });
        }
    }
}
