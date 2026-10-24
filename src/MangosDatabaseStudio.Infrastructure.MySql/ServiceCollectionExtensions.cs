// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.DatabaseUpdates;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Application.Sql;
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    public static class ServiceCollectionExtensions
    {
        // Add mangos my sql infrastructure using schema-aware database operations.
        public static IServiceCollection AddMangosMySqlInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            MySqlOptions options = new();
            configuration.GetSection(MySqlOptions.SectionName).Bind(options);
            services.AddSingleton(options);
            services.AddSingleton<MySqlConnectionFactory>();
            services.AddScoped<IConnectionTester, ConnectionTester>();
            services.AddScoped<IMySqlSchemaService, MySqlSchemaService>();
            services.AddScoped<IMySqlSchemaEditorService, MySqlSchemaEditorService>();
            services.AddScoped<IGenericTableService, GenericTableService>();
            services.AddScoped<INpcVendorService, NpcVendorService>();
            services.AddScoped<INpcTrainerService, NpcTrainerService>();
            services.AddScoped<IPoolViewerService, PoolViewerService>();
            services.AddScoped<ILootViewerService, LootViewerService>();
            services.AddScoped<IGossipViewerService, GossipViewerService>();
            services.AddScoped<IEventViewerService, EventViewerService>();
            services.AddScoped<IQuestViewerService, QuestViewerService>();
            services.AddScoped<IWorldDoubleSpawnService, WorldDoubleSpawnService>();
            services.AddScoped<ISqlExecutionService, SqlExecutionService>();
            services.AddScoped<IChangeRevertExecutionService, ChangeRevertExecutionService>();
            services.AddScoped<ISqlImportService, SqlImportService>();
            services.AddScoped<ISqlExportService, SqlExportService>();
            services.AddScoped<IMangosDatabaseUpdateService, MangosDatabaseUpdateService>();
            return services;
        }
    }
}
