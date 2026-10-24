// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Version-specific MaNGOS schema discovery and compatibility.

using MangosDatabaseStudio.Application.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace MangosDatabaseStudio.Schema
{
    public static class ServiceCollectionExtensions
    {
        // Add mangos schema according to the available model or service contract.
        public static IServiceCollection AddMangosSchema(this IServiceCollection services)
        {
            services.AddSingleton<IMangosSchemaCatalog, MangosSchemaCatalog>();
            return services;
        }
    }
}
