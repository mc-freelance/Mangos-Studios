// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;
using System.Linq;

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class TableMetadata
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public string Collation { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public long ApproximateRows
        {
            get; set;
        }

        public long DataLength
        {
            get; set;
        }

        public long IndexLength
        {
            get; set;
        }

        public IReadOnlyList<ColumnMetadata> Columns { get; set; } = new List<ColumnMetadata>();
        public IReadOnlyList<TableIndexMetadata> Indexes { get; set; } = new List<TableIndexMetadata>();

        public IReadOnlyList<ColumnMetadata> PrimaryKeyColumns
        {
            get
            {
                return Columns.Where(column => column.IsPrimaryKey).OrderBy(column => column.PrimaryKeyOrder).ToList();
            }
        }

        public bool CanEdit
        {
            get
            {
                return PrimaryKeyColumns.Count > 0;
            }
        }
    }
}
