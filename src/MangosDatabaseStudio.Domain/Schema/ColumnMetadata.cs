// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

namespace MangosDatabaseStudio.Domain.Schema
{
    public sealed class ColumnMetadata
    {
        public string Name { get; set; } = string.Empty;
        public int OrdinalPosition
        {
            get; set;
        }

        public string DataType { get; set; } = string.Empty;
        public string ColumnType { get; set; } = string.Empty;
        public string? DefaultValue
        {
            get; set;
        }

        public bool IsNullable
        {
            get; set;
        }

        public bool IsPrimaryKey
        {
            get; set;
        }

        public int PrimaryKeyOrder
        {
            get; set;
        }

        public bool IsAutoIncrement
        {
            get; set;
        }

        public bool IsGenerated
        {
            get; set;
        }

        public string Extra { get; set; } = string.Empty;
        public string CharacterSetName { get; set; } = string.Empty;
        public string CollationName { get; set; } = string.Empty;
        public long? CharacterMaximumLength
        {
            get; set;
        }

        public int? NumericPrecision
        {
            get; set;
        }

        public int? NumericScale
        {
            get; set;
        }

        public string Comment { get; set; } = string.Empty;

        public bool IsTextual
        {
            get
            {
                return DataType == "char" || DataType == "varchar" || DataType.Contains("text") ||
                       DataType == "enum" || DataType == "set";
            }
        }

        public bool IsNumeric
        {
            get
            {
                return DataType == "tinyint" || DataType == "smallint" || DataType == "mediumint" ||
                       DataType == "int" || DataType == "bigint" || DataType == "decimal" ||
                       DataType == "float" || DataType == "double" || DataType == "bit";
            }
        }
    }
}
