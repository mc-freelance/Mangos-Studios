// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// SQL execution, import/export, and change-reversal contracts.

using System;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Operations;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Sql;

namespace MangosDatabaseStudio.Application.Sql
{
    public interface ISqlImportService
    {
        Task<SqlImportAnalysis> AnalyzeAsync(string token, string originalFileName, string sqlFilePath, IProgress<OperationProgressUpdate>? progress, CancellationToken cancellationToken);
        Task<SqlImportResult> ExecuteAsync(ConnectionProfile profile, string database, string sqlFilePath, IProgress<OperationProgressUpdate>? progress, CancellationToken cancellationToken);
    }
}
