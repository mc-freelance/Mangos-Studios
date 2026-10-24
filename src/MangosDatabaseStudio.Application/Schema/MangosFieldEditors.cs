// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;

namespace MangosDatabaseStudio.Application.Schema
{
    public enum MangosFieldEditorKind
    {
        Select = 0,
        BitMask = 1,
        MultiSelect = 2
    }

    public enum MangosFieldSelectionMode
    {
        Single = 0,
        Multiple = 1
    }

    public enum MangosFieldValueHandling
    {
        Direct = 0,
        BitwiseOr = 1,
        Additive = 2
    }

    public sealed class MangosFieldOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class MangosFieldEditorDefinition
    {
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public MangosFieldEditorKind Kind
        {
            get; set;
        }

        public MangosFieldSelectionMode SelectionMode { get; set; } = MangosFieldSelectionMode.Single;
        public MangosFieldValueHandling ValueHandling { get; set; } = MangosFieldValueHandling.Direct;
        public bool PreserveUnknownValues { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public IReadOnlyList<MangosFieldOption> Options { get; set; } = new List<MangosFieldOption>();
    }
}
