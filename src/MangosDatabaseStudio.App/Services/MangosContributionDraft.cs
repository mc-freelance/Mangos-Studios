// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class MangosContributionDraft
    {
        public bool Enabled
        {
            get; set;
        }

        public string OldVersion { get; set; } = string.Empty;
        public string OldStructure { get; set; } = string.Empty;
        public string OldContent { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public string NewStructure { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public List<string> Statements { get; set; } = new List<string>();

        // Normalize a stored value for stable comparisons and display.
        public MangosContributionDraft Normalize()
        {
            return new MangosContributionDraft
            {
                Enabled = Enabled,
                OldVersion = NormalizeText(OldVersion, 64),
                OldStructure = NormalizeText(OldStructure, 64),
                OldContent = NormalizeText(OldContent, 64),
                NewVersion = NormalizeText(NewVersion, 64),
                NewStructure = NormalizeText(NewStructure, 64),
                NewContent = NormalizeText(NewContent, 64),
                Description = NormalizeText(Description, 30),
                Comment = NormalizeText(Comment, 150),
                Statements = (Statements ?? new List<string>())
                    .Where(statement => !string.IsNullOrWhiteSpace(statement))
                    .Select(statement => statement.Trim())
                    .Take(MangosContributionDraftSession.MaximumStatementCount)
                    .ToList()
            };
        }

        // Normalize text according to the available model or service contract.
        private static string NormalizeText(string? value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength];
        }
    }

    public static class MangosContributionDraftSession
    {
        private const string SessionKeyPrefix = "MangosContributionDraft:";
        internal const int MaximumStatementCount = 250;
        private const int MaximumStatementLength = 1_000_000;
        private const int MaximumDraftLength = 2_000_000;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Handle read within the mangos contribution draft workflow.
        public static MangosContributionDraft Read(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database)
        {
            string key = CreateSessionKey(userId, connectionId, database);
            string? json = session.GetString(key);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new MangosContributionDraft();
            }

            try
            {
                return JsonSerializer.Deserialize<MangosContributionDraft>(json, SerializerOptions)?.Normalize()
                       ?? new MangosContributionDraft();
            }
            catch (JsonException)
            {
                session.Remove(key);
                return new MangosContributionDraft();
            }
        }

        // Persist settings according to the available model or service contract.
        public static MangosContributionDraft SaveSettings(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            bool enabled,
            string? oldVersion,
            string? oldStructure,
            string? oldContent,
            string? newVersion,
            string? newStructure,
            string? newContent,
            string? description,
            string? comment)
        {
            MangosContributionDraft draft = Read(session, userId, connectionId, database);
            draft.Enabled = enabled;
            draft.OldVersion = oldVersion ?? string.Empty;
            draft.OldStructure = oldStructure ?? string.Empty;
            draft.OldContent = oldContent ?? string.Empty;
            draft.NewVersion = newVersion ?? string.Empty;
            draft.NewStructure = newStructure ?? string.Empty;
            draft.NewContent = newContent ?? string.Empty;
            draft.Description = description ?? string.Empty;
            draft.Comment = comment ?? string.Empty;
            return Write(session, userId, connectionId, database, draft);
        }

        // Attempt to append statement according to the available model or service contract.
        public static bool TryAppendStatement(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            string? sql,
            out MangosContributionDraft draft)
        {
            draft = Read(session, userId, connectionId, database);
            string statement = (sql ?? string.Empty).Trim();
            if (!draft.Enabled || string.IsNullOrWhiteSpace(statement))
            {
                return true;
            }

            if (statement.Length > MaximumStatementLength ||
                draft.Statements.Count >= MaximumStatementCount ||
                draft.Statements.Sum(item => item.Length) + statement.Length > MaximumDraftLength)
            {
                return false;
            }

            draft.Statements.Add(statement);
            draft = Write(session, userId, connectionId, database, draft);
            return true;
        }

        // Handle clear within the mangos contribution draft workflow.
        public static void Clear(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database)
        {
            session.Remove(CreateSessionKey(userId, connectionId, database));
        }

        // Create session key according to the available model or service contract.
        public static string CreateSessionKey(Guid userId, Guid connectionId, string database)
        {
            string identity = string.Join(
                "|",
                userId.ToString("D"),
                connectionId.ToString("D"),
                database ?? string.Empty);

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return SessionKeyPrefix + Convert.ToHexString(hash);
        }

        // Handle write within the mangos contribution draft workflow.
        private static MangosContributionDraft Write(
            ISession session,
            Guid userId,
            Guid connectionId,
            string database,
            MangosContributionDraft draft)
        {
            MangosContributionDraft normalized = draft.Normalize();
            string json = JsonSerializer.Serialize(normalized, SerializerOptions);
            session.SetString(CreateSessionKey(userId, connectionId, database), json);
            return normalized;
        }
    }
}
