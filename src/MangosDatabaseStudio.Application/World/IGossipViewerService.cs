// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;

namespace MangosDatabaseStudio.Application.World
{
    public interface IGossipViewerService
    {
        Task<PagedResult<GossipMenuSummary>> SearchMenusAsync(ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, string? search, int page, int pageSize, CancellationToken cancellationToken);
        Task<PagedResult<GossipSourceSummary>> SearchSourcesAsync(ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, string? sourceFilter, string? search, int page, int pageSize, CancellationToken cancellationToken);
        Task<PagedResult<GossipCreatureOption>> SearchEligibleCreaturesAsync(ConnectionProfile profile, TableMetadata creatureTemplate, ulong gossipFlagMask, string? search, int page, int pageSize, CancellationToken cancellationToken);
        Task<GossipMenuDetail?> GetMenuAsync(ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata? gossipMenuOption, TableMetadata? npcText, TableMetadata? creatureTemplate, TableMetadata? gameObjectTemplate, long entry, int textPage, int textPageSize, int optionPage, int optionPageSize, CancellationToken cancellationToken);
        GossipEditorCapabilities GetEditorCapabilities(TableMetadata? gossipMenu, TableMetadata? gossipMenuOption);
        Task<GossipMutationResult> AddMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? npcText, GossipMenuRowValues values, CancellationToken cancellationToken);
        Task<GossipMutationResult> UpdateMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? npcText, GossipMenuRowValues original, GossipMenuRowValues replacement, CancellationToken cancellationToken);
        Task<GossipMutationResult> DeleteMenuRowAsync(ConnectionProfile profile, TableMetadata gossipMenu, TableMetadata? gossipMenuOption, GossipMenuRowValues original, CancellationToken cancellationToken);
        Task<GossipMutationResult> AddOptionAsync(ConnectionProfile profile, TableMetadata gossipMenuOption, TableMetadata? gossipMenu, GossipOptionValues values, CancellationToken cancellationToken);
        Task<GossipMutationResult> UpdateOptionAsync(ConnectionProfile profile, TableMetadata gossipMenuOption, TableMetadata? gossipMenu, GossipOptionValues original, GossipOptionValues replacement, CancellationToken cancellationToken);
        Task<GossipMutationResult> DeleteOptionAsync(ConnectionProfile profile, TableMetadata? gossipMenu, TableMetadata gossipMenuOption, GossipOptionValues original, CancellationToken cancellationToken);
        Task<GossipMutationResult> AddCreatureSourceAsync(ConnectionProfile profile, TableMetadata creatureTemplate, long creatureEntry, long menuId, ulong gossipFlagMask, CancellationToken cancellationToken);
    }
}
