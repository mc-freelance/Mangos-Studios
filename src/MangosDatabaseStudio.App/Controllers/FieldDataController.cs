// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// ASP.NET Core request handling; permissions and validation must remain enforced server-side.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.App.Models;
using MangosDatabaseStudio.App.Services;
using MangosDatabaseStudio.App.Services.FieldData;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MangosDatabaseStudio.App.Controllers
{
    [Authorize(Policy = AppPermissions.FieldDataView)]
    public sealed class FieldDataController : Controller
    {
        private readonly IFieldDataDefinitionStore _store;
        private readonly IActiveConnectionAccessor _activeConnection;
        private readonly IMySqlSchemaService _schema;
        private readonly IApplicationUserStore _users;

        // Initialize the field data controller dependencies and state.
        public FieldDataController(
            IFieldDataDefinitionStore store,
            IActiveConnectionAccessor activeConnection,
            IMySqlSchemaService schema,
            IApplicationUserStore users)
        {
            _store = store;
            _activeConnection = activeConnection;
            _schema = schema;
            _users = users;
        }

        [HttpGet]
        // Handle the index page or action with the surrounding authorization checks.
        public async Task<IActionResult> Index(long? categoryId, string? search, int page = 1, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            string normalizedSearch = (search ?? string.Empty).Trim();
            int normalizedPageSize = await ResolvePageSizeAsync(pageSize, cancellationToken);
            IReadOnlyList<FieldDataDefinitionRecord> allDefinitions = await _store.GetDefinitionsAsync(categoryId, normalizedSearch, cancellationToken);
            int totalDefinitions = allDefinitions.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalDefinitions / (double)normalizedPageSize));
            int normalizedPage = Math.Min(Math.Max(page, 1), totalPages);
            IReadOnlyList<FieldDataDefinitionRecord> definitions = allDefinitions
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return View(new FieldDataIndexViewModel
            {
                Stats = await _store.GetStatsAsync(cancellationToken),
                Categories = await _store.GetCategoriesAsync(cancellationToken),
                Definitions = definitions,
                CategoryId = categoryId,
                Search = normalizedSearch,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalDefinitions = totalDefinitions,
                TotalPages = totalPages,
                CanManage = User.HasPermission(AppPermissions.FieldDataManage)
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Create category as part of the authorized desktop request.
        public IActionResult CreateCategory()
        {
            return View("Category", new FieldDataCategoryEditViewModel
            {
                IsCreate = true,
                IsEnabled = true,
                Icon = "bi-collection"
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Load or save the selected field-data category subject to validation.
        public async Task<IActionResult> EditCategory(long id, CancellationToken cancellationToken)
        {
            FieldDataCategoryRecord? category = await _store.GetCategoryAsync(id, cancellationToken);
            if (category == null)
            {
                return NotFound();
            }

            return View("Category", new FieldDataCategoryEditViewModel
            {
                Id = category.Id,
                IsCreate = false,
                Key = category.Key,
                Name = category.Name,
                Description = category.Description,
                Icon = category.Icon,
                SortOrder = category.SortOrder,
                IsEnabled = category.IsEnabled
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Persist category as part of the authorized desktop request.
        public async Task<IActionResult> SaveCategory(FieldDataCategoryEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = model.Id <= 0;
            if (!ModelState.IsValid)
            {
                return View("Category", model);
            }

            try
            {
                FieldDataCategoryRecord record = new()
                {
                    Id = model.Id,
                    Key = model.Key,
                    Name = model.Name,
                    Description = model.Description,
                    Icon = model.Icon,
                    SortOrder = model.SortOrder,
                    IsEnabled = model.IsEnabled
                };
                if (model.IsCreate)
                {
                    await _store.CreateCategoryAsync(record, cancellationToken);
                    TempData["Success"] = "Field-data category added.";
                }
                else
                {
                    await _store.UpdateCategoryAsync(record, cancellationToken);
                    TempData["Success"] = "Field-data category updated.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                return View("Category", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Delete category as part of the authorized desktop request.
        public async Task<IActionResult> DeleteCategory(long id, CancellationToken cancellationToken)
        {
            try
            {
                await _store.DeleteCategoryAsync(id, cancellationToken);
                TempData["Success"] = "Field-data category removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Create definition as part of the authorized desktop request.
        public async Task<IActionResult> CreateDefinition(long? categoryId, CancellationToken cancellationToken)
        {
            FieldDataDefinitionEditViewModel model = new()
            {
                IsCreate = true,
                CategoryId = categoryId,
                IsEnabled = true,
                SelectionMode = MangosFieldSelectionMode.Single,
                ValueHandling = MangosFieldValueHandling.Direct,
                PreserveUnknownValues = true
            };
            await PrepareDefinitionModelAsync(model, cancellationToken);
            return View("Definition", model);
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Load or save the selected field-definition metadata subject to validation.
        public async Task<IActionResult> EditDefinition(long id, CancellationToken cancellationToken)
        {
            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(id, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            FieldDataDefinitionEditViewModel model = new()
            {
                Id = definition.Id,
                IsCreate = false,
                CategoryId = definition.CategoryId,
                DisplayName = definition.DisplayName,
                TableName = definition.TableName,
                ColumnName = definition.ColumnName,
                SelectionMode = definition.SelectionMode,
                ValueHandling = definition.ValueHandling,
                PreserveUnknownValues = definition.PreserveUnknownValues,
                Description = definition.Description,
                IsEnabled = definition.IsEnabled,
                Priority = definition.Priority,
                IsSystem = definition.IsSystem,
                SourceKey = definition.SourceKey
            };
            await PrepareDefinitionModelAsync(model, cancellationToken);
            model.Variants = await _store.GetVariantsAsync(definition.Id, cancellationToken);
            return View("Definition", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Persist definition as part of the authorized desktop request.
        public async Task<IActionResult> SaveDefinition(FieldDataDefinitionEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = model.Id <= 0;
            if (model.SelectionMode == MangosFieldSelectionMode.Single)
            {
                model.ValueHandling = MangosFieldValueHandling.Direct;
                model.PreserveUnknownValues = false;
            }
            else if (model.ValueHandling == MangosFieldValueHandling.Direct)
            {
                ModelState.AddModelError(nameof(model.ValueHandling), "Choose how multiple selected values are combined.");
            }

            if (!ModelState.IsValid)
            {
                await PrepareDefinitionModelAsync(model, cancellationToken);
                if (!model.IsCreate)
                {
                    model.Variants = await _store.GetVariantsAsync(model.Id, cancellationToken);
                }

                return View("Definition", model);
            }

            try
            {
                FieldDataDefinitionRecord record = new()
                {
                    Id = model.Id,
                    CategoryId = model.CategoryId,
                    SourceKey = model.SourceKey,
                    DisplayName = model.DisplayName,
                    TableName = model.TableName,
                    ColumnName = model.ColumnName,
                    SelectionMode = model.SelectionMode,
                    ValueHandling = model.ValueHandling,
                    PreserveUnknownValues = model.PreserveUnknownValues,
                    Description = model.Description,
                    IsEnabled = model.IsEnabled,
                    Priority = model.Priority
                };

                if (model.IsCreate)
                {
                    long id = await _store.CreateDefinitionAsync(record, cancellationToken);
                    TempData["Success"] = "Column field data added. Add at least one expansion data set before it becomes active in the World Editor.";
                    return RedirectToAction(nameof(EditDefinition), new
                    {
                        id
                    });
                }

                await _store.UpdateDefinitionAsync(record, cancellationToken);
                TempData["Success"] = "Column field data updated.";
                return RedirectToAction(nameof(EditDefinition), new
                {
                    id = model.Id
                });
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                await PrepareDefinitionModelAsync(model, cancellationToken);
                if (!model.IsCreate)
                {
                    model.Variants = await _store.GetVariantsAsync(model.Id, cancellationToken);
                }

                return View("Definition", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Delete definition as part of the authorized desktop request.
        public async Task<IActionResult> DeleteDefinition(
            long id,
            long? categoryId,
            string? search,
            int page = 1,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _store.DeleteDefinitionAsync(id, cancellationToken);
                TempData["Success"] = "Column field data and its expansion entries were removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction(nameof(Index), new
            {
                categoryId,
                search,
                page
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Create variant as part of the authorized desktop request.
        public async Task<IActionResult> CreateVariant(long definitionId, CancellationToken cancellationToken)
        {
            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(definitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            return View("Variant", BuildVariantModel(definition, new FieldDataVariantRecord
            {
                DefinitionId = definitionId,
                Key = "expansion-data"
            }, true));
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Load and validate the variant editor request.
        public async Task<IActionResult> EditVariant(long id, int page = 1, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            FieldDataVariantRecord? variant = await _store.GetVariantAsync(id, cancellationToken);
            if (variant == null)
            {
                return NotFound();
            }

            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(variant.DefinitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            FieldDataVariantEditViewModel model = BuildVariantModel(definition, variant, false);
            ApplyEntryPagination(model, variant.Options, page, await ResolvePageSizeAsync(pageSize, cancellationToken));
            return View("Variant", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Persist variant as part of the authorized desktop request.
        public async Task<IActionResult> SaveVariant(FieldDataVariantEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = model.Id <= 0;
            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(model.DefinitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            if (model.Generations == null || model.Generations.Count == 0)
            {
                ModelState.AddModelError(nameof(model.Generations), "Select at least one expansion.");
            }

            if (!ModelState.IsValid)
            {
                FieldDataVariantRecord draft = new()
                {
                    Id = model.Id,
                    DefinitionId = model.DefinitionId,
                    Key = model.Key,
                    Description = model.Description,
                    Generations = model.Generations ?? new List<int>()
                };
                FieldDataVariantEditViewModel prepared = BuildVariantModel(definition, draft, model.IsCreate);
                if (!model.IsCreate)
                {
                    FieldDataVariantRecord? existing = await _store.GetVariantAsync(model.Id, cancellationToken);
                    ApplyEntryPagination(prepared, existing?.Options ?? new List<FieldDataOptionRecord>(), model.Page, await GetPreferredPageSizeAsync(cancellationToken));
                }

                return View("Variant", prepared);
            }

            try
            {
                FieldDataVariantRecord record = new()
                {
                    Id = model.Id,
                    DefinitionId = model.DefinitionId,
                    Key = model.Key,
                    Description = model.Description,
                    Generations = model.Generations ?? new List<int>()
                };
                if (model.IsCreate)
                {
                    long id = await _store.CreateVariantAsync(record, cancellationToken);
                    TempData["Success"] = "Expansion data set added. You can now add its entries.";
                    return RedirectToAction(nameof(EditVariant), new
                    {
                        id
                    });
                }

                await _store.UpdateVariantAsync(record, cancellationToken);
                TempData["Success"] = "Expansion data set updated.";
                return RedirectToAction(nameof(EditVariant), new
                {
                    id = model.Id,
                    page = model.Page
                });
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                FieldDataVariantRecord draft = new()
                {
                    Id = model.Id,
                    DefinitionId = model.DefinitionId,
                    Key = model.Key,
                    Description = model.Description,
                    Generations = model.Generations ?? new List<int>()
                };
                FieldDataVariantEditViewModel prepared = BuildVariantModel(definition, draft, model.IsCreate);
                if (!model.IsCreate)
                {
                    FieldDataVariantRecord? existing = await _store.GetVariantAsync(model.Id, cancellationToken);
                    ApplyEntryPagination(prepared, existing?.Options ?? new List<FieldDataOptionRecord>(), model.Page, await GetPreferredPageSizeAsync(cancellationToken));
                }

                return View("Variant", prepared);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Delete variant as part of the authorized desktop request.
        public async Task<IActionResult> DeleteVariant(long id, long definitionId, CancellationToken cancellationToken)
        {
            try
            {
                await _store.DeleteVariantAsync(id, cancellationToken);
                TempData["Success"] = "Expansion data set removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction(nameof(EditDefinition), new
            {
                id = definitionId
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Create entry as part of the authorized desktop request.
        public async Task<IActionResult> CreateEntry(long variantId, int page = 1, CancellationToken cancellationToken = default)
        {
            FieldDataVariantRecord? variant = await _store.GetVariantAsync(variantId, cancellationToken);
            if (variant == null)
            {
                return NotFound();
            }

            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(variant.DefinitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            return View("Entry", BuildEntryModel(definition, variant, new FieldDataOptionRecord
            {
                VariantId = variantId,
                SortOrder = variant.Options.Count
            }, true, page, await GetPreferredPageSizeAsync(cancellationToken)));
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Load and validate the entry editor request.
        public async Task<IActionResult> EditEntry(long variantId, long id, int page = 1, CancellationToken cancellationToken = default)
        {
            FieldDataVariantRecord? variant = await _store.GetVariantAsync(variantId, cancellationToken);
            if (variant == null)
            {
                return NotFound();
            }

            FieldDataOptionRecord? option = variant.Options.FirstOrDefault(item => item.Id == id);
            if (option == null)
            {
                return NotFound();
            }

            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(variant.DefinitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            return View("Entry", BuildEntryModel(definition, variant, option, false, page, await GetPreferredPageSizeAsync(cancellationToken)));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Persist entry as part of the authorized desktop request.
        public async Task<IActionResult> SaveEntry(FieldDataOptionEditViewModel model, CancellationToken cancellationToken)
        {
            model.IsCreate = model.Id <= 0;
            FieldDataVariantRecord? variant = await _store.GetVariantAsync(model.VariantId, cancellationToken);
            if (variant == null)
            {
                return NotFound();
            }

            FieldDataDefinitionRecord? definition = await _store.GetDefinitionAsync(variant.DefinitionId, cancellationToken);
            if (definition == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View("Entry", BuildEntryModel(definition, variant, new FieldDataOptionRecord
                {
                    Id = model.Id,
                    VariantId = model.VariantId,
                    Value = model.Value,
                    Label = model.Label,
                    Description = model.Description,
                    SortOrder = model.SortOrder
                }, model.IsCreate, model.Page, await GetPreferredPageSizeAsync(cancellationToken)));
            }

            try
            {
                FieldDataOptionRecord option = new()
                {
                    Id = model.Id,
                    VariantId = model.VariantId,
                    Value = model.Value,
                    Label = model.Label,
                    Description = model.Description,
                    SortOrder = model.SortOrder
                };
                if (model.IsCreate)
                {
                    await _store.CreateOptionAsync(option, cancellationToken);
                    TempData["Success"] = "Field-data entry added.";
                }
                else
                {
                    await _store.UpdateOptionAsync(option, cancellationToken);
                    TempData["Success"] = "Field-data entry updated.";
                }

                return RedirectToAction(nameof(EditVariant), new
                {
                    id = model.VariantId,
                    page = model.Page
                });
            }
            catch (Exception exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
                return View("Entry", BuildEntryModel(definition, variant, new FieldDataOptionRecord
                {
                    Id = model.Id,
                    VariantId = model.VariantId,
                    Value = model.Value,
                    Label = model.Label,
                    Description = model.Description,
                    SortOrder = model.SortOrder
                }, model.IsCreate, model.Page, await GetPreferredPageSizeAsync(cancellationToken)));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Delete entry as part of the authorized desktop request.
        public async Task<IActionResult> DeleteEntry(long id, long variantId, int page = 1, CancellationToken cancellationToken = default)
        {
            try
            {
                await _store.DeleteOptionAsync(id, cancellationToken);
                TempData["Success"] = "Field-data entry removed.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }

            return RedirectToAction(nameof(EditVariant), new
            {
                id = variantId,
                page
            });
        }

        [HttpGet]
        [Authorize(Policy = AppPermissions.FieldDataManage)]
        // Handle column options within the field data controller workflow.
        public async Task<IActionResult> ColumnOptions(string table, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(table))
            {
                return Json(Array.Empty<object>());
            }

            ConnectionProfile? profile = await _activeConnection.GetActiveAsync(cancellationToken);
            if (profile == null || string.IsNullOrWhiteSpace(profile.DefaultDatabase))
            {
                return Json(Array.Empty<object>());
            }

            try
            {
                TableMetadata metadata = await _schema.GetTableAsync(profile, profile.DefaultDatabase, table.Trim(), cancellationToken);
                return Json(metadata.Columns
                    .OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(column => new
                    {
                        name = column.Name,
                        type = column.ColumnType,
                        comment = column.Comment
                    }));
            }
            catch
            {
                return Json(Array.Empty<object>());
            }
        }

        // Handle prepare definition model async within the field data controller workflow.
        private async Task PrepareDefinitionModelAsync(FieldDataDefinitionEditViewModel model, CancellationToken cancellationToken)
        {
            model.Categories = await _store.GetCategoriesAsync(cancellationToken);
            model.AvailableTables = Array.Empty<string>();
            model.AvailableColumns = Array.Empty<string>();
            model.HasActiveDatabaseConnection = false;
            model.CanBrowseDatabaseSchema = false;
            model.ActiveDatabase = string.Empty;

            ConnectionProfile? profile = await _activeConnection.GetActiveAsync(cancellationToken);
            if (profile == null || string.IsNullOrWhiteSpace(profile.DefaultDatabase))
            {
                return;
            }

            model.HasActiveDatabaseConnection = true;
            model.ActiveDatabase = profile.DefaultDatabase;
            try
            {
                IReadOnlyList<TableMetadata> tables = await _schema.GetTablesAsync(profile, profile.DefaultDatabase, cancellationToken);
                model.AvailableTables = tables
                    .Select(table => table.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                model.CanBrowseDatabaseSchema = true;

                if (!string.IsNullOrWhiteSpace(model.TableName))
                {
                    try
                    {
                        TableMetadata selectedTable = await _schema.GetTableAsync(
                            profile,
                            profile.DefaultDatabase,
                            model.TableName.Trim(),
                            cancellationToken);
                        model.AvailableColumns = selectedTable.Columns
                            .Select(column => column.Name)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    catch
                    {
                        model.AvailableColumns = Array.Empty<string>();
                    }
                }
            }
            catch
            {
                model.AvailableTables = Array.Empty<string>();
                model.AvailableColumns = Array.Empty<string>();
                model.CanBrowseDatabaseSchema = false;
            }
        }

        // Clamp requested page sizes to the configured browsing limit.
        private static int NormalizePageSize(int pageSize)
        {
            return pageSize switch
            {
                10 => 10,
                20 => 20,
                50 => 50,
                100 => 100,
                200 => 200,
                _ => 20
            };
        }

        // Read preferred page size as part of the authorized desktop request.
        private Task<int> GetPreferredPageSizeAsync(CancellationToken cancellationToken)
        {
            return ResolvePageSizeAsync(null, cancellationToken);
        }

        // Resolve page size as part of the authorized desktop request.
        private async Task<int> ResolvePageSizeAsync(int? requestedPageSize, CancellationToken cancellationToken)
        {
            string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(value, out Guid id))
            {
                return requestedPageSize.HasValue ? NormalizePageSize(requestedPageSize.Value) : 20;
            }

            ApplicationUser? user = await _users.GetAsync(id, cancellationToken);
            UserPreferences preferences = UserPreferences.Normalize(user?.Preferences);

            if (requestedPageSize.HasValue && IsSupportedPageSize(requestedPageSize.Value))
            {
                int requested = requestedPageSize.Value;
                if (preferences.DefaultPageSize != requested && user != null)
                {
                    preferences.DefaultPageSize = requested;
                    await _users.UpdatePreferencesAsync(id, preferences, cancellationToken);
                }

                return requested;
            }

            return NormalizePageSize(preferences.DefaultPageSize);
        }

        // Determine whether supported page size as part of the authorized desktop request.
        private static bool IsSupportedPageSize(int pageSize)
        {
            return pageSize == 10 ||
                   pageSize == 20 ||
                   pageSize == 50 ||
                   pageSize == 100 ||
                   pageSize == 200;
        }

        // Apply entry pagination as part of the authorized desktop request.
        private static void ApplyEntryPagination(
            FieldDataVariantEditViewModel model,
            IReadOnlyList<FieldDataOptionRecord> options,
            int page,
            int pageSize)
        {
            int normalizedPageSize = NormalizePageSize(pageSize);
            int totalEntries = options.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalEntries / (double)normalizedPageSize));
            int normalizedPage = Math.Min(Math.Max(page, 1), totalPages);

            model.Page = normalizedPage;
            model.PageSize = normalizedPageSize;
            model.TotalEntries = totalEntries;
            model.TotalPages = totalPages;
            model.Options = options
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();
        }

        // Build variant model as part of the authorized desktop request.
        private static FieldDataVariantEditViewModel BuildVariantModel(
            FieldDataDefinitionRecord definition,
            FieldDataVariantRecord variant,
            bool isCreate)
        {
            return new FieldDataVariantEditViewModel
            {
                Id = variant.Id,
                DefinitionId = definition.Id,
                IsCreate = isCreate,
                DefinitionName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ColumnName : definition.DisplayName,
                TableName = definition.TableName,
                ColumnName = definition.ColumnName,
                SelectionMode = definition.SelectionMode,
                ValueHandling = definition.ValueHandling,
                Key = variant.Key,
                Description = variant.Description,
                Generations = variant.Generations.ToList(),
                Options = variant.Options
            };
        }

        // Build entry model as part of the authorized desktop request.
        private static FieldDataOptionEditViewModel BuildEntryModel(
            FieldDataDefinitionRecord definition,
            FieldDataVariantRecord variant,
            FieldDataOptionRecord option,
            bool isCreate,
            int page = 1,
            int pageSize = 20)
        {
            return new FieldDataOptionEditViewModel
            {
                Id = option.Id,
                VariantId = variant.Id,
                DefinitionId = definition.Id,
                IsCreate = isCreate,
                DefinitionName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ColumnName : definition.DisplayName,
                VariantName = variant.Key,
                SelectionMode = definition.SelectionMode,
                ValueHandling = definition.ValueHandling,
                Value = option.Value,
                Label = option.Label,
                Description = option.Description,
                SortOrder = option.SortOrder,
                Page = Math.Max(1, page),
                PageSize = NormalizePageSize(pageSize)
            };
        }
    }
}
