// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MySQL/MariaDB queries and schema-adaptive data operations.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.World;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;
using MangosDatabaseStudio.Infrastructure.MySql.Internal;
using MySqlConnector;

namespace MangosDatabaseStudio.Infrastructure.MySql
{
    internal sealed class PoolViewerService : IPoolViewerService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the pool viewer service dependencies and state.
        public PoolViewerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Search for  using schema-aware database operations.
        public async Task<PagedResult<PoolSummary>> SearchAsync(
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
            CancellationToken cancellationToken)
        {
            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;
            string normalizedPopulation = PoolPopulationFilters.Normalize(population);

            string templateTable = Qualify(poolTemplate);
            string entry = Column("pt", templateSchema.Entry);
            string description = templateSchema.Description == null
                ? "''"
                : "COALESCE(" + Column("pt", templateSchema.Description) + ", '')";
            string maxLimit = templateSchema.MaxLimit == null
                ? "NULL"
                : Column("pt", templateSchema.MaxLimit);
            List<string> predicates = new();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                predicates.Add("(CAST(" + entry + " AS CHAR) LIKE @search OR " + description + " LIKE @search)");
            }

            string hasMembers = BuildPoolHasMembersPredicate(
                poolCreature,
                poolCreatureTemplate,
                poolGameObject,
                poolGameObjectTemplate,
                poolPool,
                "pt",
                templateSchema.Entry);
            if (string.Equals(normalizedPopulation, PoolPopulationFilters.Populated, StringComparison.OrdinalIgnoreCase))
            {
                predicates.Add("(" + hasMembers + ")");
            }
            else if (string.Equals(normalizedPopulation, PoolPopulationFilters.Empty, StringComparison.OrdinalIgnoreCase))
            {
                predicates.Add("NOT (" + hasMembers + ")");
            }

            string where = predicates.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", predicates);

