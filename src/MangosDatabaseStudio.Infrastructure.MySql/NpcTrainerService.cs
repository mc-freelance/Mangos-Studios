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
    internal sealed class NpcTrainerService : INpcTrainerService
    {
        private readonly MySqlConnectionFactory _connectionFactory;
        private readonly MySqlOptions _options;

        // Initialize the npc trainer service dependencies and state.
        public NpcTrainerService(MySqlConnectionFactory connectionFactory, MySqlOptions options)
        {
            _connectionFactory = connectionFactory;
            _options = options;
        }

        // Search for  using schema-aware database operations.
        public async Task<PagedResult<NpcTrainerSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcTrainer,
            TableMetadata? npcTrainerTemplate,
            ulong trainerFlagMask,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            TrainerSchema schema = TrainerSchema.Create(creatureTemplate, npcTrainer, npcTrainerTemplate, null);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, _options.MaximumBrowsePageSize);
            int offset = checked((page - 1) * pageSize);
            string normalizedSearch = search?.Trim() ?? string.Empty;

            string creatureTable = Qualify(creatureTemplate);
            string trainerTable = Qualify(npcTrainer);
            string entry = Column("ct", schema.CreatureEntry);
            string name = Column("ct", schema.CreatureName);
            string subName = schema.CreatureSubName == null ? "''" : "COALESCE(" + Column("ct", schema.CreatureSubName) + ", '')";
            string flags = Column("ct", schema.CreatureNpcFlags);
            string trainerEntry = Column("nt", schema.TrainerEntry);
            string directCount = "(SELECT COUNT(*) FROM " + trainerTable + " AS nt WHERE " + trainerEntry + " = " + entry + ")";
            string templateId = schema.CreatureTrainerTemplateId == null ? "NULL" : Column("ct", schema.CreatureTrainerTemplateId);
            string templateCount = "0";

            if (npcTrainerTemplate != null && schema.TemplateEntry != null && schema.CreatureTrainerTemplateId != null)
            {
                templateCount = "(SELECT COUNT(*) FROM " + Qualify(npcTrainerTemplate) + " AS ntt WHERE " +
                    Column("ntt", schema.TemplateEntry) + " = " + templateId + ")";
            }

            string searchClause = string.IsNullOrEmpty(normalizedSearch)
                ? string.Empty
                : " AND (" + name + " LIKE @search OR CAST(" + entry + " AS CHAR) = @searchExact)";

            string where = " WHERE (CAST(" + flags + " AS UNSIGNED) & @trainerMask) <> 0" + searchClause;
            string countSql = "SELECT COUNT(*) FROM " + creatureTable + " AS ct" + where + ";";
            string dataSql =
                "SELECT " + entry + " AS TrainerEntry, " + name + " AS TrainerName, " + subName + " AS TrainerSubName, " +
                flags + " AS TrainerNpcFlags, " + templateId + " AS TrainerTemplateId, (" + directCount + " + " + templateCount + ") AS TrainerSpellCount " +
                "FROM " + creatureTable + " AS ct" + where + " ORDER BY " + name + " ASC, " + entry + " ASC LIMIT @limit OFFSET @offset;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new(countSql, connection))
            {
                AddSearchParameters(command, trainerFlagMask, normalizedSearch);
                total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            List<NpcTrainerSummary> trainers = new();
            await using (MySqlCommand command = new(dataSql, connection))
            {
                AddSearchParameters(command, trainerFlagMask, normalizedSearch);

                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    trainers.Add(ReadTrainer(reader));
                }
            }

            return new PagedResult<NpcTrainerSummary>
            {
                Items = trainers,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Read  using schema-aware database operations.
        public async Task<NpcTrainerDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata creatureTemplate,
            TableMetadata npcTrainer,
            TableMetadata? npcTrainerTemplate,
            TableMetadata? spellTemplate,
            ulong trainerFlagMask,
            long entryValue,
            int spellPage,
            int spellPageSize,
            CancellationToken cancellationToken)
        {
            TrainerSchema schema = TrainerSchema.Create(creatureTemplate, npcTrainer, npcTrainerTemplate, spellTemplate);
            spellPage = Math.Max(1, spellPage);
            spellPageSize = Math.Clamp(spellPageSize, 10, _options.MaximumBrowsePageSize);

            string creatureTable = Qualify(creatureTemplate);
            string trainerTable = Qualify(npcTrainer);
            string entry = Column("ct", schema.CreatureEntry);
            string name = Column("ct", schema.CreatureName);
            string subName = schema.CreatureSubName == null ? "''" : "COALESCE(" + Column("ct", schema.CreatureSubName) + ", '')";
            string flags = Column("ct", schema.CreatureNpcFlags);
            string templateIdExpression = schema.CreatureTrainerTemplateId == null ? "NULL" : Column("ct", schema.CreatureTrainerTemplateId);
            string directCount = "(SELECT COUNT(*) FROM " + trainerTable + " AS nt WHERE " + Column("nt", schema.TrainerEntry) + " = " + entry + ")";
            string templateCount = "0";
            if (npcTrainerTemplate != null && schema.TemplateEntry != null && schema.CreatureTrainerTemplateId != null)
            {
                templateCount = "(SELECT COUNT(*) FROM " + Qualify(npcTrainerTemplate) + " AS ntt WHERE " +
                    Column("ntt", schema.TemplateEntry) + " = " + templateIdExpression + ")";
            }

            string trainerSql =
                "SELECT " + entry + " AS TrainerEntry, " + name + " AS TrainerName, " + subName + " AS TrainerSubName, " +
                flags + " AS TrainerNpcFlags, " + templateIdExpression + " AS TrainerTemplateId, (" + directCount + " + " + templateCount + ") AS TrainerSpellCount " +
                "FROM " + creatureTable + " AS ct WHERE " + entry + " = @entry AND (CAST(" + flags + " AS UNSIGNED) & @trainerMask) <> 0 LIMIT 1;";

            await using MySqlConnection connection = _connectionFactory.Create(profile, creatureTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            NpcTrainerSummary? trainer;
            await using (MySqlCommand command = new(trainerSql, connection))
            {
                command.Parameters.AddWithValue("@entry", entryValue);
                command.Parameters.AddWithValue("@trainerMask", trainerFlagMask);

                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                trainer = await reader.ReadAsync(cancellationToken) ? ReadTrainer(reader) : null;
            }

            if (trainer == null)
            {
                return null;
            }

            int totalPages = spellPageSize <= 0 ? 0 : (int)Math.Ceiling(trainer.SpellCount / (double)spellPageSize);
            if (totalPages == 0)
            {
                spellPage = 1;
            }
            else if (spellPage > totalPages)
            {
                spellPage = totalPages;
            }

            int offset = checked((spellPage - 1) * spellPageSize);

            List<string> unionParts = new()
            {
                BuildSpellSelect(npcTrainer, "nt", schema.TrainerEntry, schema.TrainerSpell, schema.TrainerSpellCost,
                    schema.TrainerRequiredSpell, schema.TrainerRequiredSkill, schema.TrainerRequiredSkillValue,
                    schema.TrainerRequiredLevel, schema.TrainerConditionId, spellTemplate, schema, false,
                    Column("nt", schema.TrainerEntry) + " = @entry")
            };

            if (npcTrainerTemplate != null && trainer.TrainerTemplateId.GetValueOrDefault() > 0 && schema.TemplateEntry != null && schema.TemplateSpell != null)
            {
                unionParts.Add(BuildSpellSelect(npcTrainerTemplate, "ntt", schema.TemplateEntry, schema.TemplateSpell, schema.TemplateSpellCost,
                    schema.TemplateRequiredSpell, schema.TemplateRequiredSkill, schema.TemplateRequiredSkillValue,
                    schema.TemplateRequiredLevel, schema.TemplateConditionId, spellTemplate, schema, true,
                    Column("ntt", schema.TemplateEntry) + " = @templateId"));
            }

            string spellsSql = "SELECT * FROM (" + string.Join(" UNION ALL ", unionParts) + ") AS trainer_spells " +
                "ORDER BY IsTemplateSpell ASC, SpellId ASC LIMIT @limit OFFSET @offset;";

            List<NpcTrainerSpell> spells = new();
            await using (MySqlCommand command = new(spellsSql, connection))
            {
                command.Parameters.AddWithValue("@entry", entryValue);
                command.Parameters.AddWithValue("@templateId", trainer.TrainerTemplateId.GetValueOrDefault());
                command.Parameters.AddWithValue("@limit", spellPageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    spells.Add(new NpcTrainerSpell
                    {
                        SpellId = GetInt64(reader, "SpellId"),
                        SpellName = GetString(reader, "SpellName"),
                        SpellCost = GetNullableInt64(reader, "SpellCost"),
                        RequiredSpell = GetNullableInt64(reader, "RequiredSpell"),
                        RequiredSkill = GetNullableInt64(reader, "RequiredSkill"),
                        RequiredSkillValue = GetNullableInt64(reader, "RequiredSkillValue"),
                        RequiredLevel = GetNullableInt64(reader, "RequiredLevel"),
                        ConditionId = GetNullableInt64(reader, "ConditionId"),
                        IsTemplateSpell = GetInt64(reader, "IsTemplateSpell") != 0,
                        TemplateId = GetNullableInt64(reader, "TemplateId")
                    });
                }
            }

            return new NpcTrainerDetail
            {
                Trainer = trainer,
                Spells = new PagedResult<NpcTrainerSpell>
                {
                    Items = spells,
                    TotalCount = trainer.SpellCount,
                    Page = spellPage,
                    PageSize = spellPageSize
                }
            };
        }

        // Search for spells using schema-aware database operations.
        public async Task<PagedResult<NpcTrainerSpellOption>> SearchSpellsAsync(
            ConnectionProfile profile,
            TableMetadata spellTemplate,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ColumnMetadata spellId = Required(spellTemplate, "id", "entry", "spell");
            ColumnMetadata? spellName = Optional(spellTemplate, "spellname", "name", "spell_name");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 25, 200);
            int offset = checked((page - 1) * pageSize);
            string normalized = search?.Trim() ?? string.Empty;
            string id = MySqlIdentifier.Quote(spellId.Name);
            string name = spellName == null ? "''" : "COALESCE(" + MySqlIdentifier.Quote(spellName.Name) + ", '')";
            string where = string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : " WHERE CAST(" + id + " AS CHAR) = @searchExact" + (spellName == null ? string.Empty : " OR " + MySqlIdentifier.Quote(spellName.Name) + " LIKE @search");
            string table = Qualify(spellTemplate);

            await using MySqlConnection connection = _connectionFactory.Create(profile, spellTemplate.DatabaseName);
            await connection.OpenAsync(cancellationToken);

            long total;
            await using (MySqlCommand command = new("SELECT COUNT(*) FROM " + table + where + ";", connection))
            {
                AddSpellSearch(command, normalized);
                total = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            List<NpcTrainerSpellOption> results = new();
            await using (MySqlCommand command = new(
                "SELECT " + id + " AS SpellId, " + name + " AS SpellName FROM " + table + where + " ORDER BY " +
                (spellName == null ? id : MySqlIdentifier.Quote(spellName.Name) + " ASC, " + id) + " ASC LIMIT @limit OFFSET @offset;", connection))
            {
                AddSpellSearch(command, normalized);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new NpcTrainerSpellOption
                    {
                        SpellId = GetInt64(reader, "SpellId"),
                        SpellName = GetString(reader, "SpellName")
                    });
                }
            }

            return new PagedResult<NpcTrainerSpellOption>
            {
                Items = results,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // Report which editor operations the detected database schema supports.
        public NpcTrainerEditorCapabilities GetEditorCapabilities(TableMetadata npcTrainer)
        {
            TrainerEditSchema schema = TrainerEditSchema.Create(npcTrainer);
            IReadOnlyList<string> keyColumns;
            try
            {
                keyColumns = ResolveKeyColumns(npcTrainer, schema).Select(column => column.Name).ToArray();
            }
            catch (InvalidOperationException)
            {
                keyColumns = Array.Empty<string>();
            }

            return new NpcTrainerEditorCapabilities
            {
                EntryColumn = schema.Entry.Name,
                SpellColumn = schema.Spell.Name,
                SpellCostColumn = schema.SpellCost?.Name,
                RequiredSpellColumn = schema.RequiredSpell?.Name,
                RequiredSkillColumn = schema.RequiredSkill?.Name,
                RequiredSkillValueColumn = schema.RequiredSkillValue?.Name,
                RequiredLevelColumn = schema.RequiredLevel?.Name,
                ConditionIdColumn = schema.ConditionId?.Name,
                UpdateColumns = schema.GetUpdateColumns().Select(column => column.Name).ToArray(),
                KeyColumns = keyColumns
            };
        }

        // Add spells using schema-aware database operations.
        public async Task<NpcTrainerMutationResult> AddSpellsAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            IReadOnlyList<NpcTrainerSpellValues> spells,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC trainer entry is required.");
            }

            if (spells == null || spells.Count == 0)
            {
                throw new InvalidOperationException("Add at least one trainer spell before saving.");
            }

            if (spells.Count > 250)
            {
                throw new InvalidOperationException("A maximum of 250 trainer spells can be added in one operation.");
            }

            foreach (NpcTrainerSpellValues spell in spells)
            {
                ValidateSpellValues(spell);
            }

            TrainerEditSchema schema = TrainerEditSchema.Create(npcTrainer);
            IReadOnlyList<ColumnMetadata> columns = schema.GetInsertColumns();
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcTrainer, schema);

            string sql = "INSERT INTO " + Qualify(npcTrainer) + " (" + string.Join(", ", columns.Select(c => MySqlIdentifier.Quote(c.Name))) + ") VALUES (" +
                string.Join(", ", columns.Select(c => "@" + ParameterName(c.Name))) + ");";
            string preview = BuildInsertPreview(npcTrainer, schema, entry, spells);
            string revertPreview = BuildDeleteRevertPreview(npcTrainer, schema, entry, spells, keyColumns, columns);

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcTrainer.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            int affected = 0;
            try
            {
                foreach (NpcTrainerSpellValues spell in spells)
                {
                    await using MySqlCommand command = new(sql, connection, transaction);
                    AddMutationParameters(command, schema, columns, entry, spell, string.Empty);
                    affected += await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return new NpcTrainerMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Update spell using schema-aware database operations.
        public async Task<NpcTrainerMutationResult> UpdateSpellAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            NpcTrainerSpellValues original,
            NpcTrainerSpellValues replacement,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC trainer entry is required.");
            }

            ValidateSpellValues(original);
            ValidateSpellValues(replacement);
            TrainerEditSchema schema = TrainerEditSchema.Create(npcTrainer);
            IReadOnlyList<ColumnMetadata> updateColumns = schema.GetUpdateColumns();
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcTrainer, schema);
            string set = string.Join(", ", updateColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " = @new_" + ParameterName(c.Name)));
            string where = string.Join(" AND ", keyColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " <=> @old_" + ParameterName(c.Name)));
            string sql = "UPDATE " + Qualify(npcTrainer) + " SET " + set + " WHERE " + where + ";";
            string preview = BuildUpdatePreview(npcTrainer, schema, entry, original, replacement, updateColumns, keyColumns);
            string revertPreview = BuildUpdateRevertPreview(npcTrainer, schema, entry, original, replacement, updateColumns, keyColumns);

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcTrainer.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            foreach (ColumnMetadata column in updateColumns)
            {
                command.Parameters.AddWithValue("@new_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, replacement));
            }

            foreach (ColumnMetadata column in keyColumns)
            {
                command.Parameters.AddWithValue("@old_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, original));
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The trainer spell could not be updated because the original row was not found. Refresh the trainer and try again.");
            }

            return new NpcTrainerMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Delete spell using schema-aware database operations.
        public async Task<NpcTrainerMutationResult> DeleteSpellAsync(
            ConnectionProfile profile,
            TableMetadata npcTrainer,
            long entry,
            NpcTrainerSpellValues original,
            CancellationToken cancellationToken)
        {
            if (entry <= 0)
            {
                throw new InvalidOperationException("A valid NPC trainer entry is required.");
            }

            ValidateSpellValues(original);
            TrainerEditSchema schema = TrainerEditSchema.Create(npcTrainer);
            IReadOnlyList<ColumnMetadata> keyColumns = ResolveKeyColumns(npcTrainer, schema);
            string where = string.Join(" AND ", keyColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " <=> @old_" + ParameterName(c.Name)));
            string sql = "DELETE FROM " + Qualify(npcTrainer) + " WHERE " + where + ";";
            string preview = "DELETE FROM " + MySqlIdentifier.Quote(npcTrainer.Name) + " WHERE " +
                string.Join(" AND ", keyColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " <=> " + SqlLiteral(ResolveColumnValue(schema, c, entry, original)))) + ";";
            string revertPreview = BuildInsertPreview(npcTrainer, schema, entry, new[] { original });

            await using MySqlConnection connection = _connectionFactory.Create(profile, npcTrainer.DatabaseName);
            await connection.OpenAsync(cancellationToken);
            await using MySqlCommand command = new(sql, connection);
            foreach (ColumnMetadata column in keyColumns)
            {
                command.Parameters.AddWithValue("@old_" + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, original));
            }

            int affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new InvalidOperationException("The trainer spell could not be removed because the row was not found. Refresh the trainer and try again.");
            }

            return new NpcTrainerMutationResult
            {
                AffectedRows = affected,
                SqlPreview = preview,
                RevertSqlPreview = revertPreview
            };
        }

        // Build spell select using schema-aware database operations.
        private static string BuildSpellSelect(
            TableMetadata sourceTable,
            string sourceAlias,
            ColumnMetadata sourceEntry,
            ColumnMetadata sourceSpell,
            ColumnMetadata? spellCost,
            ColumnMetadata? requiredSpell,
            ColumnMetadata? requiredSkill,
            ColumnMetadata? requiredSkillValue,
            ColumnMetadata? requiredLevel,
            ColumnMetadata? conditionId,
            TableMetadata? spellTemplate,
            TrainerSchema schema,
            bool template,
            string where)
        {
            string source = Qualify(sourceTable) + " AS " + sourceAlias;
            string join = string.Empty;
            string spellName = "''";
            if (spellTemplate != null && schema.SpellTemplateId != null)
            {
                join = " LEFT JOIN " + Qualify(spellTemplate) + " AS st ON " + Column("st", schema.SpellTemplateId) + " = " + Column(sourceAlias, sourceSpell);
                if (schema.SpellTemplateName != null)
                {
                    spellName = "COALESCE(" + Column("st", schema.SpellTemplateName) + ", '')";
                }
            }

            return "SELECT " + Column(sourceAlias, sourceSpell) + " AS SpellId, " + spellName + " AS SpellName, " +
                OptionalValue(sourceAlias, spellCost, "SpellCost") + ", " + OptionalValue(sourceAlias, requiredSpell, "RequiredSpell") + ", " +
                OptionalValue(sourceAlias, requiredSkill, "RequiredSkill") + ", " + OptionalValue(sourceAlias, requiredSkillValue, "RequiredSkillValue") + ", " +
                OptionalValue(sourceAlias, requiredLevel, "RequiredLevel") + ", " + OptionalValue(sourceAlias, conditionId, "ConditionId") + ", " +
                (template ? "1" : "0") + " AS IsTemplateSpell, " + (template ? Column(sourceAlias, sourceEntry) : "NULL") + " AS TemplateId FROM " + source + join + " WHERE " + where;
        }

        // Handle optional value within the npc trainer service workflow.
        private static string OptionalValue(string alias, ColumnMetadata? column, string resultAlias)
        {
            return (column == null ? "NULL" : Column(alias, column)) + " AS " + resultAlias;
        }

        // Validate spell values using schema-aware database operations.
        private static void ValidateSpellValues(NpcTrainerSpellValues spell)
        {
            if (spell == null || spell.SpellId <= 0)
            {
                throw new InvalidOperationException("Every trainer row must contain a valid spell ID.");
            }

            ValidateNonNegative(spell.SpellCost, "Spell cost");
            ValidateNonNegative(spell.RequiredSpell, "Required spell");
            ValidateNonNegative(spell.RequiredSkill, "Required skill");
            ValidateNonNegative(spell.RequiredSkillValue, "Required skill value");
            ValidateNonNegative(spell.RequiredLevel, "Required level");
            ValidateNonNegative(spell.ConditionId, "Condition");
        }

        // Validate non negative using schema-aware database operations.
        private static void ValidateNonNegative(long? value, string label)
        {
            if (value < 0)
            {
                throw new InvalidOperationException(label + " cannot be negative.");
            }
        }

        // Add mutation parameters using schema-aware database operations.
        private static void AddMutationParameters(MySqlCommand command, TrainerEditSchema schema, IReadOnlyList<ColumnMetadata> columns, long entry, NpcTrainerSpellValues spell, string prefix)
        {
            foreach (ColumnMetadata column in columns)
            {
                command.Parameters.AddWithValue("@" + prefix + ParameterName(column.Name), ResolveColumnValue(schema, column, entry, spell));
            }
        }

        // Resolve column value using schema-aware database operations.
        private static object ResolveColumnValue(TrainerEditSchema schema, ColumnMetadata column, long entry, NpcTrainerSpellValues spell)
        {
            if (SameColumn(column, schema.Entry))
            {
                return entry;
            }

            if (SameColumn(column, schema.Spell))
            {
                return spell.SpellId;
            }

            if (SameColumn(column, schema.SpellCost))
            {
                return NormalizeOptional(column, spell.SpellCost);
            }

            if (SameColumn(column, schema.RequiredSpell))
            {
                return NormalizeOptional(column, spell.RequiredSpell);
            }

            if (SameColumn(column, schema.RequiredSkill))
            {
                return NormalizeOptional(column, spell.RequiredSkill);
            }

            if (SameColumn(column, schema.RequiredSkillValue))
            {
                return NormalizeOptional(column, spell.RequiredSkillValue);
            }

            if (SameColumn(column, schema.RequiredLevel))
            {
                return NormalizeOptional(column, spell.RequiredLevel);
            }

            if (SameColumn(column, schema.ConditionId))
            {
                return NormalizeOptional(column, spell.ConditionId);
            }

            throw new InvalidOperationException("Unsupported npc_trainer key column '" + column.Name + "'.");
        }

        // Normalize optional using schema-aware database operations.
        private static object NormalizeOptional(ColumnMetadata column, long? value)
        {
            if (value.HasValue)
            {
                return value.Value;
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultValue) && long.TryParse(column.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }

            return column.IsNullable ? DBNull.Value : 0L;
        }

        // Resolve key columns using schema-aware database operations.
        private static IReadOnlyList<ColumnMetadata> ResolveKeyColumns(TableMetadata table, TrainerEditSchema schema)
        {
            if (table.PrimaryKeyColumns.Count > 0)
            {
                foreach (ColumnMetadata column in table.PrimaryKeyColumns)
                {
                    ResolveColumnValue(schema, column, 1L, new NpcTrainerSpellValues { SpellId = 1L });
                }

                return table.PrimaryKeyColumns;
            }

            return new[]
            {
                schema.Entry,
                schema.Spell
            };
        }

        // Build insert preview using schema-aware database operations.
        private static string BuildInsertPreview(TableMetadata table, TrainerEditSchema schema, long entry, IReadOnlyList<NpcTrainerSpellValues> spells)
        {
            IReadOnlyList<ColumnMetadata> columns = schema.GetInsertColumns();
            List<string> rows = spells.Select(spell => "(" + string.Join(", ", columns.Select(c => SqlLiteral(ResolveColumnValue(schema, c, entry, spell)))) + ")").ToList();
            return "INSERT INTO " + MySqlIdentifier.Quote(table.Name) + " (" + string.Join(", ", columns.Select(c => MySqlIdentifier.Quote(c.Name))) + ") VALUES\n    " + string.Join(",\n    ", rows) + ";";
        }

        // Build update preview using schema-aware database operations.
        private static string BuildUpdatePreview(TableMetadata table, TrainerEditSchema schema, long entry, NpcTrainerSpellValues original, NpcTrainerSpellValues replacement, IReadOnlyList<ColumnMetadata> updateColumns, IReadOnlyList<ColumnMetadata> keyColumns)
        {
            return "UPDATE " + MySqlIdentifier.Quote(table.Name) + " SET\n    " +
                string.Join(",\n    ", updateColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " = " + SqlLiteral(ResolveColumnValue(schema, c, entry, replacement)))) +
                "\nWHERE " + string.Join(" AND ", keyColumns.Select(c => MySqlIdentifier.Quote(c.Name) + " <=> " + SqlLiteral(ResolveColumnValue(schema, c, entry, original)))) + ";";
        }

        // Build delete revert preview using schema-aware database operations.
        private static string BuildDeleteRevertPreview(
            TableMetadata npcTrainer,
            TrainerEditSchema schema,
            long entry,
            IReadOnlyList<NpcTrainerSpellValues> spells,
            IReadOnlyList<ColumnMetadata> keyColumns,
            IReadOnlyList<ColumnMetadata> insertedColumns)
        {
            IReadOnlyList<ColumnMetadata> guardColumns = keyColumns
                .Concat(insertedColumns)
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            List<string> rows = spells.Select(spell =>
                "(" + string.Join(" AND ", guardColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " <=> " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, spell)))) + ")").ToList();

            return "DELETE FROM " + MySqlIdentifier.Quote(npcTrainer.Name) +
                   " WHERE " + string.Join(" OR ", rows) +
                   " LIMIT " + spells.Count.ToString(CultureInfo.InvariantCulture) + ";";
        }

        // Build update revert preview using schema-aware database operations.
        private static string BuildUpdateRevertPreview(
            TableMetadata npcTrainer,
            TrainerEditSchema schema,
            long entry,
            NpcTrainerSpellValues original,
            NpcTrainerSpellValues replacement,
            IReadOnlyList<ColumnMetadata> updateColumns,
            IReadOnlyList<ColumnMetadata> keyColumns)
        {
            IReadOnlyList<ColumnMetadata> guardColumns = keyColumns
                .Concat(updateColumns)
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            return "UPDATE " + MySqlIdentifier.Quote(npcTrainer.Name) + " SET\n    " +
                string.Join(",\n    ", updateColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " = " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, original)))) +
                "\nWHERE " + string.Join(" AND ", guardColumns.Select(column =>
                    MySqlIdentifier.Quote(column.Name) + " <=> " +
                    SqlLiteral(ResolveColumnValue(schema, column, entry, replacement)))) + ";";
        }

        // Format a value as a SQL literal for a generated SQL preview.
        private static string SqlLiteral(object value) => value == DBNull.Value || value == null ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        // Normalize a column name into a safe query parameter identifier.
        private static string ParameterName(string value) => new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        // Test whether the supplied schema metadata describes the same column.
        private static bool SameColumn(ColumnMetadata column, ColumnMetadata? other) => other != null && string.Equals(column.Name, other.Name, StringComparison.OrdinalIgnoreCase);

        // Add search parameters using schema-aware database operations.
        private static void AddSearchParameters(MySqlCommand command, ulong trainerFlagMask, string search)
        {
            command.Parameters.AddWithValue("@trainerMask", trainerFlagMask);
            if (!string.IsNullOrEmpty(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
                command.Parameters.AddWithValue("@searchExact", search);
            }
        }

        // Add spell search using schema-aware database operations.
        private static void AddSpellSearch(MySqlCommand command, string search)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                command.Parameters.AddWithValue("@search", "%" + search + "%");
                command.Parameters.AddWithValue("@searchExact", search);
            }
        }

        // Read trainer using schema-aware database operations.
        private static NpcTrainerSummary ReadTrainer(DbDataReader reader)
        {
            return new NpcTrainerSummary
            {
                Entry = GetInt64(reader, "TrainerEntry"),
                Name = GetString(reader, "TrainerName"),
                SubName = GetString(reader, "TrainerSubName"),
                NpcFlags = GetUInt64(reader, "TrainerNpcFlags"),
                TrainerTemplateId = GetNullableInt64(reader, "TrainerTemplateId"),
                SpellCount = GetInt64(reader, "TrainerSpellCount")
            };
        }

        private sealed class TrainerEditSchema
        {
            public ColumnMetadata Entry { get; private set; } = null!;
            public ColumnMetadata Spell { get; private set; } = null!;
            public ColumnMetadata? SpellCost
            {
                get; private set;
            }

            public ColumnMetadata? RequiredSpell
            {
                get; private set;
            }

            public ColumnMetadata? RequiredSkill
            {
                get; private set;
            }

            public ColumnMetadata? RequiredSkillValue
            {
                get; private set;
            }

            public ColumnMetadata? RequiredLevel
            {
                get; private set;
            }

            public ColumnMetadata? ConditionId
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static TrainerEditSchema Create(TableMetadata table) => new()
            {
                Entry = Required(table, "entry"),
                Spell = Required(table, "spell"),
                SpellCost = Optional(table, "spellcost", "spell_cost", "cost"),
                RequiredSpell = Optional(table, "reqspell", "requiredspell", "req_spell", "required_spell"),
                RequiredSkill = Optional(table, "reqskill", "requiredskill", "req_skill", "required_skill"),
                RequiredSkillValue = Optional(table, "reqskillvalue", "requiredskillvalue", "req_skill_value", "required_skill_value"),
                RequiredLevel = Optional(table, "reqlevel", "requiredlevel", "req_level", "required_level"),
                ConditionId = Optional(table, "condition_id", "conditionid")
            };

            // Read insert columns using schema-aware database operations.
            public IReadOnlyList<ColumnMetadata> GetInsertColumns()
            {
                List<ColumnMetadata> columns = new() { Entry, Spell };
                Add(columns, SpellCost);
                Add(columns, RequiredSpell);
                Add(columns, RequiredSkill);
                Add(columns, RequiredSkillValue);
                Add(columns, RequiredLevel);
                Add(columns, ConditionId);
                return columns;
            }

            // Read update columns using schema-aware database operations.
            public IReadOnlyList<ColumnMetadata> GetUpdateColumns()
            {
                List<ColumnMetadata> columns = new() { Spell };
                Add(columns, SpellCost);
                Add(columns, RequiredSpell);
                Add(columns, RequiredSkill);
                Add(columns, RequiredSkillValue);
                Add(columns, RequiredLevel);
                Add(columns, ConditionId);
                return columns;
            }

            // Handle add within the npc trainer service workflow.
            private static void Add(List<ColumnMetadata> list, ColumnMetadata? column)
            {
                if (column != null)
                {
                    list.Add(column);
                }
            }
        }

        private sealed class TrainerSchema
        {
            public ColumnMetadata CreatureEntry { get; private set; } = null!;
            public ColumnMetadata CreatureName { get; private set; } = null!;
            public ColumnMetadata? CreatureSubName
            {
                get; private set;
            }

            public ColumnMetadata CreatureNpcFlags { get; private set; } = null!;
            public ColumnMetadata? CreatureTrainerTemplateId
            {
                get; private set;
            }

            public ColumnMetadata TrainerEntry { get; private set; } = null!;
            public ColumnMetadata TrainerSpell { get; private set; } = null!;
            public ColumnMetadata? TrainerSpellCost
            {
                get; private set;
            }

            public ColumnMetadata? TrainerRequiredSpell
            {
                get; private set;
            }

            public ColumnMetadata? TrainerRequiredSkill
            {
                get; private set;
            }

            public ColumnMetadata? TrainerRequiredSkillValue
            {
                get; private set;
            }

            public ColumnMetadata? TrainerRequiredLevel
            {
                get; private set;
            }

            public ColumnMetadata? TrainerConditionId
            {
                get; private set;
            }

            public ColumnMetadata? TemplateEntry
            {
                get; private set;
            }

            public ColumnMetadata? TemplateSpell
            {
                get; private set;
            }

            public ColumnMetadata? TemplateSpellCost
            {
                get; private set;
            }

            public ColumnMetadata? TemplateRequiredSpell
            {
                get; private set;
            }

            public ColumnMetadata? TemplateRequiredSkill
            {
                get; private set;
            }

            public ColumnMetadata? TemplateRequiredSkillValue
            {
                get; private set;
            }

            public ColumnMetadata? TemplateRequiredLevel
            {
                get; private set;
            }

            public ColumnMetadata? TemplateConditionId
            {
                get; private set;
            }

            public ColumnMetadata? SpellTemplateId
            {
                get; private set;
            }

            public ColumnMetadata? SpellTemplateName
            {
                get; private set;
            }

            // Build a field definition from its seed metadata and expansion variants.
            public static TrainerSchema Create(TableMetadata creature, TableMetadata trainer, TableMetadata? trainerTemplate, TableMetadata? spellTemplate)
            {
                TrainerEditSchema direct = TrainerEditSchema.Create(trainer);
                TrainerSchema schema = new()
                {
                    CreatureEntry = Required(creature, "entry"),
                    CreatureName = Required(creature, "name"),
                    CreatureSubName = Optional(creature, "subname", "sub_name"),
                    CreatureNpcFlags = Required(creature, "npcflag", "npcflags", "npc_flags"),
                    CreatureTrainerTemplateId = Optional(creature, "trainertemplateid", "trainer_template_id"),
                    TrainerEntry = direct.Entry,
                    TrainerSpell = direct.Spell,
                    TrainerSpellCost = direct.SpellCost,
                    TrainerRequiredSpell = direct.RequiredSpell,
                    TrainerRequiredSkill = direct.RequiredSkill,
                    TrainerRequiredSkillValue = direct.RequiredSkillValue,
                    TrainerRequiredLevel = direct.RequiredLevel,
                    TrainerConditionId = direct.ConditionId
                };

                if (trainerTemplate != null)
                {
                    TrainerEditSchema template = TrainerEditSchema.Create(trainerTemplate);
                    schema.TemplateEntry = template.Entry;
                    schema.TemplateSpell = template.Spell;
                    schema.TemplateSpellCost = template.SpellCost;
                    schema.TemplateRequiredSpell = template.RequiredSpell;
                    schema.TemplateRequiredSkill = template.RequiredSkill;
                    schema.TemplateRequiredSkillValue = template.RequiredSkillValue;
                    schema.TemplateRequiredLevel = template.RequiredLevel;
                    schema.TemplateConditionId = template.ConditionId;
                }

                if (spellTemplate != null)
                {
                    schema.SpellTemplateId = Optional(spellTemplate, "id", "entry", "spell");
                    schema.SpellTemplateName = Optional(spellTemplate, "spellname", "name", "spell_name");
                }

                return schema;
            }
        }

        // Handle required within the npc trainer service workflow.
        private static ColumnMetadata Required(TableMetadata table, params string[] names) => Optional(table, names) ?? throw new InvalidOperationException("Table '" + table.Name + "' does not expose the required column " + string.Join("/", names) + ".");
        // Find an optional schema column among its known alternative names.
        private static ColumnMetadata? Optional(TableMetadata table, params string[] names) => table.Columns.FirstOrDefault(column => names.Any(name => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)));
        // Quote and combine the database and table names as a qualified SQL identifier.
        private static string Qualify(TableMetadata table) => MySqlIdentifier.Quote(table.DatabaseName) + "." + MySqlIdentifier.Quote(table.Name);
        // Build a table-alias-qualified, safely quoted column name.
        private static string Column(string alias, ColumnMetadata column) => alias + "." + MySqlIdentifier.Quote(column.Name);
        // Read int64 using schema-aware database operations.
        private static long GetInt64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? 0L : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Read uint64 using schema-aware database operations.
        private static ulong GetUInt64(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? 0UL : Convert.ToUInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
        }

        // Read string using schema-aware database operations.
        private static string GetString(DbDataReader reader, string name)
        {
            int i = reader.GetOrdinal(name);
            return reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // Read nullable int64 using schema-aware database operations.
        private static long? GetNullableInt64(DbDataReader reader, string name)
        {
            try
            {
                int i = reader.GetOrdinal(name);
                return reader.IsDBNull(i) ? null : Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }
    }
}
