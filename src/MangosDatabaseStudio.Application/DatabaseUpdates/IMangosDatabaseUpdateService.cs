// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MaNGOS database update discovery and execution contracts.

using System;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.DatabaseUpdates;

namespace MangosDatabaseStudio.Application.DatabaseUpdates
{
    public interface IMangosDatabaseUpdateService
    {
        Task<MangosDatabaseUpdatePlan> BuildPlanAsync(
            ConnectionProfile profile,
            string database,
            string sourceDirectory,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken);

        Task<MangosDatabaseUpdateBatchResult> ApplyAsync(
            ConnectionProfile profile,
            string database,
            string sourceDirectory,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken);
    }
}
