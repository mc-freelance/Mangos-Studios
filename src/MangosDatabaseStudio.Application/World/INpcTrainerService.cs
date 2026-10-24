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
    public interface INpcTrainerService
    {
        Task<PagedResult<NpcTrainerSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcTrainer,
            TableMetadata? npcTrainerTemplate,
            ulong trainerFlagMask,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<NpcTrainerDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcTrainer,
            TableMetadata? npcTrainerTemplate,
            TableMetadata? spellTemplate,
            ulong trainerFlagMask,
            long entry,
            int spellPage,
            int spellPageSize,
            CancellationToken cancellationToken);

        Task<PagedResult<NpcTrainerSpellOption>> SearchSpellsAsync(
            ConnectionProfile profile,
            TableMetadata spellTemplate,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        NpcTrainerEditorCapabilities GetEditorCapabilities(TableMetadata npcTrainer);

        Task<NpcTrainerMutationResult> AddSpellsAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            IReadOnlyList<NpcTrainerSpellValues> spells,
            CancellationToken cancellationToken);

        Task<NpcTrainerMutationResult> UpdateSpellAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            NpcTrainerSpellValues original,
            NpcTrainerSpellValues replacement,
            CancellationToken cancellationToken);

        Task<NpcTrainerMutationResult> DeleteSpellAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            NpcTrainerSpellValues original,
            CancellationToken cancellationToken);
    }
}
