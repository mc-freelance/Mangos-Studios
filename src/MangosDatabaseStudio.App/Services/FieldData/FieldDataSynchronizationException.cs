// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Identify Field Data startup errors without exposing encryption material.

using System;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal sealed class FieldDataSynchronizationException : Exception
    {
        // Retain the original reason so a user can diagnose a failed startup.
        public FieldDataSynchronizationException(Exception innerException)
            : base("The Field Data catalog could not be updated safely.", innerException)
        {
        }
    }
}