            string countSql = "SELECT COUNT(*) FROM " + templateTable + " AS pt" + where + ";";
            string dataSql =
                "SELECT " + entry + " AS PoolEntry, " +
                maxLimit + " AS PoolMaxLimit, " +
                description + " AS PoolDescription, " +
                BuildMemberCountSelection(poolCreature, "pc", "pt", templateSchema.Entry, false) + " AS CreatureSpawnCount, " +
                BuildMemberCountSelection(poolCreatureTemplate, "pct", "pt", templateSchema.Entry, false) + " AS CreatureTemplateCount, " +
                BuildMemberCountSelection(poolGameObject, "pg", "pt", templateSchema.Entry, false) + " AS GameObjectSpawnCount, " +
                BuildMemberCountSelection(poolGameObjectTemplate, "pgt", "pt", templateSchema.Entry, false) + " AS GameObjectTemplateCount, " +
                BuildMemberCountSelection(poolPool, "pp", "pt", templateSchema.Entry, true) + " AS ChildPoolCount " +
                "FROM " + templateTable + " AS pt" + where +
                " ORDER BY " + entry + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                AddSearchParameter(command, normalizedSearch);
                object? value = await command.ExecuteScalarAsync(cancellationToken);
                total = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }

            List<PoolSummary> pools = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                AddSearchParameter(command, normalizedSearch);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    pools.Add(ReadPoolSummary(reader));
                }
            }

            return new PagedResult<PoolSummary>
            {
                Items = pools,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Read  using schema-aware database operations.
        public async Task<PoolDetail?> GetAsync(
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
            CancellationToken cancellationToken)
        {
            PoolSummary? summary = await GetPoolSummaryAsync(
                profile,
                poolTemplate,
                poolCreature,
                poolCreatureTemplate,
                poolGameObject,
                poolGameObjectTemplate,
                poolPool,
                entry,
                cancellationToken);
            if (summary == null)
            {
                return null;
            }

            List<PoolMember> members = new();
            if (poolCreature != null)
            {
                await TryLoadMembersAsync(profile, poolCreature, entry, PoolMemberSources.CreatureSpawn, "Creature spawn", creature, creatureTemplate, members, cancellationToken);
            }

            if (poolCreatureTemplate != null)
            {
                await TryLoadMembersAsync(profile, poolCreatureTemplate, entry, PoolMemberSources.CreatureTemplate, "Creature template", null, creatureTemplate, members, cancellationToken);
            }

            if (poolGameObject != null)
            {
                await TryLoadMembersAsync(profile, poolGameObject, entry, PoolMemberSources.GameObjectSpawn, "GameObject spawn", gameObject, gameObjectTemplate, members, cancellationToken);
            }

            if (poolGameObjectTemplate != null)
            {
                await TryLoadMembersAsync(profile, poolGameObjectTemplate, entry, PoolMemberSources.GameObjectTemplate, "GameObject template", null, gameObjectTemplate, members, cancellationToken);
            }

            if (poolPool != null)
            {
                try
                {
                    await LoadChildPoolsAsync(profile, poolTemplate, poolPool, entry, members, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // Keep pool_template browsing available when an unfamiliar optional
                    // pool_pool schema cannot be interpreted safely.
                }
            }

            members = members
                .OrderBy(member => MemberSourceOrder(member.Source))
                .ThenBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.MemberId)
                .ToList();

            memberPage = Math.Max(1, memberPage);
            memberPageSize = Math.Clamp(memberPageSize, 10, _options.MaximumBrowsePageSize);
            int totalPages = members.Count == 0 ? 0 : (int)Math.Ceiling(members.Count / (double)memberPageSize);
            if (totalPages == 0)
            {
                memberPage = 1;
            }
            else if (memberPage > totalPages)
            {
                memberPage = totalPages;
            }

            int offset = checked((memberPage - 1) * memberPageSize);
            IReadOnlyList<PoolParentMembership> parents = Array.Empty<PoolParentMembership>();
            if (poolPool != null)
            {
                try
                {
                    parents = await LoadParentPoolsAsync(profile, poolTemplate, poolPool, entry, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // Optional parent relationships should not make the entire viewer unavailable.
                }
            }

            return new PoolDetail
            {
                Pool = summary,
                Members = new PagedResult<PoolMember>
                {
                    Items = members.Skip(offset).Take(memberPageSize).ToArray(),
                    TotalCount = members.Count,
                    Page = memberPage,
                    PageSize = memberPageSize
                },
                ParentPools = parents
            };
        }

        // Report which editor operations the detected database schema supports.
        public PoolEditorCapabilities GetEditorCapabilities(
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool)
        {
            PoolTemplateSchema template = PoolTemplateSchema.Create(poolTemplate);
            List<PoolMemberSourceCapability> sources = new();
            AddMemberSourceCapability(sources, poolCreature, PoolMemberSources.CreatureSpawn, "Creature spawn", "Spawn GUID", false);
            AddMemberSourceCapability(sources, poolCreatureTemplate, PoolMemberSources.CreatureTemplate, "Creature template", "Creature entry", false);
            AddMemberSourceCapability(sources, poolGameObject, PoolMemberSources.GameObjectSpawn, "GameObject spawn", "Spawn GUID", false);
            AddMemberSourceCapability(sources, poolGameObjectTemplate, PoolMemberSources.GameObjectTemplate, "GameObject template", "GameObject entry", false);
            AddMemberSourceCapability(sources, poolPool, PoolMemberSources.ChildPool, "Child pool", "Child pool entry", true);

            return new PoolEditorCapabilities
            {
                EntryColumn = template.Entry.Name,
                MaxLimitColumn = template.MaxLimit?.Name,
                DescriptionColumn = template.Description?.Name,
                MemberSources = sources
            };
        }

        // Add pool using schema-aware database operations.
        public async Task<PoolMutationResult> AddPoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            PoolTemplateValues values,
            CancellationToken cancellationToken)
        {
            if (values.Entry <= 0)
            {
                throw new InvalidOperationException("Pool entry must be greater than zero.");
            }

            if (values.MaxLimit < 0)
            {
                throw new InvalidOperationException("Max active cannot be negative.");
            }

            PoolTemplateSchema schema = PoolTemplateSchema.Create(poolTemplate);
            List<ColumnMetadata> columns = new() { schema.Entry };
            List<string> parameterNames = new() { "@entry" };
            if (schema.MaxLimit != null)
            {
                columns.Add(schema.MaxLimit);
                parameterNames.Add("@maxLimit");
            }

            if (schema.Description != null)
            {
                columns.Add(schema.Description);
                parameterNames.Add("@description");
            }

            string sql = "INSERT INTO " + Qualify(poolTemplate) + " (" +
                         string.Join(", ", columns.Select(column => MySqlIdentifier.Quote(column.Name))) +
                         ") VALUES (" + string.Join(", ", parameterNames) + ");";

            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", values.Entry);
            if (schema.MaxLimit != null)
            {
                command.Parameters.AddWithValue("@maxLimit", values.MaxLimit ?? 1);
            }

            if (schema.Description != null)
            {
                command.Parameters.AddWithValue("@description", values.Description ?? string.Empty);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            string preview = BuildPoolInsertPreview(poolTemplate, schema, values);
            string revert = "DELETE FROM " + MySqlIdentifier.Quote(poolTemplate.Name) +
                            " WHERE " + MySqlIdentifier.Quote(schema.Entry.Name) + " = " + values.Entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = poolTemplate.Name,
                SqlPreview = preview,
                RevertSqlPreview = revert
            };
        }

        // Update pool using schema-aware database operations.
        public async Task<PoolMutationResult> UpdatePoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            long originalEntry,
            PoolTemplateValues replacement,
            CancellationToken cancellationToken)
        {
            if (originalEntry <= 0)
            {
                throw new InvalidOperationException("A valid pool entry is required.");
            }

            if (replacement.MaxLimit < 0)
            {
                throw new InvalidOperationException("Max active cannot be negative.");
            }

            PoolTemplateSchema schema = PoolTemplateSchema.Create(poolTemplate);
            PoolTemplateValues? original = await GetPoolTemplateValuesAsync(profile, poolTemplate, schema, originalEntry, cancellationToken) ?? throw new InvalidOperationException("The selected pool no longer exists.");
            List<string> setters = new();
            if (schema.MaxLimit != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.MaxLimit.Name) + " = @maxLimit");
            }

            if (schema.Description != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Description.Name) + " = @description");
            }

            if (setters.Count == 0)
            {
                throw new InvalidOperationException("This pool_template schema does not expose editable max-limit or description columns.");
            }

            string sql = "UPDATE " + Qualify(poolTemplate) + " SET " + string.Join(", ", setters) +
                         " WHERE " + MySqlIdentifier.Quote(schema.Entry.Name) + " = @entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", originalEntry);
            if (schema.MaxLimit != null)
            {
                command.Parameters.AddWithValue("@maxLimit", replacement.MaxLimit ?? 1);
            }

            if (schema.Description != null)
            {
                command.Parameters.AddWithValue("@description", replacement.Description ?? string.Empty);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            string preview = BuildPoolUpdatePreview(poolTemplate, schema, originalEntry, replacement);
            string revert = BuildPoolUpdatePreview(poolTemplate, schema, originalEntry, original);
            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = poolTemplate.Name,
                SqlPreview = preview,
                RevertSqlPreview = revert
            };
        }

        // Delete pool using schema-aware database operations.
        public async Task<PoolMutationResult> DeletePoolAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            long entry,
            CancellationToken cancellationToken)
        {
            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            PoolTemplateValues? original = await GetPoolTemplateValuesAsync(profile, poolTemplate, templateSchema, entry, cancellationToken) ?? throw new InvalidOperationException("The selected pool no longer exists.");
            long references = 0;
            references += await CountPoolReferencesAsync(profile, poolCreature, entry, false, false, cancellationToken);
            references += await CountPoolReferencesAsync(profile, poolCreatureTemplate, entry, false, false, cancellationToken);
            references += await CountPoolReferencesAsync(profile, poolGameObject, entry, false, false, cancellationToken);
            references += await CountPoolReferencesAsync(profile, poolGameObjectTemplate, entry, false, false, cancellationToken);
            references += await CountPoolReferencesAsync(profile, poolPool, entry, true, false, cancellationToken);
            references += await CountPoolReferencesAsync(profile, poolPool, entry, true, true, cancellationToken);
            if (references > 0)
            {
                throw new InvalidOperationException(
                    "Pool " + entry.ToString(CultureInfo.InvariantCulture) + " still has " +
                    references.ToString("N0", CultureInfo.InvariantCulture) +
                    " membership or parent relationship(s). Remove those relationships before deleting the pool template.");
            }

            string sql = "DELETE FROM " + Qualify(poolTemplate) + " WHERE " + MySqlIdentifier.Quote(templateSchema.Entry.Name) + " = @entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = poolTemplate.Name,
                SqlPreview = "DELETE FROM " + MySqlIdentifier.Quote(poolTemplate.Name) + " WHERE " + MySqlIdentifier.Quote(templateSchema.Entry.Name) + " = " + entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;",
                RevertSqlPreview = BuildPoolInsertPreview(poolTemplate, templateSchema, original)
            };
        }

        // Add member using schema-aware database operations.
        public async Task<PoolMutationResult> AddMemberAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            PoolMemberValues values,
            CancellationToken cancellationToken)
        {
            if (poolEntry <= 0 || values.MemberId <= 0)
            {
                throw new InvalidOperationException("Pool and member entries must be greater than zero.");
            }

            if (values.Chance.HasValue && (values.Chance.Value < 0d || values.Chance.Value > 100d))
            {
                throw new InvalidOperationException("Pool member chance must be between 0 and 100 percent.");
            }

            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            if (await GetPoolTemplateValuesAsync(profile, poolTemplate, templateSchema, poolEntry, cancellationToken) == null)
            {
                throw new InvalidOperationException("The selected pool no longer exists.");
            }

            TableMetadata sourceTable = ResolveSourceTable(availablePoolTables, values.Source);
            PoolMembershipSchema schema = PoolMembershipSchema.Create(sourceTable, values.Source);
            if (await GetMemberValuesAsync(profile, sourceTable, schema, poolEntry, values.MemberId, cancellationToken) != null)
            {
                throw new InvalidOperationException("That member is already linked to the selected pool through " + sourceTable.Name + ".");
            }

            if (schema.IsPoolRelationship)
            {
                if (values.MemberId == poolEntry)
                {
                    throw new InvalidOperationException("A pool cannot be a child of itself.");
                }

                if (await GetPoolTemplateValuesAsync(profile, poolTemplate, templateSchema, values.MemberId, cancellationToken) == null)
                {
                    throw new InvalidOperationException("Child pool " + values.MemberId.ToString(CultureInfo.InvariantCulture) + " does not exist in pool_template.");
                }

                if (await WouldCreatePoolCycleAsync(profile, sourceTable, schema, values.MemberId, poolEntry, cancellationToken))
                {
                    throw new InvalidOperationException("That child-pool relationship would create a circular pool hierarchy.");
                }
            }

            List<ColumnMetadata> columns = new() { schema.Member, schema.Pool };
            List<string> parameters = new() { "@member", "@pool" };
            if (schema.Chance != null)
            {
                columns.Add(schema.Chance);
                parameters.Add("@chance");
            }

            if (schema.Description != null)
            {
                columns.Add(schema.Description);
                parameters.Add("@description");
            }

            string sql = "INSERT INTO " + Qualify(sourceTable) + " (" +
                         string.Join(", ", columns.Select(column => MySqlIdentifier.Quote(column.Name))) +
                         ") VALUES (" + string.Join(", ", parameters) + ");";
            await using MySqlConnection connection = _connectionFactory.Create(profile, sourceTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@member", values.MemberId);
            command.Parameters.AddWithValue("@pool", poolEntry);
            if (schema.Chance != null)
            {
                command.Parameters.AddWithValue("@chance", values.Chance ?? 0d);
            }

            if (schema.Description != null)
            {
                command.Parameters.AddWithValue("@description", values.Description ?? string.Empty);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            string preview = BuildMemberInsertPreview(sourceTable, schema, poolEntry, values);
            string revert = BuildMemberDeletePreview(sourceTable, schema, poolEntry, values.MemberId);
            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = sourceTable.Name,
                SqlPreview = preview,
                RevertSqlPreview = revert
            };
        }

        // Update member using schema-aware database operations.
        public async Task<PoolMutationResult> UpdateMemberAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            long originalMemberId,
            PoolMemberValues replacement,
            CancellationToken cancellationToken)
        {
            if (poolEntry <= 0 || originalMemberId <= 0 || replacement.MemberId <= 0)
            {
                throw new InvalidOperationException("Pool and member entries must be greater than zero.");
            }

            if (replacement.Chance.HasValue && (replacement.Chance.Value < 0d || replacement.Chance.Value > 100d))
            {
                throw new InvalidOperationException("Pool member chance must be between 0 and 100 percent.");
            }

            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            if (await GetPoolTemplateValuesAsync(profile, poolTemplate, templateSchema, poolEntry, cancellationToken) == null)
            {
                throw new InvalidOperationException("The selected pool no longer exists.");
            }

            TableMetadata sourceTable = ResolveSourceTable(availablePoolTables, replacement.Source);
            PoolMembershipSchema schema = PoolMembershipSchema.Create(sourceTable, replacement.Source);
            PoolMemberValues? original = await GetMemberValuesAsync(profile, sourceTable, schema, poolEntry, originalMemberId, cancellationToken) ?? throw new InvalidOperationException("The selected pool membership no longer exists.");
            if (replacement.MemberId != originalMemberId &&
                await GetMemberValuesAsync(profile, sourceTable, schema, poolEntry, replacement.MemberId, cancellationToken) != null)
            {
                throw new InvalidOperationException("That member is already linked to the selected pool through " + sourceTable.Name + ".");
            }

            if (schema.IsPoolRelationship)
            {
                if (replacement.MemberId == poolEntry)
                {
                    throw new InvalidOperationException("A pool cannot be a child of itself.");
                }

                if (await GetPoolTemplateValuesAsync(profile, poolTemplate, templateSchema, replacement.MemberId, cancellationToken) == null)
                {
                    throw new InvalidOperationException("Child pool " + replacement.MemberId.ToString(CultureInfo.InvariantCulture) + " does not exist in pool_template.");
                }

                if (await WouldCreatePoolCycleAsync(profile, sourceTable, schema, replacement.MemberId, poolEntry, cancellationToken))
                {
                    throw new InvalidOperationException("That child-pool relationship would create a circular pool hierarchy.");
                }
            }

            List<string> setters = new();
            if (replacement.MemberId != originalMemberId)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Member.Name) + " = @replacementMember");
            }

            if (schema.Chance != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Chance.Name) + " = @chance");
            }

            if (schema.Description != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Description.Name) + " = @description");
            }

            if (setters.Count == 0)
            {
                throw new InvalidOperationException("This pool membership schema does not expose editable member fields.");
            }

            string sql = "UPDATE " + Qualify(sourceTable) + " SET " + string.Join(", ", setters) + " WHERE " +
                         MySqlIdentifier.Quote(schema.Member.Name) + " = @originalMember AND " +
                         MySqlIdentifier.Quote(schema.Pool.Name) + " = @pool LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, sourceTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@originalMember", originalMemberId);
            command.Parameters.AddWithValue("@pool", poolEntry);
            if (replacement.MemberId != originalMemberId)
            {
                command.Parameters.AddWithValue("@replacementMember", replacement.MemberId);
            }

            if (schema.Chance != null)
            {
                command.Parameters.AddWithValue("@chance", replacement.Chance ?? 0d);
            }

            if (schema.Description != null)
            {
                command.Parameters.AddWithValue("@description", replacement.Description ?? string.Empty);
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = sourceTable.Name,
                SqlPreview = BuildMemberUpdatePreview(sourceTable, schema, poolEntry, originalMemberId, replacement),
                RevertSqlPreview = BuildMemberUpdatePreview(sourceTable, schema, poolEntry, replacement.MemberId, original)
            };
        }

        // Remove member using schema-aware database operations.
        public async Task<PoolMutationResult> RemoveMemberAsync(
            ConnectionProfile profile,
            IReadOnlyDictionary<string, TableMetadata> availablePoolTables,
            long poolEntry,
            PoolMemberValues values,
            CancellationToken cancellationToken)
        {
            TableMetadata sourceTable = ResolveSourceTable(availablePoolTables, values.Source);
            PoolMembershipSchema schema = PoolMembershipSchema.Create(sourceTable, values.Source);
            PoolMemberValues? original = await GetMemberValuesAsync(profile, sourceTable, schema, poolEntry, values.MemberId, cancellationToken) ?? throw new InvalidOperationException("The selected pool membership no longer exists.");
            string sql = "DELETE FROM " + Qualify(sourceTable) + " WHERE " +
                         MySqlIdentifier.Quote(schema.Member.Name) + " = @member AND " +
                         MySqlIdentifier.Quote(schema.Pool.Name) + " = @pool LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, sourceTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@member", values.MemberId);
            command.Parameters.AddWithValue("@pool", poolEntry);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken);

            return new PoolMutationResult
            {
                AffectedRows = affected,
                ObjectName = sourceTable.Name,
                SqlPreview = BuildMemberDeletePreview(sourceTable, schema, poolEntry, values.MemberId),
                RevertSqlPreview = BuildMemberInsertPreview(sourceTable, schema, poolEntry, original)
            };
        }

        // Read pool summary using schema-aware database operations.
        private async Task<PoolSummary?> GetPoolSummaryAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            long entryValue,
            CancellationToken cancellationToken)
        {
            PoolTemplateSchema schema = PoolTemplateSchema.Create(poolTemplate);
            string entry = Column("pt", schema.Entry);
            string description = schema.Description == null ? "''" : "COALESCE(" + Column("pt", schema.Description) + ", '')";
            string maxLimit = schema.MaxLimit == null ? "NULL" : Column("pt", schema.MaxLimit);
            string sql =
                "SELECT " + entry + " AS PoolEntry, " + maxLimit + " AS PoolMaxLimit, " + description + " AS PoolDescription, " +
                BuildMemberCountSelection(poolCreature, "pc", "pt", schema.Entry, false) + " AS CreatureSpawnCount, " +
                BuildMemberCountSelection(poolCreatureTemplate, "pct", "pt", schema.Entry, false) + " AS CreatureTemplateCount, " +
                BuildMemberCountSelection(poolGameObject, "pg", "pt", schema.Entry, false) + " AS GameObjectSpawnCount, " +
                BuildMemberCountSelection(poolGameObjectTemplate, "pgt", "pt", schema.Entry, false) + " AS GameObjectTemplateCount, " +
                BuildMemberCountSelection(poolPool, "pp", "pt", schema.Entry, true) + " AS ChildPoolCount " +
                "FROM " + Qualify(poolTemplate) + " AS pt WHERE " + entry + " = @entry LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entryValue);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadPoolSummary(reader) : null;
        }

        // Attempt to load members using schema-aware database operations.
        private async Task TryLoadMembersAsync(
            ConnectionProfile profile,
            TableMetadata poolTable,
            long poolEntry,
            string source,
            string sourceLabel,
            TableMetadata? spawnTable,
            TableMetadata? templateTable,
            ICollection<PoolMember> destination,
            CancellationToken cancellationToken)
        {
            try
            {
                await LoadMembersAsync(profile, poolTable, poolEntry, source, sourceLabel, spawnTable, templateTable, destination, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Unknown future membership schemas are omitted instead of breaking
                // the pool_template directory or unrelated supported memberships.
            }
        }

        // Load members using schema-aware database operations.
        private async Task LoadMembersAsync(
            ConnectionProfile profile,
            TableMetadata poolTable,
            long poolEntry,
            string source,
            string sourceLabel,
            TableMetadata? spawnTable,
            TableMetadata? templateTable,
            ICollection<PoolMember> destination,
            CancellationToken cancellationToken)
        {
            PoolMembershipSchema schema = PoolMembershipSchema.Create(poolTable, source);
            string member = Column("p", schema.Member);
            string pool = Column("p", schema.Pool);
            string chance = schema.Chance == null ? "NULL" : Column("p", schema.Chance);
            string description = schema.Description == null ? "''" : "COALESCE(" + Column("p", schema.Description) + ", '')";

            List<string> joins = new();
            string entityEntrySelection = "NULL";
            string nameSelection = "''";

            if (spawnTable != null)
            {
                ColumnMetadata? spawnGuid = Find(spawnTable, "guid");
                ColumnMetadata? spawnEntry = Find(spawnTable, "id", "entry");
                if (spawnGuid != null)
                {
                    joins.Add(" LEFT JOIN " + Qualify(spawnTable) + " AS s ON " + Column("s", spawnGuid) + " = " + member);
                    if (spawnEntry != null)
                    {
                        entityEntrySelection = Column("s", spawnEntry);
                        if (templateTable != null)
                        {
                            ColumnMetadata? templateEntry = Find(templateTable, "entry", "id");
                            ColumnMetadata? templateName = Find(templateTable, "name");
                            if (templateEntry != null && templateName != null)
                            {
                                joins.Add(" LEFT JOIN " + Qualify(templateTable) + " AS t ON " + Column("t", templateEntry) + " = " + Column("s", spawnEntry));
                                nameSelection = "COALESCE(" + Column("t", templateName) + ", '')";
                            }
                        }
                    }
                }
            }
            else if (templateTable != null)
            {
                ColumnMetadata? templateEntry = Find(templateTable, "entry", "id");
                ColumnMetadata? templateName = Find(templateTable, "name");
                if (templateEntry != null)
                {
                    joins.Add(" LEFT JOIN " + Qualify(templateTable) + " AS t ON " + Column("t", templateEntry) + " = " + member);
                    entityEntrySelection = Column("t", templateEntry);
                    if (templateName != null)
                    {
                        nameSelection = "COALESCE(" + Column("t", templateName) + ", '')";
                    }
                }
            }

            string sql = "SELECT " + member + " AS MemberId, " + entityEntrySelection + " AS EntityEntry, " +
                         nameSelection + " AS MemberName, " + chance + " AS MembershipChance, " + description + " AS MembershipDescription " +
                         "FROM " + Qualify(poolTable) + " AS p" + string.Concat(joins) +
                         " WHERE " + pool + " = @pool;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@pool", poolEntry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long memberId = GetInt64(reader, "MemberId");
                long? entityEntry = GetNullableInt64(reader, "EntityEntry");
                string name = GetString(reader, "MemberName");
                bool isResolved = entityEntry.HasValue;
                destination.Add(new PoolMember
                {
                    Source = source,
                    SourceLabel = sourceLabel,
                    MemberId = memberId,
                    EntityEntry = entityEntry,
                    ResolvedName = name,
                    IsResolved = isResolved,
                    DisplayName = !string.IsNullOrWhiteSpace(name)
                        ? name
                        : isResolved && entityEntry.HasValue
                            ? FallbackResolvedName(source, entityEntry.Value)
                            : FallbackMemberName(source, memberId),
                    Chance = GetNullableDouble(reader, "MembershipChance"),
                    Description = GetString(reader, "MembershipDescription")
                });
            }
        }

        // Load child pools using schema-aware database operations.
        private async Task LoadChildPoolsAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata poolPool,
            long motherPoolEntry,
            List<PoolMember> destination,
            CancellationToken cancellationToken)
        {
            PoolMembershipSchema schema = PoolMembershipSchema.Create(poolPool, PoolMemberSources.ChildPool);
            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            string description = templateSchema.Description == null ? "''" : "COALESCE(" + Column("child", templateSchema.Description) + ", '')";
            string chance = schema.Chance == null ? "NULL" : Column("p", schema.Chance);
            string membershipDescription = schema.Description == null ? "''" : "COALESCE(" + Column("p", schema.Description) + ", '')";
            string sql =
                "SELECT " + Column("p", schema.Member) + " AS MemberId, " + Column("child", templateSchema.Entry) + " AS EntityEntry, " + description + " AS MemberName, " +
                chance + " AS MembershipChance, " + membershipDescription + " AS MembershipDescription " +
                "FROM " + Qualify(poolPool) + " AS p LEFT JOIN " + Qualify(poolTemplate) + " AS child ON " +
                Column("child", templateSchema.Entry) + " = " + Column("p", schema.Member) +
                " WHERE " + Column("p", schema.Pool) + " = @pool;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, poolPool.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@pool", motherPoolEntry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long memberId = GetInt64(reader, "MemberId");
                long? entityEntry = GetNullableInt64(reader, "EntityEntry");
                string name = GetString(reader, "MemberName");
                bool isResolved = entityEntry.HasValue;
                destination.Add(new PoolMember
                {
                    Source = PoolMemberSources.ChildPool,
                    SourceLabel = "Child pool",
                    MemberId = memberId,
                    EntityEntry = entityEntry,
                    ResolvedName = name,
                    IsResolved = isResolved,
                    DisplayName = !string.IsNullOrWhiteSpace(name)
                        ? name
                        : isResolved
                            ? "Pool " + entityEntry.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)
                            : FallbackMemberName(PoolMemberSources.ChildPool, memberId),
                    Chance = GetNullableDouble(reader, "MembershipChance"),
                    Description = GetString(reader, "MembershipDescription")
                });
            }
        }

        // Load parent pools using schema-aware database operations.
        private async Task<IReadOnlyList<PoolParentMembership>> LoadParentPoolsAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            TableMetadata poolPool,
            long childPoolEntry,
            CancellationToken cancellationToken)
        {
            PoolMembershipSchema schema = PoolMembershipSchema.Create(poolPool, PoolMemberSources.ChildPool);
            PoolTemplateSchema templateSchema = PoolTemplateSchema.Create(poolTemplate);
            string description = templateSchema.Description == null ? "''" : "COALESCE(" + Column("parent", templateSchema.Description) + ", '')";
            string chance = schema.Chance == null ? "NULL" : Column("p", schema.Chance);
            string membershipDescription = schema.Description == null ? "''" : "COALESCE(" + Column("p", schema.Description) + ", '')";
            string sql =
                "SELECT " + Column("p", schema.Pool) + " AS ParentPoolEntry, " + description + " AS ParentDescription, " +
                chance + " AS MembershipChance, " + membershipDescription + " AS MembershipDescription " +
                "FROM " + Qualify(poolPool) + " AS p LEFT JOIN " + Qualify(poolTemplate) + " AS parent ON " +
                Column("parent", templateSchema.Entry) + " = " + Column("p", schema.Pool) +
                " WHERE " + Column("p", schema.Member) + " = @child ORDER BY " + Column("p", schema.Pool) + ";";

            List<PoolParentMembership> parents = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile, poolPool.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@child", childPoolEntry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                parents.Add(new PoolParentMembership
                {
                    PoolEntry = GetInt64(reader, "ParentPoolEntry"),
                    Description = GetString(reader, "ParentDescription"),
                    Chance = GetNullableDouble(reader, "MembershipChance"),
                    MembershipDescription = GetString(reader, "MembershipDescription")
                });
            }

            return parents;
        }

        // Read pool template values using schema-aware database operations.
        private async Task<PoolTemplateValues?> GetPoolTemplateValuesAsync(
            ConnectionProfile profile,
            TableMetadata poolTemplate,
            PoolTemplateSchema schema,
            long entry,
            CancellationToken cancellationToken)
        {
            string max = schema.MaxLimit == null ? "NULL" : MySqlIdentifier.Quote(schema.MaxLimit.Name);
            string description = schema.Description == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(schema.Description.Name) + ", '')";
            string sql = "SELECT " + MySqlIdentifier.Quote(schema.Entry.Name) + " AS PoolEntry, " + max + " AS PoolMaxLimit, " +
                         description + " AS PoolDescription FROM " + Qualify(poolTemplate) + " WHERE " +
                         MySqlIdentifier.Quote(schema.Entry.Name) + " = @entry LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, poolTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", entry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new PoolTemplateValues
            {
                Entry = GetInt64(reader, "PoolEntry"),
                MaxLimit = GetNullableInt32(reader, "PoolMaxLimit"),
                Description = GetString(reader, "PoolDescription")
            };
        }

        // Handle would create pool cycle async within the pool viewer service workflow.
        private async Task<bool> WouldCreatePoolCycleAsync(
            ConnectionProfile profile,
            TableMetadata poolPool,
            PoolMembershipSchema schema,
            long childPoolEntry,
            long motherPoolEntry,
            CancellationToken cancellationToken)
        {
            string sql = "SELECT " + MySqlIdentifier.Quote(schema.Member.Name) + " AS ChildPoolEntry, " +
                         MySqlIdentifier.Quote(schema.Pool.Name) + " AS MotherPoolEntry FROM " + Qualify(poolPool) + ";";
            Dictionary<long, List<long>> parentsByChild = new();
            await using MySqlConnection connection = _connectionFactory.Create(profile, poolPool.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long child = GetInt64(reader, "ChildPoolEntry");
                long parent = GetInt64(reader, "MotherPoolEntry");
                if (!parentsByChild.TryGetValue(child, out List<long>? parents))
                {
                    parents = new List<long>();
                    parentsByChild.Add(child, parents);
                }

                parents.Add(parent);
            }

            Queue<long> pending = new();
            HashSet<long> visited = new();
            pending.Enqueue(motherPoolEntry);
            while (pending.Count > 0)
            {
                long current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (current == childPoolEntry)
                {
                    return true;
                }

                if (!parentsByChild.TryGetValue(current, out List<long>? parents))
                {
                    continue;
                }

                foreach (long parent in parents)
                {
                    pending.Enqueue(parent);
                }
            }

            return false;
        }

        // Read member values using schema-aware database operations.
        private async Task<PoolMemberValues?> GetMemberValuesAsync(
            ConnectionProfile profile,
            TableMetadata sourceTable,
            PoolMembershipSchema schema,
            long poolEntry,
            long memberId,
            CancellationToken cancellationToken)
        {
            string chance = schema.Chance == null ? "NULL" : MySqlIdentifier.Quote(schema.Chance.Name);
            string description = schema.Description == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(schema.Description.Name) + ", '')";
            string sql = "SELECT " + chance + " AS MembershipChance, " + description + " AS MembershipDescription FROM " +
                         Qualify(sourceTable) + " WHERE " + MySqlIdentifier.Quote(schema.Member.Name) + " = @member AND " +
                         MySqlIdentifier.Quote(schema.Pool.Name) + " = @pool LIMIT 1;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, sourceTable.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@member", memberId);
            command.Parameters.AddWithValue("@pool", poolEntry);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new PoolMemberValues
            {
                Source = sourceTable.Name,
                MemberId = memberId,
                Chance = GetNullableDouble(reader, "MembershipChance"),
                Description = GetString(reader, "MembershipDescription")
            };
        }

        // Count pool references using schema-aware database operations.
        private async Task<long> CountPoolReferencesAsync(
            ConnectionProfile profile,
            TableMetadata? table,
            long poolEntry,
            bool poolRelationship,
            bool countAsChild,
            CancellationToken cancellationToken)
        {
            if (table == null)
            {
                return 0;
            }

            PoolMembershipSchema schema = PoolMembershipSchema.Create(
                table,
                poolRelationship ? PoolMemberSources.ChildPool : table.Name);

            ColumnMetadata column = countAsChild ? schema.Member : schema.Pool;
            string sql = "SELECT COUNT(*) FROM " + Qualify(table) + " WHERE " + MySqlIdentifier.Quote(column.Name) + " = @entry;";
            await using MySqlConnection connection = _connectionFactory.Create(profile, table.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@entry", poolEntry);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        // Build member count selection using schema-aware database operations.
        private static string BuildMemberCountSelection(
            TableMetadata? table,
            string alias,
            string templateAlias,
            ColumnMetadata templateEntry,
            bool poolRelationship)
        {
            if (table == null)
            {
                return "0";
            }

            PoolMembershipSchema schema;
            try
            {
                schema = PoolMembershipSchema.Create(table, poolRelationship ? PoolMemberSources.ChildPool : table.Name);
            }
            catch (InvalidOperationException)
            {
                return "0";
            }

            return "(SELECT COUNT(*) FROM " + Qualify(table) + " AS " + alias + " WHERE " +
                   Column(alias, schema.Pool) + " = " + Column(templateAlias, templateEntry) + ")";
        }

        // Build pool has members predicate using schema-aware database operations.
        private static string BuildPoolHasMembersPredicate(
            TableMetadata? poolCreature,
            TableMetadata? poolCreatureTemplate,
            TableMetadata? poolGameObject,
            TableMetadata? poolGameObjectTemplate,
            TableMetadata? poolPool,
            string templateAlias,
            ColumnMetadata templateEntry)
        {
            List<string> predicates = new();
            AddPoolMembershipExistsPredicate(predicates, poolCreature, "pcf", templateAlias, templateEntry, false);
            AddPoolMembershipExistsPredicate(predicates, poolCreatureTemplate, "pctf", templateAlias, templateEntry, false);
            AddPoolMembershipExistsPredicate(predicates, poolGameObject, "pgf", templateAlias, templateEntry, false);
            AddPoolMembershipExistsPredicate(predicates, poolGameObjectTemplate, "pgtf", templateAlias, templateEntry, false);
            AddPoolMembershipExistsPredicate(predicates, poolPool, "ppf", templateAlias, templateEntry, true);
            return predicates.Count == 0 ? "0 = 1" : string.Join(" OR ", predicates);
        }

        // Add pool membership exists predicate using schema-aware database operations.
        private static void AddPoolMembershipExistsPredicate(
            List<string> destination,
            TableMetadata? table,
            string alias,
            string templateAlias,
            ColumnMetadata templateEntry,
            bool poolRelationship)
        {
            if (table == null)
            {
                return;
            }

            try
            {
                PoolMembershipSchema schema = PoolMembershipSchema.Create(table, poolRelationship ? PoolMemberSources.ChildPool : table.Name);
                destination.Add("EXISTS (SELECT 1 FROM " + Qualify(table) + " AS " + alias + " WHERE " +
                                Column(alias, schema.Pool) + " = " + Column(templateAlias, templateEntry) + " LIMIT 1)");
            }
            catch (InvalidOperationException)
            {
                // Unknown optional pool table schemas are ignored just as they are for counts.
            }
        }

        // Add member source capability using schema-aware database operations.
        private static void AddMemberSourceCapability(
            List<PoolMemberSourceCapability> destination,
            TableMetadata? table,
            string source,
            string label,
            string memberLabel,
            bool poolRelationship)
        {
            if (table == null)
            {
                return;
            }

            try
            {
                PoolMembershipSchema schema = PoolMembershipSchema.Create(table, poolRelationship ? PoolMemberSources.ChildPool : source);
                destination.Add(new PoolMemberSourceCapability
                {
                    Source = source,
                    Label = label,
                    MemberLabel = memberLabel,
                    HasChance = schema.Chance != null,
                    HasDescription = schema.Description != null
                });
            }
            catch (InvalidOperationException)
            {
                // Unknown future schema: keep the viewer available but omit unsafe mutation source.
            }
        }

        // Resolve source table using schema-aware database operations.
        private static TableMetadata ResolveSourceTable(IReadOnlyDictionary<string, TableMetadata> tables, string? source)
        {
            string normalized = (source ?? string.Empty).Trim();
            if (!IsKnownSource(normalized) || !tables.TryGetValue(normalized, out TableMetadata? table))
            {
                throw new InvalidOperationException("The selected pool membership source is not available in this database.");
            }

            return table;
        }

        // Determine whether known source using schema-aware database operations.
        private static bool IsKnownSource(string source)
        {
            return string.Equals(source, PoolMemberSources.CreatureSpawn, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, PoolMemberSources.CreatureTemplate, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, PoolMemberSources.GameObjectSpawn, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, PoolMemberSources.GameObjectTemplate, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, PoolMemberSources.ChildPool, StringComparison.OrdinalIgnoreCase);
        }

        // Build pool insert preview using schema-aware database operations.
        private static string BuildPoolInsertPreview(TableMetadata table, PoolTemplateSchema schema, PoolTemplateValues values)
        {
            List<string> columns = new() { MySqlIdentifier.Quote(schema.Entry.Name) };
            List<string> literals = new() { values.Entry.ToString(CultureInfo.InvariantCulture) };
            if (schema.MaxLimit != null)
            {
                columns.Add(MySqlIdentifier.Quote(schema.MaxLimit.Name));
                literals.Add((values.MaxLimit ?? 1).ToString(CultureInfo.InvariantCulture));
            }

            if (schema.Description != null)
            {
                columns.Add(MySqlIdentifier.Quote(schema.Description.Name));
                literals.Add(QuoteSql(values.Description));
            }

            return "INSERT INTO " + MySqlIdentifier.Quote(table.Name) + " (" + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", literals) + ");";
        }

        // Build pool update preview using schema-aware database operations.
        private static string BuildPoolUpdatePreview(TableMetadata table, PoolTemplateSchema schema, long entry, PoolTemplateValues values)
        {
            List<string> setters = new();
            if (schema.MaxLimit != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.MaxLimit.Name) + " = " + (values.MaxLimit ?? 1).ToString(CultureInfo.InvariantCulture));
            }

            if (schema.Description != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Description.Name) + " = " + QuoteSql(values.Description));
            }

            return "UPDATE " + MySqlIdentifier.Quote(table.Name) + " SET " + string.Join(", ", setters) +
                   " WHERE " + MySqlIdentifier.Quote(schema.Entry.Name) + " = " + entry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Build member insert preview using schema-aware database operations.
        private static string BuildMemberInsertPreview(TableMetadata table, PoolMembershipSchema schema, long poolEntry, PoolMemberValues values)
        {
            List<string> columns = new() { MySqlIdentifier.Quote(schema.Member.Name), MySqlIdentifier.Quote(schema.Pool.Name) };
            List<string> literals = new()
            {
                values.MemberId.ToString(CultureInfo.InvariantCulture),
                poolEntry.ToString(CultureInfo.InvariantCulture)
            };

            if (schema.Chance != null)
            {
                columns.Add(MySqlIdentifier.Quote(schema.Chance.Name));
                literals.Add((values.Chance ?? 0d).ToString("R", CultureInfo.InvariantCulture));
            }

            if (schema.Description != null)
            {
                columns.Add(MySqlIdentifier.Quote(schema.Description.Name));
                literals.Add(QuoteSql(values.Description));
            }

            return "INSERT INTO " + MySqlIdentifier.Quote(table.Name) + " (" + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", literals) + ");";
        }

        // Build member update preview using schema-aware database operations.
        private static string BuildMemberUpdatePreview(
            TableMetadata table,
            PoolMembershipSchema schema,
            long poolEntry,
            long originalMemberId,
            PoolMemberValues replacement)
        {
            List<string> setters = new();
            if (replacement.MemberId != originalMemberId)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Member.Name) + " = " + replacement.MemberId.ToString(CultureInfo.InvariantCulture));
            }

            if (schema.Chance != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Chance.Name) + " = " + (replacement.Chance ?? 0d).ToString("R", CultureInfo.InvariantCulture));
            }

            if (schema.Description != null)
            {
                setters.Add(MySqlIdentifier.Quote(schema.Description.Name) + " = " + QuoteSql(replacement.Description));
            }

            return "UPDATE " + MySqlIdentifier.Quote(table.Name) + " SET " + string.Join(", ", setters) +
                   " WHERE " + MySqlIdentifier.Quote(schema.Member.Name) + " = " + originalMemberId.ToString(CultureInfo.InvariantCulture) +
                   " AND " + MySqlIdentifier.Quote(schema.Pool.Name) + " = " + poolEntry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Build member delete preview using schema-aware database operations.
        private static string BuildMemberDeletePreview(TableMetadata table, PoolMembershipSchema schema, long poolEntry, long memberId)
        {
            return "DELETE FROM " + MySqlIdentifier.Quote(table.Name) + " WHERE " +
                   MySqlIdentifier.Quote(schema.Member.Name) + " = " + memberId.ToString(CultureInfo.InvariantCulture) + " AND " +
                   MySqlIdentifier.Quote(schema.Pool.Name) + " = " + poolEntry.ToString(CultureInfo.InvariantCulture) + " LIMIT 1;";
        }

        // Read pool summary using schema-aware database operations.
        private static PoolSummary ReadPoolSummary(DbDataReader reader)
        {
            return new PoolSummary
            {
                Entry = GetInt64(reader, "PoolEntry"),
                MaxLimit = GetNullableInt32(reader, "PoolMaxLimit"),
                Description = GetString(reader, "PoolDescription"),
                CreatureSpawnCount = GetInt64(reader, "CreatureSpawnCount"),
                CreatureTemplateCount = GetInt64(reader, "CreatureTemplateCount"),
                GameObjectSpawnCount = GetInt64(reader, "GameObjectSpawnCount"),
                GameObjectTemplateCount = GetInt64(reader, "GameObjectTemplateCount"),
                ChildPoolCount = GetInt64(reader, "ChildPoolCount")
            };
        }

        // Add search parameter using schema-aware database operations.
        private static void AddSearchParameter(MySqlCommand command, string search)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
            }
        }

        // Handle member source order within the pool viewer service workflow.
        private static int MemberSourceOrder(string source)
        {
            if (string.Equals(source, PoolMemberSources.CreatureSpawn, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(source, PoolMemberSources.CreatureTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(source, PoolMemberSources.GameObjectSpawn, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(source, PoolMemberSources.GameObjectTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return 4;
        }

        // Handle fallback member name within the pool viewer service workflow.
        private static string FallbackMemberName(string source, long memberId)
        {
            if (string.Equals(source, PoolMemberSources.CreatureSpawn, StringComparison.OrdinalIgnoreCase))
            {
                return "Creature spawn " + memberId.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(source, PoolMemberSources.CreatureTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return "Creature entry " + memberId.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(source, PoolMemberSources.GameObjectSpawn, StringComparison.OrdinalIgnoreCase))
            {
                return "GameObject spawn " + memberId.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(source, PoolMemberSources.GameObjectTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return "GameObject entry " + memberId.ToString(CultureInfo.InvariantCulture);
            }

            return "Pool " + memberId.ToString(CultureInfo.InvariantCulture);
        }

        // Handle fallback resolved name within the pool viewer service workflow.
        private static string FallbackResolvedName(string source, long entityEntry)
        {
            if (string.Equals(source, PoolMemberSources.CreatureSpawn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, PoolMemberSources.CreatureTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return "Creature entry " + entityEntry.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(source, PoolMemberSources.GameObjectSpawn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, PoolMemberSources.GameObjectTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return "GameObject entry " + entityEntry.ToString(CultureInfo.InvariantCulture);
            }

            return "Pool " + entityEntry.ToString(CultureInfo.InvariantCulture);
        }

        // Quote and combine the database and table names as a qualified SQL identifier.
        private static string Qualify(TableMetadata table)
        {
            return MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
        }

        // Build a table-alias-qualified, safely quoted column name.
        private static string Column(string alias, ColumnMetadata column)
        {
            return alias + "." + MySqlIdentifier.Quote(column.Name);
        }

        // Locate a compatible column from the available schema metadata.
        private static ColumnMetadata? Find(TableMetadata? table, params string[] names)
        {
            if (table == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                ColumnMetadata? column = table.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                {
                    return column;
                }
            }

            return null;
        }

        // Escape and quote sql using schema-aware database operations.
        private static string QuoteSql(string? value)
        {
            return "'" + (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        // Read int64 using schema-aware database operations.
        private static long GetInt64(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return 0;
            }

            return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read nullable int64 using schema-aware database operations.
        private static long? GetNullableInt64(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read nullable int32 using schema-aware database operations.
        private static int? GetNullableInt32(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read nullable double using schema-aware database operations.
        private static double? GetNullableDouble(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            return Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        // Read string using schema-aware database operations.
        private static string GetString(DbDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private sealed class PoolTemplateSchema
        {
            public ColumnMetadata Entry { get; private set; } = new ColumnMetadata();
            public ColumnMetadata? MaxLimit
            {
                get; private set;
            }

            public ColumnMetadata? Description
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static PoolTemplateSchema Create(TableMetadata table)
            {
                ColumnMetadata? entry = Find(table, "entry", "pool_entry", "pool_id", "id");
                return entry == null
                    ? throw new InvalidOperationException("pool_template does not expose a supported pool entry column.")
                    : new PoolTemplateSchema
                    {
                        Entry = entry,
                        MaxLimit = Find(table, "max_limit", "maxlimit", "limit"),
                        Description = Find(table, "description", "comment")
                    };
            }
        }

        private sealed class PoolMembershipSchema
        {
            public ColumnMetadata Member { get; private set; } = new ColumnMetadata();
            public ColumnMetadata Pool { get; private set; } = new ColumnMetadata();
            public ColumnMetadata? Chance
            {
                get; private set;
            }

            public ColumnMetadata? Description
            {
                get; private set;
            }

            public bool IsPoolRelationship
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static PoolMembershipSchema Create(TableMetadata table, string source)
            {
                bool relationship = string.Equals(source, PoolMemberSources.ChildPool, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(table.Name, "pool_pool", StringComparison.OrdinalIgnoreCase);
                ColumnMetadata? member = relationship
                    ? Find(table, "pool_id", "pool_entry", "entry", "child_pool")
                    : string.Equals(table.Name, "pool_creature", StringComparison.OrdinalIgnoreCase) || string.Equals(table.Name, "pool_gameobject", StringComparison.OrdinalIgnoreCase)
                        ? Find(table, "guid")
                        : Find(table, "id", "entry");
                ColumnMetadata? pool = relationship
                    ? Find(table, "mother_pool", "motherpool", "mother_pool_entry", "parent_pool")
                    : Find(table, "pool_entry", "poolentry", "pool_id", "poolid", "pool");
                if (member == null || pool == null)
                {
                    throw new InvalidOperationException("Table '" + table.Name + "' does not expose the expected pool membership columns.");
                }

                return new PoolMembershipSchema
                {
                    Member = member,
                    Pool = pool,
                    Chance = Find(table, "chance"),
                    Description = Find(table, "description", "comment"),
                    IsPoolRelationship = relationship
                };
            }
        }
    }
}
