// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MaNGOS database update discovery and execution contracts.

using System;

namespace MangosDatabaseStudio.Domain.DatabaseUpdates
{
    public sealed class MangosDatabaseUpdateSource
    {
        public Guid ConnectionId
        {
            get; set;
        }

        public string DatabaseName { get; set; } = string.Empty;
        public string SourceDirectory { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
