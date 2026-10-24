// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

namespace MangosDatabaseStudio.Domain.Sql
{
    public sealed class SqlImportResult
    {
        public bool Succeeded
        {
            get; set;
        }

        public int ExitCode
        {
            get; set;
        }

        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public double DurationMilliseconds
        {
            get; set;
        }
    }
}
