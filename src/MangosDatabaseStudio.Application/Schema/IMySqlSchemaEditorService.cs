// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Schema metadata and guarded table-editing contracts.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.Application.Schema
{
    public sealed class SchemaColumnDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string ColumnType { get; set; } = string.Empty;
        public bool IsNullable
        {
            get; set;
        }

        public string DefaultMode { get; set; } = "none";
        public string DefaultValue { get; set; } = string.Empty;
        public bool IsAutoIncrement
        {
            get; set;
        }

        public string Comment { get; set; } = string.Empty;
        public string Position { get; set; } = "LAST";
        public string AfterColumn { get; set; } = string.Empty;
    }

    public sealed class SchemaIndexDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = "INDEX";
        public IReadOnlyList<string> Columns { get; set; } = new List<string>();
    }

    public sealed class SchemaTablePropertiesDefinition
    {
        public string Engine { get; set; } = string.Empty;
        public string Collation { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }

    public sealed class SchemaChangePlan
    {
        public string Operation { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
        public bool IsDestructive
        {
            get; set;
        }

        public string ConfirmationValue { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
    }

    public interface IMySqlSchemaEditorService
    {
        Task<SchemaChangePlan> BuildAddColumnPlanAsync(ConnectionProfile profile, string database, string table, SchemaColumnDefinition definition, CancellationToken cancellationToken);
        Task<SchemaChangePlan> BuildEditColumnPlanAsync(ConnectionProfile profile, string database, string table, string originalColumn, SchemaColumnDefinition definition, CancellationToken cancellationToken);
        Task<SchemaChangePlan> BuildDropColumnPlanAsync(ConnectionProfile profile, string database, string table, string column, CancellationToken cancellationToken);
        Task<SchemaChangePlan> BuildAddIndexPlanAsync(ConnectionProfile profile, string database, string table, SchemaIndexDefinition definition, CancellationToken cancellationToken);
        Task<SchemaChangePlan> BuildDropIndexPlanAsync(ConnectionProfile profile, string database, string table, string indexName, CancellationToken cancellationToken);
        Task<SchemaChangePlan> BuildTablePropertiesPlanAsync(ConnectionProfile profile, string database, string table, SchemaTablePropertiesDefinition definition, CancellationToken cancellationToken);
        Task ExecuteAsync(ConnectionProfile profile, SchemaChangePlan plan, CancellationToken cancellationToken);
    }
}
