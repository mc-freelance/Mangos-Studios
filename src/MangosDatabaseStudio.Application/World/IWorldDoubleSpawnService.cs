// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;

namespace MangosDatabaseStudio.Application.World
{
    public interface IWorldDoubleSpawnService
    {
        Task<IReadOnlyList<int>> GetMapIdsAsync(
            ConnectionProfile profile,
            TableMetadata? creature,
            TableMetadata? gameObject,
            CancellationToken cancellationToken);

        Task<DoubleSpawnSearchResult> SearchAsync(
            ConnectionProfile profile,
            TableMetadata? creature,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObject,
            TableMetadata? gameObjectTemplate,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventCreatureData,
            TableMetadata? gameEventGameObject,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolTemplate,
            TableMetadata? poolPool,
            DoubleSpawnSearchRequest request,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken);
    }
}
