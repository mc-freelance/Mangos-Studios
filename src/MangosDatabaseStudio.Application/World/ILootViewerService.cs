// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;

namespace MangosDatabaseStudio.Application.World
{
    public interface ILootViewerService
    {
        IReadOnlyList<LootTableDescriptor> GetLootTables(IReadOnlyList<TableMetadata> tables);

        Task<PagedResult<LootTemplateSummary>> SearchTemplatesAsync(
            ConnectionProfile profile,
            IReadOnlyList<TableMetadata> lootTables,
            string? tableFilter,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<LootTemplateDetail?> GetTemplateAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long entry,
            int rowPage,
            int rowPageSize,
            CancellationToken cancellationToken);


        LootEditorCapabilities GetEditorCapabilities(TableMetadata lootTable);

        Task<LootMutationResult> AddRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            LootRowValues values,
            CancellationToken cancellationToken);

        Task<LootMutationResult> UpdateRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            TableMetadata? itemTemplate,
            TableMetadata? referenceLootTable,
            LootRowValues original,
            LootRowValues replacement,
            CancellationToken cancellationToken);

        Task<LootMutationResult> DeleteRowAsync(
            ConnectionProfile profile,
            TableMetadata lootTable,
            LootRowValues original,
            CancellationToken cancellationToken);

        Task<PagedResult<LootSourceSummary>> SearchSourcesAsync(
            ConnectionProfile profile,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            IReadOnlyDictionary<string, TableMetadata> lootTables,
            string? sourceFilter,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);
    }
}
