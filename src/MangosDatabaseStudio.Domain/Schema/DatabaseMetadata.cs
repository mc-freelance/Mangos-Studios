// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class DatabaseMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string CharacterSet { get; set; } = string.Empty;
        public string Collation { get; set; } = string.Empty;
    }
}
