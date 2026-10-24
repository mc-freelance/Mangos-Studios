// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System;

namespace MangosDatabaseStudio.Domain.Schema
{
    public static class ColumnEditPolicy
    {
        // Determine whether locked identifier according to the available model or service contract.
        public static bool IsLockedIdentifier(
            TableMetadata table,
            ColumnMetadata column)
        {
            if (!column.IsPrimaryKey)
            {
                return false;
            }

            // ExtendedCost participates in npc_vendor's composite key,
            // but it is vendor configuration and must remain editable.
            bool isEditableVendorCost =
                string.Equals(
                    table.Name,
                    "npc_vendor",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    column.Name,
                    "ExtendedCost",
                    StringComparison.OrdinalIgnoreCase);

            return !isEditableVendorCost;
        }
    }
}
