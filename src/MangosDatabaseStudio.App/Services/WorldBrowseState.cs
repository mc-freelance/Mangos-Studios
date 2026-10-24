// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class WorldBrowseState
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string Search { get; set; } = string.Empty;
        public string Sort { get; set; } = string.Empty;
        public bool Descending
        {
            get; set;
        }

        // Normalize a stored value for stable comparisons and display.
        public WorldBrowseState Normalize()
        {
            return new WorldBrowseState
            {
                Page = Math.Max(1, Page),
                PageSize = NormalizePageSize(PageSize),
                Search = Search ?? string.Empty,
                Sort = Sort ?? string.Empty,
                Descending = Descending
            };
        }

        // Clamp requested page sizes to the configured browsing limit.
        private static int NormalizePageSize(int pageSize)
        {
            return pageSize switch
            {
                10 => 10,
                20 => 20,
                50 => 50,
                100 => 100,
                200 => 200,
                _ => 20
            };
        }
    }

    public static class WorldBrowseStateSession
    {
        private const string SessionKeyPrefix = "WorldBrowseState:";
        private const string CapturedReturnKeyPrefix = "WorldBrowseCapturedReturn:";
        private const string PendingReturnKeyPrefix = "WorldBrowsePendingReturn:";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Handle read within the world browse state workflow.
        public static WorldBrowseState? Read(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            string key = CreateSessionKey(userId, connectionId, database, table);
            return ReadState(session, key, removeAfterRead: false);
        }

        // Handle write within the world browse state workflow.
        public static void Write(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table,
            WorldBrowseState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            string key = CreateSessionKey(userId, connectionId, database, table);
            WriteState(session, key, state);
        }

        // Handle capture return state within the world browse state workflow.
        public static string CaptureReturnState(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            WorldBrowseState state =
                Read(session, userId, connectionId, database, table) ??
                new WorldBrowseState();

            string token = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(24));

            string key = CreateScopedSessionKey(
                CapturedReturnKeyPrefix,
                userId,
                connectionId,
                database,
                table,
                token);

            WriteState(session, key, state);
            return token;
        }

        // Handle queue captured return within the world browse state workflow.
        public static bool QueueCapturedReturn(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table,
            string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string capturedKey = CreateScopedSessionKey(
                CapturedReturnKeyPrefix,
                userId,
                connectionId,
                database,
                table,
                token);

            WorldBrowseState? state = ReadState(
                session,
                capturedKey,
                removeAfterRead: true);

            if (state == null)
            {
                return false;
            }

            string pendingKey = CreatePendingReturnKey(
                userId,
                connectionId,
                database,
                table);

            WriteState(session, pendingKey, state);
            return true;
        }

        // Handle queue current return within the world browse state workflow.
        public static bool QueueCurrentReturn(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            WorldBrowseState? state = Read(
                session,
                userId,
                connectionId,
                database,
                table);

            if (state == null)
            {
                return false;
            }

            string pendingKey = CreatePendingReturnKey(
                userId,
                connectionId,
                database,
                table);

            WriteState(session, pendingKey, state);
            return true;
        }

        // Handle consume pending return within the world browse state workflow.
        public static WorldBrowseState? ConsumePendingReturn(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            string key = CreatePendingReturnKey(
                userId,
                connectionId,
                database,
                table);

            return ReadState(session, key, removeAfterRead: true);
        }

        // Handle clear pending return within the world browse state workflow.
        public static void ClearPendingReturn(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            session.Remove(
                CreatePendingReturnKey(
                    userId,
                    connectionId,
                    database,
                    table));
        }

        // Create session key according to the available model or service contract.
        public static string CreateSessionKey(
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            return CreateScopedSessionKey(
                SessionKeyPrefix,
                userId,
                connectionId,
                database,
                table,
                string.Empty);
        }

        // Create pending return key according to the available model or service contract.
        private static string CreatePendingReturnKey(
            Guid userId,
            Guid connectionId,
            string database,
            string table)
        {
            return CreateScopedSessionKey(
                PendingReturnKeyPrefix,
                userId,
                connectionId,
                database,
                table,
                string.Empty);
        }

        // Create scoped session key according to the available model or service contract.
        private static string CreateScopedSessionKey(
            string prefix,
            Guid userId,
            Guid connectionId,
            string database,
            string table,
            string scope)
        {
            string identity = string.Join(
                "|",
                userId.ToString("D"),
                connectionId.ToString("D"),
                database ?? string.Empty,
                table ?? string.Empty,
                scope ?? string.Empty);

            byte[] hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(identity));

            return prefix + Convert.ToHexString(hash);
        }

        // Write state according to the available model or service contract.
        private static void WriteState(
            ISession session,
            string key,
            WorldBrowseState state)
        {
            string json = JsonSerializer.Serialize(
                state.Normalize(),
                SerializerOptions);

            session.SetString(key, json);
        }

        // Read state according to the available model or service contract.
        private static WorldBrowseState? ReadState(
            ISession session,
            string key,
            bool removeAfterRead)
        {
            string? json = session.GetString(key);

            if (removeAfterRead)
            {
                session.Remove(key);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer
                    .Deserialize<WorldBrowseState>(json, SerializerOptions)
                    ?.Normalize();
            }
            catch (JsonException)
            {
                session.Remove(key);
                return null;
            }
        }
    }
}
