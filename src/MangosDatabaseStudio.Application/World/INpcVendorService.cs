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
    public interface INpcVendorService
    {
        Task<PagedResult<NpcVendorSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcVendor,
            ulong vendorFlagMask,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<NpcVendorDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            ulong vendorFlagMask,
            long entry,
            int itemPage,
            int itemPageSize,
            CancellationToken cancellationToken);

        Task<PagedResult<NpcVendorItemOption>> SearchItemsAsync(
            ConnectionProfile profile,
            TableMetadata itemTemplate,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        NpcVendorEditorCapabilities GetEditorCapabilities(TableMetadata npcVendor);

        Task<NpcVendorMutationResult> AddItemsAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            long entry,
            IReadOnlyList<NpcVendorItemValues> items,
            CancellationToken cancellationToken);

        Task<NpcVendorMutationResult> UpdateItemAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            TableMetadata itemTemplate,
            long entry,
            NpcVendorItemValues original,
            NpcVendorItemValues replacement,
            CancellationToken cancellationToken);

        Task<NpcVendorMutationResult> DeleteItemAsync(
            ConnectionProfile profile,
            TableMetadata npcVendor,
            long entry,
            NpcVendorItemValues original,
            CancellationToken cancellationToken);
    }
}
