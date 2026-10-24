// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System;
using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = new List<T>();
        public long TotalCount
        {
            get; set;
        }

        public int Page
        {
            get; set;
        }

        public int PageSize
        {
            get; set;
        }

        public int TotalPages
        {
            get
            {
                return PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
            }
        }
    }
}
