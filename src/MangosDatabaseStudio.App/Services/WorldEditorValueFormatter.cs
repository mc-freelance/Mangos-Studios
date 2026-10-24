// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.App.Services
{
    public static class WorldEditorValueFormatter
    {
        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
        private static readonly Regex HtmlDatePattern = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex HtmlDateTimePattern = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\\.[0-9]{1,6})?)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Format for editor according to the available model or service contract.
        public static string FormatForEditor(ColumnMetadata column, object? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string text = value as string ?? Convert.ToString(value, InvariantCulture) ?? string.Empty;
            string dataType = column.DataType.ToLowerInvariant();

            // Text values must be preserved exactly. In particular, the literal text
            // "NULL" is not the same as a database NULL value.
            if (column.IsTextual || dataType == "json" || IsBinary(column))
            {
                return text;
            }

            return dataType switch
            {
                "date" => NormalizeDate(text),
                "datetime" or "timestamp" => NormalizeDateTime(text),
                "time" => text.Trim(),
                _ => text.Trim(),
            };
        }

        // Read input type according to the available model or service contract.
        public static string GetInputType(ColumnMetadata column, string inputValue)
        {
            if (column.IsNumeric)
            {
                return "number";
            }

            string dataType = column.DataType.ToLowerInvariant();
            if (dataType == "date")
            {
                return inputValue.Length == 0 || IsValidHtmlDate(inputValue) ? "date" : "text";
            }

            if (dataType == "datetime" || dataType == "timestamp")
            {
                return inputValue.Length == 0 || IsValidHtmlDateTime(inputValue) ? "datetime-local" : "text";
            }

            // MySQL TIME is a duration and can be negative or exceed 24 hours. An
            // HTML time input cannot safely represent the complete MySQL range.
            if (dataType == "time")
            {
                return "text";
            }

            return "text";
        }

        // Determine whether valid html date according to the available model or service contract.
        public static bool IsValidHtmlDate(string value)
        {
            if (!HtmlDatePattern.IsMatch(value) || value.StartsWith("0000-", StringComparison.Ordinal))
            {
                return false;
            }

            return DateTime.TryParseExact(value, "yyyy-MM-dd", InvariantCulture, DateTimeStyles.None, out _);
        }

        // Determine whether valid html date time according to the available model or service contract.
        public static bool IsValidHtmlDateTime(string value)
        {
            if (!HtmlDateTimePattern.IsMatch(value) || value.StartsWith("0000-", StringComparison.Ordinal))
            {
                return false;
            }

            return DateTime.TryParse(value, InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
        }

        // Normalize date according to the available model or service contract.
        private static string NormalizeDate(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.StartsWith("0000-", StringComparison.Ordinal))
            {
                return trimmed.Length >= 10 ? trimmed[..10] : trimmed;
            }

            if (DateTime.TryParse(trimmed, InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed))
            {
                return parsed.ToString("yyyy-MM-dd", InvariantCulture);
            }

            return trimmed;
        }

        // Normalize date time according to the available model or service contract.
        private static string NormalizeDateTime(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.StartsWith("0000-", StringComparison.Ordinal))
            {
                return trimmed.Replace(' ', 'T');
            }

            if (!DateTime.TryParse(trimmed, InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed))
            {
                return trimmed;
            }

            string result = parsed.ToString("yyyy-MM-dd'T'HH:mm:ss", InvariantCulture);
            int microseconds = (int)((parsed.Ticks % TimeSpan.TicksPerSecond) / 10);
            if (microseconds > 0)
            {
                result += "." + microseconds.ToString("D6", InvariantCulture).TrimEnd('0');
            }

            return result;
        }

        // Determine whether binary according to the available model or service contract.
        private static bool IsBinary(ColumnMetadata column)
        {
            return column.DataType.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
                   column.DataType.Contains("binary", StringComparison.OrdinalIgnoreCase);
        }
    }
}
