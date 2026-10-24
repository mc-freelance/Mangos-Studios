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
    public interface IQuestViewerService
    {
        Task<PagedResult<QuestSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<QuestDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long entry,
            int relationPage,
            int relationPageSize,
            CancellationToken cancellationToken);

        QuestEditorCapabilities GetEditorCapabilities(
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables);

        Task<QuestMutationResult> AddQuestAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            QuestTemplateValues values,
            CancellationToken cancellationToken);

        Task<QuestMutationResult> UpdateQuestAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            long entry,
            QuestTemplateValues values,
            CancellationToken cancellationToken);

        Task<QuestMutationResult> DeleteQuestAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            long entry,
            CancellationToken cancellationToken);

        Task<QuestMutationResult> AddRelationAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long questEntry,
            QuestRelationValues values,
            CancellationToken cancellationToken);

        Task<QuestMutationResult> UpdateRelationAsync(
            ConnectionProfile profile,
            TableMetadata questTemplate,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObjectTemplate,
            long questEntry,
            QuestRelationValues original,
            QuestRelationValues replacement,
            CancellationToken cancellationToken);

        Task<QuestMutationResult> RemoveRelationAsync(
            ConnectionProfile profile,
            IReadOnlyDictionary<string, TableMetadata> relationTables,
            long questEntry,
            QuestRelationValues values,
            CancellationToken cancellationToken);
    }
}
