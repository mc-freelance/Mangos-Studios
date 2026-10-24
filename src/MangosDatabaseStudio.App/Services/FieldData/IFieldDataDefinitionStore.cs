// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    public interface IFieldDataDefinitionStore
    {
        Task<FieldDataCatalogStats> GetStatsAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<FieldDataCategoryRecord>> GetCategoriesAsync(CancellationToken cancellationToken);
        Task<FieldDataCategoryRecord?> GetCategoryAsync(long id, CancellationToken cancellationToken);
        Task<long> CreateCategoryAsync(FieldDataCategoryRecord category, CancellationToken cancellationToken);
        Task UpdateCategoryAsync(FieldDataCategoryRecord category, CancellationToken cancellationToken);
        Task DeleteCategoryAsync(long id, CancellationToken cancellationToken);

        Task<IReadOnlyList<FieldDataDefinitionRecord>> GetDefinitionsAsync(long? categoryId, string? search, CancellationToken cancellationToken);
        Task<FieldDataDefinitionRecord?> GetDefinitionAsync(long id, CancellationToken cancellationToken);
        Task<long> CreateDefinitionAsync(FieldDataDefinitionRecord definition, CancellationToken cancellationToken);
        Task UpdateDefinitionAsync(FieldDataDefinitionRecord definition, CancellationToken cancellationToken);
        Task DeleteDefinitionAsync(long id, CancellationToken cancellationToken);

        Task<IReadOnlyList<FieldDataVariantRecord>> GetVariantsAsync(long definitionId, CancellationToken cancellationToken);
        Task<FieldDataVariantRecord?> GetVariantAsync(long id, CancellationToken cancellationToken);
        Task<long> CreateVariantAsync(FieldDataVariantRecord variant, CancellationToken cancellationToken);
        Task UpdateVariantAsync(FieldDataVariantRecord variant, CancellationToken cancellationToken);
        Task DeleteVariantAsync(long id, CancellationToken cancellationToken);

        Task<long> CreateOptionAsync(FieldDataOptionRecord option, CancellationToken cancellationToken);
        Task UpdateOptionAsync(FieldDataOptionRecord option, CancellationToken cancellationToken);
        Task DeleteOptionAsync(long id, CancellationToken cancellationToken);
    }
}
