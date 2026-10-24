// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.DatabaseBrowse)]
    public sealed class DatabaseAdminController : StudioController
    {
        private readonly IMySqlSchemaService _schema;
        private readonly IMySqlSchemaEditorService _schemaEditor;
        private readonly IAuthorizationService _authorization;
        private readonly IChangeStorageStore _changeStorage;
        private readonly ILogger<DatabaseAdminController> _logger;
        private const string PreviewPointerSessionKey = "SchemaPreview:Current";

        // Initialize the database admin controller dependencies and state.
        public DatabaseAdminController(
            IActiveConnectionAccessor activeConnectionAccessor,
            IMySqlSchemaService schema,
            IMySqlSchemaEditorService schemaEditor,
            IAuthorizationService authorization,
            IChangeStorageStore changeStorage,
            ILogger<DatabaseAdminController> logger)
            : base(activeConnectionAccessor)
        {
            _schema = schema;
            _schemaEditor = schemaEditor;
            _authorization = authorization;
            _changeStorage = changeStorage;
            _logger = logger;
        }

        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            return View(new DatabaseAdminIndexViewModel
            {
                Databases = DatabaseVisibilityPolicy.FilterVisible(await _schema.GetDatabasesAsync(profile, cancellationToken))
            });
        }

        // Handle tables within the database admin controller workflow.
        public async Task<IActionResult> Tables(string database, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            return View(new DatabaseTablesViewModel
            {
                Database = database,
                Tables = await _schema.GetTablesAsync(profile, database, cancellationToken)
            });
        }

        [Authorize(Policy = AppPermissions.DatabaseStructureView)]
        // Handle structure within the database admin controller workflow.
        public async Task<IActionResult> Structure(string database, string table, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            return View(await BuildStructureViewModelAsync(metadata));
        }

        [Authorize(Policy = AppPermissions.DatabaseStructureCreate)]
        // Add column as part of the authorized desktop request.
        public async Task<IActionResult> AddColumn(string database, string table, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            return View("ColumnEditor", new SchemaColumnEditorViewModel
            {
                Database = database,
                Table = table,
                IsNew = true,
                Position = "LAST",
                DefaultMode = "none",
                AvailableColumns = metadata.Columns
            });
        }

        [Authorize(Policy = AppPermissions.DatabaseStructureModify)]
        // Load and validate the column editor request.
        public async Task<IActionResult> EditColumn(string database, string table, string column, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            ColumnMetadata? current = metadata.Columns.FirstOrDefault(item => string.Equals(item.Name, column, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                return NotFound();
            }

            (string defaultMode, string defaultValue) = GetDefaultForEditor(current);
            string position = current.OrdinalPosition <= 1 ? "FIRST" : "AFTER";
            string afterColumn = current.OrdinalPosition <= 1
                ? string.Empty
                : metadata.Columns.First(item => item.OrdinalPosition == current.OrdinalPosition - 1).Name;

            return View("ColumnEditor", new SchemaColumnEditorViewModel
            {
                Database = database,
                Table = table,
                IsNew = false,
                OriginalColumnName = current.Name,
                Name = current.Name,
                ColumnType = current.ColumnType,
                IsNullable = current.IsNullable,
                DefaultMode = defaultMode,
                DefaultValue = defaultValue,
                IsAutoIncrement = current.IsAutoIncrement,
                Comment = current.Comment,
                Position = position,
                AfterColumn = afterColumn,
                AvailableColumns = metadata.Columns
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Handle preview column within the database admin controller workflow.
        public async Task<IActionResult> PreviewColumn(SchemaColumnEditorViewModel model, CancellationToken cancellationToken)
        {
            string permission = model.IsNew ? AppPermissions.DatabaseStructureCreate : AppPermissions.DatabaseStructureModify;
            if (!(await _authorization.AuthorizeAsync(User, permission)).Succeeded)
            {
                return Forbid();
            }

            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await PopulateColumnEditorAsync(profile, model, cancellationToken);
                return View("ColumnEditor", model);
            }

            SchemaChangeRequestViewModel request = new()
            {
                Database = model.Database,
                Table = model.Table,
                Operation = model.IsNew ? "add-column" : "edit-column",
                OriginalColumnName = model.OriginalColumnName,
                ColumnName = model.Name,
                ColumnType = model.ColumnType,
                IsNullable = model.IsNullable,
                DefaultMode = model.DefaultMode,
                DefaultValue = model.DefaultValue,
                IsAutoIncrement = model.IsAutoIncrement,
                Comment = model.Comment,
                Position = model.Position,
                AfterColumn = model.AfterColumn
            };

            try
            {
                return await RenderPreviewAsync(profile, request, cancellationToken);
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                await PopulateColumnEditorAsync(profile, model, cancellationToken);
                return View("ColumnEditor", model);
            }
        }

        [Authorize(Policy = AppPermissions.DatabaseStructureIndexes)]
        // Add index as part of the authorized desktop request.
        public async Task<IActionResult> AddIndex(string database, string table, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            return View("IndexEditor", new SchemaIndexEditorViewModel
            {
                Database = database,
                Table = table,
                TableMetadata = metadata
            });
        }

        [HttpPost]
        [Authorize(Policy = AppPermissions.DatabaseStructureIndexes)]
        [ValidateAntiForgeryToken]
        // Handle preview index within the database admin controller workflow.
        public async Task<IActionResult> PreviewIndex(SchemaIndexEditorViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                model.TableMetadata = await _schema.GetTableAsync(profile, model.Database, model.Table, cancellationToken);
                return View("IndexEditor", model);
            }

            SchemaChangeRequestViewModel request = new()
            {
                Database = model.Database,
                Table = model.Table,
                Operation = "add-index",
                IndexName = model.Name,
                IndexKind = model.Kind,
                IndexColumns = model.Columns
            };

            try
            {
                return await RenderPreviewAsync(profile, request, cancellationToken);
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                model.TableMetadata = await _schema.GetTableAsync(profile, model.Database, model.Table, cancellationToken);
                return View("IndexEditor", model);
            }
        }

        [Authorize(Policy = AppPermissions.DatabaseStructureTableProperties)]
        // Handle table properties within the database admin controller workflow.
        public async Task<IActionResult> TableProperties(string database, string table, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            TableMetadata metadata = await _schema.GetTableAsync(profile, database, table, cancellationToken);
            return View("TableProperties", new SchemaTablePropertiesEditorViewModel
            {
                Database = database,
                Table = table,
                Engine = metadata.Engine,
                Collation = metadata.Collation,
                Comment = metadata.Comment,
                Engines = await _schema.GetEnginesAsync(profile, cancellationToken),
                Collations = await _schema.GetCollationsAsync(profile, cancellationToken)
            });
        }

        [HttpPost]
        [Authorize(Policy = AppPermissions.DatabaseStructureTableProperties)]
        [ValidateAntiForgeryToken]
        // Handle preview table properties within the database admin controller workflow.
        public async Task<IActionResult> PreviewTableProperties(SchemaTablePropertiesEditorViewModel model, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(model.Database))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await PopulateTablePropertiesAsync(profile, model, cancellationToken);
                return View("TableProperties", model);
            }

            SchemaChangeRequestViewModel request = new()
            {
                Database = model.Database,
                Table = model.Table,
                Operation = "table-properties",
                Engine = model.Engine,
                Collation = model.Collation,
                TableComment = model.Comment
            };

            try
            {
                return await RenderPreviewAsync(profile, request, cancellationToken);
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                await PopulateTablePropertiesAsync(profile, model, cancellationToken);
                return View("TableProperties", model);
            }
        }

        [HttpPost]
        [Authorize(Policy = AppPermissions.DatabaseStructureDelete)]
        [ValidateAntiForgeryToken]
        // Handle preview drop column within the database admin controller workflow.
        public async Task<IActionResult> PreviewDropColumn(string database, string table, string column, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            try
            {
                return await RenderPreviewAsync(profile, new SchemaChangeRequestViewModel
                {
                    Database = database,
                    Table = table,
                    Operation = "drop-column",
                    OriginalColumnName = column
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                TempData["Error"] = "The column could not be prepared for removal. " + exception.Message;
                return RedirectToAction(nameof(Structure), new
                {
                    database,
                    table
                });
            }
        }

        [HttpPost]
        [Authorize(Policy = AppPermissions.DatabaseStructureIndexes)]
        [ValidateAntiForgeryToken]
        // Handle preview drop index within the database admin controller workflow.
        public async Task<IActionResult> PreviewDropIndex(string database, string table, string indexName, CancellationToken cancellationToken)
        {
            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(database))
            {
                return NotFound();
            }

            try
            {
                return await RenderPreviewAsync(profile, new SchemaChangeRequestViewModel
                {
                    Database = database,
                    Table = table,
                    Operation = "drop-index",
                    IndexName = indexName
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                TempData["Error"] = "The index could not be prepared for removal. " + exception.Message;
                return RedirectToAction(nameof(Structure), new
                {
                    database,
                    table
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Execute schema change as part of the authorized desktop request.
        public async Task<IActionResult> ExecuteSchemaChange(SchemaChangeRequestViewModel request, CancellationToken cancellationToken)
        {
            string permission = PermissionForOperation(request.Operation);
            if (!(await _authorization.AuthorizeAsync(User, permission)).Succeeded)
            {
                return Forbid();
            }

            ConnectionProfile? profile = await ActiveConnectionAccessor.GetActiveAsync(cancellationToken);
            if (profile == null)
            {
                return NoActiveConnection();
            }

            if (DatabaseVisibilityPolicy.IsProtected(request.Database))
            {
                return NotFound();
            }

            try
            {
                string previewKey = PreviewSessionKey(request.PreviewToken);
                string previewSchemaKey = PreviewSchemaSessionKey(request.PreviewToken);
                string currentPreviewToken = HttpContext.Session.GetString(PreviewPointerSessionKey) ?? string.Empty;
                string? storedSchemaFingerprint = string.IsNullOrWhiteSpace(previewSchemaKey) ? null : HttpContext.Session.GetString(previewSchemaKey);
                string currentCreateTableSql = await _schema.GetCreateTableSqlAsync(profile, request.Database, request.Table, cancellationToken);
                string currentSchemaFingerprint = ComputeTableSchemaFingerprint(currentCreateTableSql);
                if (!string.Equals(currentPreviewToken, request.PreviewToken, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(storedSchemaFingerprint) ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(storedSchemaFingerprint),
                        Encoding.UTF8.GetBytes(currentSchemaFingerprint)))
                {
                    TempData["Warning"] = "The live table structure changed after this preview was generated. Review the schema change again before executing it.";
                    return RedirectToAction(nameof(Structure), new
                    {
                        database = request.Database,
                        table = request.Table
                    });
                }

                SchemaChangePlan plan = await BuildPlanAsync(profile, request, cancellationToken);
                if (string.IsNullOrWhiteSpace(request.PreviewSql) || !string.Equals(request.PreviewSql.Trim(), plan.Sql.Trim(), StringComparison.Ordinal))
                {
                    TempData["Warning"] = "The live table definition changed after the SQL preview was generated. Review the schema change again before executing it.";
                    return RedirectToAction(nameof(Structure), new
                    {
                        database = request.Database,
                        table = request.Table
                    });
                }

                string expectedFingerprint = ComputePreviewFingerprint(request, plan.Sql);
                string? storedFingerprint = string.IsNullOrWhiteSpace(previewKey) ? null : HttpContext.Session.GetString(previewKey);
                if (!string.Equals(currentPreviewToken, request.PreviewToken, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(storedFingerprint) || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(storedFingerprint), Encoding.UTF8.GetBytes(expectedFingerprint)))
                {
                    TempData["Warning"] = "This schema preview is missing, expired, or no longer matches the requested operation. Generate a fresh SQL preview before executing the change.";
                    return RedirectToAction(nameof(Structure), new
                    {
                        database = request.Database,
                        table = request.Table
                    });
                }

                if (plan.IsDestructive && !string.Equals(request.Confirmation?.Trim(), plan.ConfirmationValue, StringComparison.Ordinal))
                {
                    ModelState.AddModelError(nameof(request.Confirmation), "Type '" + plan.ConfirmationValue + "' exactly to confirm this destructive schema change.");
                    return View("StructurePreview", new SchemaChangePreviewViewModel
                    {
                        Request = request,
                        Plan = plan,
                        Table = await _schema.GetTableAsync(profile, request.Database, request.Table, cancellationToken)
                    });
                }

                await _schemaEditor.ExecuteAsync(profile, plan, cancellationToken);
                HttpContext.Session.Remove(previewKey);
                HttpContext.Session.Remove(previewSchemaKey);
                HttpContext.Session.Remove(PreviewPointerSessionKey);
                await RecordSchemaChangeAsync(profile, request.Database, plan, cancellationToken);
                TempData["Success"] = plan.Summary;
                return RedirectToAction(nameof(Structure), new
                {
                    database = request.Database,
                    table = request.Table
                });
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Schema change failed for {Database}.{Table} ({Operation}).", request.Database, request.Table, request.Operation);
                TempData["Error"] = "The schema change was not applied. " + exception.Message;
                return RedirectToAction(nameof(Structure), new
                {
                    database = request.Database,
                    table = request.Table
                });
            }
        }

        // Read default for editor as part of the authorized desktop request.
        private static (string Mode, string Value) GetDefaultForEditor(ColumnMetadata column)
        {
            if (column.DefaultValue == null)
            {
                return (column.IsNullable ? "null" : "none", string.Empty);
            }

            string raw = column.DefaultValue.Trim();
            if (raw.StartsWith("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            {
                return ("current-timestamp", string.Empty);
            }

            if (string.Equals(column.DataType, "bit", StringComparison.OrdinalIgnoreCase) &&
                raw.Length >= 4 &&
                raw.StartsWith("b'", StringComparison.OrdinalIgnoreCase) &&
                raw.EndsWith('\''))
            {
                string bits = raw[2..^1];
                return ("bit-literal", bits);
            }

            if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            {
                return ("value", DecodeSqlStringLiteral(raw[1..^1]));
            }

            return ("value", raw);
        }

        // Handle decode sql string literal within the database admin controller workflow.
        private static string DecodeSqlStringLiteral(string value)
        {
            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current == '\'' && i + 1 < value.Length && value[i + 1] == '\'')
                {
                    builder.Append('\'');
                    i++;
                    continue;
                }

                if (current == '\\' && i + 1 < value.Length)
                {
                    char escaped = value[++i];
                    builder.Append(escaped switch
                    {
                        '0' => '\0',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'b' => '\b',
                        'Z' => (char)26,
                        _ => escaped
                    });
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        // Build structure view model as part of the authorized desktop request.
        private async Task<TableStructureViewModel> BuildStructureViewModelAsync(TableMetadata metadata)
        {
            return new TableStructureViewModel
            {
                Table = metadata,
                CanModifyColumns = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureModify)).Succeeded,
                CanAddColumns = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureCreate)).Succeeded,
                CanDropColumns = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureDelete)).Succeeded,
                CanManageIndexes = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureIndexes)).Succeeded,
                CanManageTableProperties = (await _authorization.AuthorizeAsync(User, AppPermissions.DatabaseStructureTableProperties)).Succeeded
            };
        }

        // Handle populate column editor async within the database admin controller workflow.
        private async Task PopulateColumnEditorAsync(ConnectionProfile profile, SchemaColumnEditorViewModel model, CancellationToken cancellationToken)
        {
            TableMetadata metadata = await _schema.GetTableAsync(profile, model.Database, model.Table, cancellationToken);
            model.AvailableColumns = metadata.Columns;
        }

        // Handle populate table properties async within the database admin controller workflow.
        private async Task PopulateTablePropertiesAsync(ConnectionProfile profile, SchemaTablePropertiesEditorViewModel model, CancellationToken cancellationToken)
        {
            model.Engines = await _schema.GetEnginesAsync(profile, cancellationToken);
            model.Collations = await _schema.GetCollationsAsync(profile, cancellationToken);
        }

        // Handle render preview async within the database admin controller workflow.
        private async Task<IActionResult> RenderPreviewAsync(ConnectionProfile profile, SchemaChangeRequestViewModel request, CancellationToken cancellationToken)
        {
            string permission = PermissionForOperation(request.Operation);
            if (!(await _authorization.AuthorizeAsync(User, permission)).Succeeded)
            {
                return Forbid();
            }

            SchemaChangePlan plan = await BuildPlanAsync(profile, request, cancellationToken);
            TableMetadata table = await _schema.GetTableAsync(profile, request.Database, request.Table, cancellationToken);
            request.PreviewSql = plan.Sql;
            string? previousToken = HttpContext.Session.GetString(PreviewPointerSessionKey);
            string previousKey = PreviewSessionKey(previousToken);
            string previousSchemaKey = PreviewSchemaSessionKey(previousToken);
            if (!string.IsNullOrWhiteSpace(previousKey))
            {
                HttpContext.Session.Remove(previousKey);
            }

            if (!string.IsNullOrWhiteSpace(previousSchemaKey))
            {
                HttpContext.Session.Remove(previousSchemaKey);
            }

            request.PreviewToken = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(PreviewPointerSessionKey, request.PreviewToken);
            HttpContext.Session.SetString(PreviewSessionKey(request.PreviewToken), ComputePreviewFingerprint(request, plan.Sql));
            string createTableSql = await _schema.GetCreateTableSqlAsync(profile, request.Database, request.Table, cancellationToken);
            HttpContext.Session.SetString(PreviewSchemaSessionKey(request.PreviewToken), ComputeTableSchemaFingerprint(createTableSql));
            return View("StructurePreview", new SchemaChangePreviewViewModel
            {
                Request = request,
                Plan = plan,
                Table = table
            });
        }

        // Build plan as part of the authorized desktop request.
        private Task<SchemaChangePlan> BuildPlanAsync(ConnectionProfile profile, SchemaChangeRequestViewModel request, CancellationToken cancellationToken)
        {
            return request.Operation switch
            {
                "add-column" => _schemaEditor.BuildAddColumnPlanAsync(profile, request.Database, request.Table, BuildColumnDefinition(request), cancellationToken),
                "edit-column" => _schemaEditor.BuildEditColumnPlanAsync(profile, request.Database, request.Table, request.OriginalColumnName ?? string.Empty, BuildColumnDefinition(request), cancellationToken),
                "drop-column" => _schemaEditor.BuildDropColumnPlanAsync(profile, request.Database, request.Table, request.OriginalColumnName ?? string.Empty, cancellationToken),
                "add-index" => _schemaEditor.BuildAddIndexPlanAsync(profile, request.Database, request.Table, new SchemaIndexDefinition
                {
                    Name = request.IndexName ?? string.Empty,
                    Kind = request.IndexKind,
                    Columns = request.IndexColumns
                }, cancellationToken),
                "drop-index" => _schemaEditor.BuildDropIndexPlanAsync(profile, request.Database, request.Table, request.IndexName ?? string.Empty, cancellationToken),
                "table-properties" => _schemaEditor.BuildTablePropertiesPlanAsync(profile, request.Database, request.Table, new SchemaTablePropertiesDefinition
                {
                    Engine = request.Engine ?? string.Empty,
                    Collation = request.Collation ?? string.Empty,
                    Comment = request.TableComment ?? string.Empty
                }, cancellationToken),
                _ => throw new InvalidOperationException("The requested schema operation is not supported.")
            };
        }

        // Build column definition as part of the authorized desktop request.
        private static SchemaColumnDefinition BuildColumnDefinition(SchemaChangeRequestViewModel request)
        {
            return new SchemaColumnDefinition
            {
                Name = request.ColumnName ?? string.Empty,
                ColumnType = request.ColumnType ?? string.Empty,
                IsNullable = request.IsNullable,
                DefaultMode = request.DefaultMode,
                DefaultValue = request.DefaultValue ?? string.Empty,
                IsAutoIncrement = request.IsAutoIncrement,
                Comment = request.Comment ?? string.Empty,
                Position = request.Position,
                AfterColumn = request.AfterColumn ?? string.Empty
            };
        }

        // Handle permission for operation within the database admin controller workflow.
        private static string PermissionForOperation(string? operation)
        {
            return operation switch
            {
                "add-column" => AppPermissions.DatabaseStructureCreate,
                "edit-column" => AppPermissions.DatabaseStructureModify,
                "drop-column" => AppPermissions.DatabaseStructureDelete,
                "add-index" or "drop-index" => AppPermissions.DatabaseStructureIndexes,
                "table-properties" => AppPermissions.DatabaseStructureTableProperties,
                _ => AppPermissions.DatabaseStructureModify
            };
        }

        // Handle preview session key within the database admin controller workflow.
        private static string PreviewSessionKey(string? token)
        {
            string normalized = (token ?? string.Empty).Trim();
            return Guid.TryParseExact(normalized, "N", out _)
                ? "SchemaPreview:" + normalized
                : string.Empty;
        }

        // Handle preview schema session key within the database admin controller workflow.
        private static string PreviewSchemaSessionKey(string? token)
        {
            string normalized = (token ?? string.Empty).Trim();
            return Guid.TryParseExact(normalized, "N", out _)
                ? "SchemaPreviewSchema:" + normalized
                : string.Empty;
        }

        // Handle compute table schema fingerprint within the database admin controller workflow.
        private static string ComputeTableSchemaFingerprint(string createTableSql)
        {
            // SHOW CREATE TABLE may include the current AUTO_INCREMENT counter, which
            // changes as rows are inserted even though the schema itself did not. Only
            // strip that volatile value from the trailing table-options section so text
            // inside column/table comments remains part of the fingerprint.
            string normalized = createTableSql ?? string.Empty;
            int tableOptionsStart = normalized.LastIndexOf(") ENGINE=", StringComparison.OrdinalIgnoreCase);
            if (tableOptionsStart >= 0)
            {
                string prefix = normalized[..(tableOptionsStart + 1)];
                string options = normalized[(tableOptionsStart + 1)..];
                options = Regex.Replace(
                    options,
                    @"\sAUTO_INCREMENT\s*=\s*\d+\b",
                    string.Empty,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                normalized = prefix + options;
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }

        // Handle compute preview fingerprint within the database admin controller workflow.
        private static string ComputePreviewFingerprint(SchemaChangeRequestViewModel request, string sql)
        {
            string payload = string.Join("\n", new[]
            {
                request.Operation ?? string.Empty,
                request.Database ?? string.Empty,
                request.Table ?? string.Empty,
                sql ?? string.Empty
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }

        // Handle record schema change async within the database admin controller workflow.
        private async Task RecordSchemaChangeAsync(ConnectionProfile profile, string database, SchemaChangePlan plan, CancellationToken cancellationToken)
        {
            try
            {
                await _changeStorage.RecordAsync(new StoredDatabaseChange
                {
                    UserId = GetCurrentUserId(),
                    Username = User.Identity?.Name ?? string.Empty,
                    ConnectionId = profile.Id,
                    ConnectionName = profile.Name,
                    DatabaseName = database,
                    Source = StoredChangeSources.SchemaEditor,
                    Operation = plan.Operation,
                    ObjectName = plan.ObjectName,
                    Summary = plan.Summary + " Schema changes require manual rollback.",
                    SqlText = plan.Sql,
                    ContributionEligible = false,
                    RevertSqlText = string.Empty,
                    RevertExpectedRows = 0,
                    ReviewStatus = StoredChangeStatuses.Approved,
                    ReviewedAtUtc = DateTime.UtcNow,
                    ReviewedByUserId = GetCurrentUserId(),
                    ReviewedByUsername = User.Identity?.Name ?? string.Empty
                }, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Schema change completed but Change Storage logging failed.");
                TempData["Warning"] = "The schema change completed, but its audit record could not be saved to Change Storage.";
            }
        }

        // Read current user id as part of the authorized desktop request.
        private Guid GetCurrentUserId()
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
        }
    }
}
