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
    public interface IPoolViewerService
    {
        Task<PagedResult<PoolSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            string? search,
            string? population,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<PoolDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            TableMetadata? creature,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObject,
            TableMetadata? gameObjectTemplate,
            long entry,
            int memberPage,
            int memberPageSize,
            CancellationToken cancellationToken);

        PoolEditorCapabilities GetEditorCapabilities(
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool);

        Task<PoolMutationResult> AddPoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            PoolTemplateValues values,
            CancellationToken cancellationToken);

        Task<PoolMutationResult> UpdatePoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            long originalEntry,
            PoolTemplateValues replacement,
            CancellationToken cancellationToken);

        Task<PoolMutationResult> DeletePoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            long entry,
            CancellationToken cancellationToken);

        Task<PoolMutationResult> AddMemberAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            PoolMemberValues values,
            CancellationToken cancellationToken);

        Task<PoolMutationResult> UpdateMemberAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            long originalMemberId,
            PoolMemberValues replacement,
            CancellationToken cancellationToken);

        Task<PoolMutationResult> RemoveMemberAsync(
            ConnectionProfile profile,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            PoolMemberValues values,
            CancellationToken cancellationToken);
    }
}
