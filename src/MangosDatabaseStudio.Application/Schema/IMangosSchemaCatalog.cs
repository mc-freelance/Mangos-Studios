// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Application.Schema
{
    public interface IMangosSchemaCatalog
    {
        string Name
        {
            get;
        }

        string Version
        {
            get;
        }

        IReadOnlyList<string> StructuralFingerprintTables
        {
            get;
        }

        MangosSchemaProfile Detect(IEnumerable<TableMetadata> tables);
        IReadOnlyList<MangosTableCategory> Categorize(IEnumerable<string> tables);
    }

    public sealed class MangosSchemaProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public MangosProjectGeneration Generation { get; set; } = MangosProjectGeneration.Unknown;
        public bool IsRecognized
        {
            get; set;
        }
    }

    public sealed class MangosTableCategory
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public IReadOnlyList<string> Tables { get; set; } = new List<string>();
    }
}
