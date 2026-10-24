// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class TableRow
    {
        public IDictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>();
        public string KeyToken { get; set; } = string.Empty;
    }
}
