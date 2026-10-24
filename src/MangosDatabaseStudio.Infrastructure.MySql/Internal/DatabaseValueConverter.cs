// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Database access helpers, identifier escaping, and type conversion internals.

using System;
using System.Globalization;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Infrastructure.MySql.Internal
{
    internal static class DatabaseValueConverter
    {
        // Handle convert within the database value converter workflow.
        public static object? Convert(ColumnMetadata column, string? rawValue, bool useNull)
        {
            if (useNull)
            {
                if (!column.IsNullable)
                {
                    throw new InvalidOperationException("Column '" + column.Name + "' does not allow NULL values.");
                }

                return null;
            }

            string value = rawValue ?? string.Empty;
            string type = column.DataType.ToLowerInvariant();

            if (column.IsTextual || type == "json")
            {
                return value;
            }

            if (value.Length == 0 && !column.IsTextual && type != "json" &&
                type != "binary" && type != "varbinary" && !type.Contains("blob", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Column '" + column.Name + "' is empty. Select NULL to store a database NULL value.");
            }

            return type switch
            {
                "tinyint" or "smallint" or "mediumint" or "int" => column.ColumnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase)
                                        ? uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
                                        : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "bigint" => column.ColumnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase)
                                        ? ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
                                        : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "decimal" => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture),
                "float" => float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                "double" => double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                "bit" => ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "date" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces).Date,
                "datetime" or "timestamp" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces),
                "time" => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                "year" => int.Parse(value, CultureInfo.InvariantCulture),
                "binary" or "varbinary" or "blob" or "tinyblob" or "mediumblob" or "longblob" => value.Length == 0 ? Array.Empty<byte>() : System.Convert.FromBase64String(value),
                _ => value,
            };
        }
    }
}
