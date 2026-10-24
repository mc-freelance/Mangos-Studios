// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Long-running operation progress contracts.

namespace MangosDatabaseStudio.Application.Operations
{
    public sealed class OperationProgressUpdate
    {
        // Initialize the operation progress update dependencies and state.
        public OperationProgressUpdate(
            string stage,
            string detail = "",
            long? current = null,
            long? total = null,
            string unit = "")
        {
            Stage = stage ?? string.Empty;
            Detail = detail ?? string.Empty;
            Current = current;
            Total = total;
            Unit = unit ?? string.Empty;
        }

        public string Stage
        {
            get;
        }

        public string Detail
        {
            get;
        }

        public long? Current
        {
            get;
        }

        public long? Total
        {
            get;
        }

        public string Unit
        {
            get;
        }
    }
}
