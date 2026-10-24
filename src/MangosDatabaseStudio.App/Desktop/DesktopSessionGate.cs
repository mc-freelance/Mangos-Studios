// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Protect the local loopback interface with the desktop session token.

using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Desktop
{
    internal sealed class DesktopSessionGate
    {
        public const string BootstrapPath = "/__studio/bootstrap";
        public const string StartupPath = "/__studio/startup";
        public const string StatusPath = "/__studio/status";
        private const string SessionCookieName = ".MangosDatabaseStudio.Desktop";

        private readonly byte[] _bootstrapSecret;
        private readonly string _sessionSecret;
        private readonly int _port;
        private int _bootstrapAvailable = 1;

        // Initialize the desktop session gate dependencies and state.
        public DesktopSessionGate(int port)
        {
            _port = port;
            _bootstrapSecret = RandomNumberGenerator.GetBytes(32);
            _sessionSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        public string BootstrapToken => Convert.ToHexString(_bootstrapSecret);

        // Determine whether loopback request as part of the local desktop lifecycle.
        public bool IsLoopbackRequest(HttpContext context)
        {
            IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress == null || !IPAddress.IsLoopback(remoteAddress))
            {
                return false;
            }

            HostString host = context.Request.Host;
            return host.Port == _port
                && (string.Equals(host.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host.Host, "[::1]", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host.Host, "::1", StringComparison.OrdinalIgnoreCase));
        }

        // Exchange the one-use bootstrap token for the desktop session cookie using a constant-time comparison.
        public bool TryBootstrap(HttpContext context)
        {
            if (!IsLoopbackRequest(context))
            {
                return false;
            }

            string supplied = context.Request.Query["token"].ToString();
            byte[] suppliedBytes;
            try
            {
                suppliedBytes = Convert.FromHexString(supplied);
            }
            catch (FormatException)
            {
                return false;
            }

            if (suppliedBytes.Length != _bootstrapSecret.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedBytes, _bootstrapSecret)
                || Interlocked.Exchange(ref _bootstrapAvailable, 0) != 1)
            {
                return false;
            }

            context.Response.Cookies.Append(SessionCookieName, _sessionSecret, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = false,
                Path = "/"
            });
            return true;
        }

        // Allow only loopback requests with the correct per-launch desktop session cookie.
        public bool IsAuthorized(HttpContext context)
        {
            if (!IsLoopbackRequest(context)
                || !context.Request.Cookies.TryGetValue(SessionCookieName, out string? supplied)
                || string.IsNullOrEmpty(supplied))
            {
                return false;
            }

            byte[] expectedBytes = Encoding.UTF8.GetBytes(_sessionSecret);
            byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
            return suppliedBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }

        // Handle reject async within the desktop session gate workflow.
        public static async Task RejectAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Not Found").ConfigureAwait(false);
        }
    }
}
