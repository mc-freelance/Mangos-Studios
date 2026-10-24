// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class TableIndexMetadata
    {
        public string Name { get; set; } = string.Empty;
        public bool IsUnique
        {
            get; set;
        }

        public string IndexType { get; set; } = string.Empty;
        public IReadOnlyList<string> Columns { get; set; } = new List<string>();

        public bool IsPrimary
        {
            get
            {
                return string.Equals(Name, "PRIMARY", System.StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
