// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Sql
{
    public sealed class SqlImportAnalysis
    {
        public string Token { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSize
        {
            get; set;
        }

        public string Sha256 { get; set; } = string.Empty;
        public int ApproximateStatementCount
        {
            get; set;
        }

        public bool ContainsDestructiveStatements
        {
            get; set;
        }

        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
    }
}
