// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Sql
{
    public sealed class SqlExecutionResult
    {
        public bool Succeeded
        {
            get; set;
        }

        public string Message { get; set; } = string.Empty;
        public long AffectedRows
        {
            get; set;
        }

        public double DurationMilliseconds
        {
            get; set;
        }

        public IReadOnlyList<string> Columns { get; set; } = new List<string>();
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = new List<IReadOnlyDictionary<string, object?>>();
    }
}
