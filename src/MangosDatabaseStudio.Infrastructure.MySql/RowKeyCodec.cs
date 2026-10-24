// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    public static class RowKeyCodec
    {
        // Handle encode within the row key codec workflow.
        public static string Encode(TableMetadata table, IReadOnlyDictionary<string, object?> values)
        {
            Dictionary<string, string?> keys = new(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnMetadata column in table.PrimaryKeyColumns)
            {
                values.TryGetValue(column.Name, out object? value);
                keys[column.Name] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(keys);
            return Base64UrlEncode(json);
        }

        // Handle decode within the row key codec workflow.
        public static IReadOnlyDictionary<string, string?> Decode(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("A row key token is required.", nameof(token));
            }

            byte[] json = Base64UrlDecode(token);
            Dictionary<string, string?>? keys = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            return keys ?? throw new InvalidOperationException("The row key token is invalid.");
        }

        // Handle base64url encode within the row key codec workflow.
        private static string Base64UrlEncode(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // Handle base64url decode within the row key codec workflow.
        private static byte[] Base64UrlDecode(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
            }

            return Convert.FromBase64String(normalized);
        }
    }
}
