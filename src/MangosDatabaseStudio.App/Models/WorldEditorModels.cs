// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class WorldIndexViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public IReadOnlyList<MangosTableCategory> Categories { get; set; } = new List<MangosTableCategory>();
        public int TableCount
        {
            get; set;
        }

        public string CatalogDescription { get; set; } = string.Empty;
        public bool CanBrowseDatabaseStructure
        {
            get; set;
        }

        public bool HasNpcVendorViewer
        {
            get; set;
        }

        public bool HasNpcTrainerViewer
        {
            get; set;
        }
    }

    public sealed class NpcVendorViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public ulong VendorFlagMask
        {
            get; set;
        }

        public string VendorFlagMaskDisplay { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public IReadOnlyList<string> MissingTables { get; set; } = new List<string>();
        public PagedResult<NpcVendorSummary> Vendors { get; set; } = new PagedResult<NpcVendorSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public NpcVendorDetail? SelectedVendor
        {
            get; set;
        }

        public NpcVendorEditorCapabilities EditorCapabilities { get; set; } = new NpcVendorEditorCapabilities();
        public bool CanAddVendorItems
        {
            get; set;
        }

        public bool CanEditVendorItems
        {
            get; set;
        }

        public bool CanDeleteVendorItems
        {
            get; set;
        }
    }


    public sealed class NpcVendorItemPostValues
    {
        public long ItemId
        {
            get; set;
        }

        public long? Slot
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? IncrementTime
        {
            get; set;
        }

        public long? ExtendedCost
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        // Handle to domain within the world editor models workflow.
        public NpcVendorItemValues ToDomain()
        {
            return new NpcVendorItemValues
            {
                ItemId = ItemId,
                Slot = Slot,
                MaxCount = MaxCount,
                IncrementTime = IncrementTime,
                ExtendedCost = ExtendedCost,
                ConditionId = ConditionId
            };
        }
    }

    public abstract class NpcVendorMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int ItemPage { get; set; } = 1;
        public int ItemPageSize { get; set; } = 20;
    }

    public sealed class NpcVendorAddItemsPostModel : NpcVendorMutationPostModel
    {
        public List<NpcVendorItemPostValues> Items { get; set; } = new List<NpcVendorItemPostValues>();
    }

    public sealed class NpcVendorEditItemPostModel : NpcVendorMutationPostModel
    {
        public NpcVendorItemPostValues Original { get; set; } = new NpcVendorItemPostValues();
        public NpcVendorItemPostValues Replacement { get; set; } = new NpcVendorItemPostValues();
    }

    public sealed class NpcVendorDeleteItemPostModel : NpcVendorMutationPostModel
    {
        public NpcVendorItemPostValues Original { get; set; } = new NpcVendorItemPostValues();
    }

    public sealed class NpcTrainerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public ulong TrainerFlagMask
        {
            get; set;
        }

        public string TrainerFlagMaskDisplay { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public bool HasSpellTemplate
        {
            get; set;
        }

        public bool HasTrainerTemplate
        {
            get; set;
        }

        public IReadOnlyList<string> MissingTables { get; set; } = new List<string>();
        public PagedResult<NpcTrainerSummary> Trainers { get; set; } = new PagedResult<NpcTrainerSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public NpcTrainerDetail? SelectedTrainer
        {
            get; set;
        }

        public NpcTrainerEditorCapabilities EditorCapabilities { get; set; } = new NpcTrainerEditorCapabilities();
        public bool CanAddTrainerSpells
        {
            get; set;
        }

        public bool CanEditTrainerSpells
        {
            get; set;
        }

        public bool CanDeleteTrainerSpells
        {
            get; set;
        }
    }

    public sealed class NpcTrainerSpellPostValues
    {
        public long SpellId
        {
            get; set;
        }

        public long? SpellCost
        {
            get; set;
        }

        public long? RequiredSpell
        {
            get; set;
        }

        public long? RequiredSkill
        {
            get; set;
        }

        public long? RequiredSkillValue
        {
            get; set;
        }

        public long? RequiredLevel
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        // Handle to domain within the world editor models workflow.
        public NpcTrainerSpellValues ToDomain()
        {
            return new NpcTrainerSpellValues
            {
                SpellId = SpellId,
                SpellCost = SpellCost,
                RequiredSpell = RequiredSpell,
                RequiredSkill = RequiredSkill,
                RequiredSkillValue = RequiredSkillValue,
                RequiredLevel = RequiredLevel,
                ConditionId = ConditionId
            };
        }
    }

    public abstract class NpcTrainerMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int SpellPage { get; set; } = 1;
        public int SpellPageSize { get; set; } = 20;
    }

    public sealed class NpcTrainerAddSpellsPostModel : NpcTrainerMutationPostModel
    {
        public List<NpcTrainerSpellPostValues> Spells { get; set; } = new List<NpcTrainerSpellPostValues>();
    }

    public sealed class NpcTrainerEditSpellPostModel : NpcTrainerMutationPostModel
    {
        public NpcTrainerSpellPostValues Original { get; set; } = new NpcTrainerSpellPostValues();
        public NpcTrainerSpellPostValues Replacement { get; set; } = new NpcTrainerSpellPostValues();
    }

    public sealed class NpcTrainerDeleteSpellPostModel : NpcTrainerMutationPostModel
    {
        public NpcTrainerSpellPostValues Original { get; set; } = new NpcTrainerSpellPostValues();
    }




    public sealed class LootViewerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string Mode { get; set; } = LootViewerModes.Templates;
        public string Search { get; set; } = string.Empty;
        public string TableFilter { get; set; } = "all";
        public string SourceFilter { get; set; } = LootSourceFilters.All;
        public string ReturnUrl { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public IReadOnlyList<LootTableDescriptor> LootTables { get; set; } = new List<LootTableDescriptor>();
        public PagedResult<LootTemplateSummary> Templates { get; set; } = new PagedResult<LootTemplateSummary>();
        public PagedResult<LootSourceSummary> Sources { get; set; } = new PagedResult<LootSourceSummary>();
        public string? SelectedTable
        {
            get; set;
        }

        public long? SelectedEntry
        {
            get; set;
        }

        public LootTemplateDetail? SelectedTemplate
        {
            get; set;
        }

        public IReadOnlyList<LootEditorCapabilities> EditorTables { get; set; } = new List<LootEditorCapabilities>();
        public bool CanAddRows
        {
            get; set;
        }

        public bool CanEditRows
        {
            get; set;
        }

        public bool CanRemoveRows
        {
            get; set;
        }
    }

    public abstract class LootMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string TableFilter { get; set; } = "all";
        public string ReturnUrl { get; set; } = string.Empty;
        public string LootTable { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int RowPage { get; set; } = 1;
        public int RowPageSize { get; set; } = 20;
    }

    public sealed class LootRowPostValues
    {
        public long ItemId
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public long? GroupId
        {
            get; set;
        }

        public string RowKind { get; set; } = "item";
        public long? MinCount
        {
            get; set;
        }

        public long? ReferenceEntry
        {
            get; set;
        }

        public long? MaxCount
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        // Handle to domain within the world editor models workflow.
        public LootRowValues ToDomain(long entry, bool supportsReferences)
        {
            long? minOrRef = supportsReferences && string.Equals(RowKind, "reference", StringComparison.OrdinalIgnoreCase)
                ? (ReferenceEntry.HasValue ? -Math.Abs(ReferenceEntry.Value) : (long?)null)
                : MinCount;
            return new LootRowValues
            {
                Entry = entry,
                ItemId = ItemId,
                Chance = Chance,
                GroupId = GroupId,
                MinCountOrReference = minOrRef,
                MaxCount = MaxCount,
                ConditionId = ConditionId
            };
        }
    }

    public sealed class LootAddRowPostModel : LootMutationPostModel
    {
        public LootRowPostValues Row { get; set; } = new LootRowPostValues();
    }

    public sealed class LootEditRowPostModel : LootMutationPostModel
    {
        public LootRowPostValues Original { get; set; } = new LootRowPostValues();
        public LootRowPostValues Replacement { get; set; } = new LootRowPostValues();
    }

    public sealed class LootDeleteRowPostModel : LootMutationPostModel
    {
        public LootRowPostValues Original { get; set; } = new LootRowPostValues();
    }

    public sealed class GossipViewerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string SourceFilter { get; set; } = GossipSourceFilters.All;
        public string ReturnUrl { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public bool HasGossipMenuTable
        {
            get; set;
        }

        public bool HasGossipOptionTable
        {
            get; set;
        }

        public bool HasNpcTextTable
        {
            get; set;
        }

        public bool HasCreatureSources
        {
            get; set;
        }

        public bool HasGameObjectSources
        {
            get; set;
        }

        public PagedResult<GossipMenuSummary> Menus { get; set; } = new PagedResult<GossipMenuSummary>();
        public PagedResult<GossipSourceSummary> Sources { get; set; } = new PagedResult<GossipSourceSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public GossipMenuDetail? SelectedMenu
        {
            get; set;
        }

        public GossipEditorCapabilities EditorCapabilities { get; set; } = new GossipEditorCapabilities();
        public bool CanAddMenuRows
        {
            get; set;
        }

        public bool CanEditMenuRows
        {
            get; set;
        }

        public bool CanDeleteMenuRows
        {
            get; set;
        }

        public bool CanAddOptions
        {
            get; set;
        }

        public bool CanEditOptions
        {
            get; set;
        }

        public bool CanDeleteOptions
        {
            get; set;
        }

        public bool CanAddCreatureSources
        {
            get; set;
        }
    }

    public sealed class GossipAddCreatureSourcePostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string SourceFilter { get; set; } = GossipSourceFilters.All;
        public string ReturnUrl { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public long CreatureEntry
        {
            get; set;
        }

        public long MenuId
        {
            get; set;
        }
    }

    public abstract class GossipMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TextPage { get; set; } = 1;
        public int TextPageSize { get; set; } = 20;
        public int OptionPage { get; set; } = 1;
        public int OptionPageSize { get; set; } = 20;
    }

    public sealed class GossipMenuRowPostValues
    {
        public long TextId
        {
            get; set;
        }

        public long? ScriptId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        // Handle to domain within the world editor models workflow.
        public GossipMenuRowValues ToDomain(long entry) => new()
        {
            Entry = entry,
            TextId = TextId,
            ScriptId = ScriptId,
            ConditionId = ConditionId
        };
    }

    public sealed class GossipOptionPostValues
    {
        public long Id
        {
            get; set;
        }

        public long? OptionIcon
        {
            get; set;
        }

        public string OptionText { get; set; } = string.Empty;
        public long? OptionBroadcastTextId
        {
            get; set;
        }

        public long? OptionId
        {
            get; set;
        }

        public long? NpcOptionNpcFlag
        {
            get; set;
        }

        public long? ActionMenuId
        {
            get; set;
        }

        public long? ActionPoiId
        {
            get; set;
        }

        public long? ActionScriptId
        {
            get; set;
        }

        public bool? BoxCoded
        {
            get; set;
        }

        public long? BoxMoney
        {
            get; set;
        }

        public string BoxText { get; set; } = string.Empty;
        public long? BoxBroadcastTextId
        {
            get; set;
        }

        public long? ConditionId
        {
            get; set;
        }

        // Handle to domain within the world editor models workflow.
        public GossipOptionValues ToDomain(long menuId) => new()
        {
            MenuId = menuId,
            Id = Id,
            OptionIcon = OptionIcon,
            OptionText = OptionText,
            OptionBroadcastTextId = OptionBroadcastTextId,
            OptionId = OptionId,
            NpcOptionNpcFlag = NpcOptionNpcFlag,
            ActionMenuId = ActionMenuId,
            ActionPoiId = ActionPoiId,
            ActionScriptId = ActionScriptId,
            BoxCoded = BoxCoded,
            BoxMoney = BoxMoney,
            BoxText = BoxText,
            BoxBroadcastTextId = BoxBroadcastTextId,
            ConditionId = ConditionId
        };
    }

    public sealed class GossipAddMenuRowPostModel : GossipMutationPostModel
    {
        public long NewEntry
        {
            get; set;
        }

        public GossipMenuRowPostValues Row { get; set; } = new GossipMenuRowPostValues();
    }

    public sealed class GossipEditMenuRowPostModel : GossipMutationPostModel
    {
        public GossipMenuRowPostValues Original { get; set; } = new GossipMenuRowPostValues();
        public GossipMenuRowPostValues Replacement { get; set; } = new GossipMenuRowPostValues();
    }

    public sealed class GossipDeleteMenuRowPostModel : GossipMutationPostModel
    {
        public GossipMenuRowPostValues Original { get; set; } = new GossipMenuRowPostValues();
    }

    public sealed class GossipOptionFieldsViewModel
    {
        public string Prefix { get; set; } = "Option";
        public GossipEditorCapabilities Capabilities { get; set; } = new GossipEditorCapabilities();
    }

    public sealed class GossipAddOptionPostModel : GossipMutationPostModel
    {
        public GossipOptionPostValues Option { get; set; } = new GossipOptionPostValues();
    }

    public sealed class GossipEditOptionPostModel : GossipMutationPostModel
    {
        public GossipOptionPostValues Original { get; set; } = new GossipOptionPostValues();
        public GossipOptionPostValues Replacement { get; set; } = new GossipOptionPostValues();
    }

    public sealed class GossipDeleteOptionPostModel : GossipMutationPostModel
    {
        public GossipOptionPostValues Original { get; set; } = new GossipOptionPostValues();
    }

    public sealed class PoolViewerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string Population { get; set; } = PoolPopulationFilters.All;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public IReadOnlyList<string> AvailableTables { get; set; } = new List<string>();
        public PagedResult<PoolSummary> Pools { get; set; } = new PagedResult<PoolSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public PoolDetail? SelectedPool
        {
            get; set;
        }

        public PoolEditorCapabilities EditorCapabilities { get; set; } = new PoolEditorCapabilities();
        public bool CanCreatePools
        {
            get; set;
        }

        public bool CanEditPools
        {
            get; set;
        }

        public bool CanDeletePools
        {
            get; set;
        }

        public bool CanAddMembers
        {
            get; set;
        }

        public bool CanEditMembers
        {
            get; set;
        }

        public bool CanRemoveMembers
        {
            get; set;
        }
    }

    public abstract class PoolMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public string Population { get; set; } = PoolPopulationFilters.All;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int MemberPage { get; set; } = 1;
        public int MemberPageSize { get; set; } = 20;
    }

    public sealed class PoolCreatePostModel : PoolMutationPostModel
    {
        public long NewEntry
        {
            get; set;
        }

        public int? MaxLimit
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolEditPostModel : PoolMutationPostModel
    {
        public int? MaxLimit
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolDeletePostModel : PoolMutationPostModel
    {
    }

    public sealed class PoolMemberPostModel : PoolMutationPostModel
    {
        public long? MembershipPoolEntry
        {
            get; set;
        }

        public string Source { get; set; } = string.Empty;
        public long MemberId
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class PoolEditMemberPostModel : PoolMutationPostModel
    {
        public string Source { get; set; } = string.Empty;
        public long OriginalMemberId
        {
            get; set;
        }

        public long MemberId
        {
            get; set;
        }

        public double? Chance
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class QuestViewerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public IReadOnlyList<string> AvailableTables { get; set; } = new List<string>();
        public PagedResult<QuestSummary> Quests { get; set; } = new PagedResult<QuestSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public QuestDetail? SelectedQuest
        {
            get; set;
        }

        public QuestEditorCapabilities EditorCapabilities { get; set; } = new QuestEditorCapabilities();
        public bool CanCreateQuests
        {
            get; set;
        }

        public bool CanEditQuests
        {
            get; set;
        }

        public bool CanDeleteQuests
        {
            get; set;
        }

        public bool CanAddRelations
        {
            get; set;
        }

        public bool CanEditRelations
        {
            get; set;
        }

        public bool CanRemoveRelations
        {
            get; set;
        }
    }

    public abstract class QuestMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int RelationPage { get; set; } = 1;
        public int RelationPageSize { get; set; } = 20;
    }

    public sealed class QuestCreatePostModel : QuestMutationPostModel
    {
        public long NewEntry
        {
            get; set;
        }

        public string Title { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Objectives { get; set; } = string.Empty;
        public long? MinLevel
        {
            get; set;
        }

        public long? QuestLevel
        {
            get; set;
        }

        public long? PrevQuestId
        {
            get; set;
        }

        public long? NextQuestId
        {
            get; set;
        }

        public long? ExclusiveGroup
        {
            get; set;
        }

        public long? NextQuestInChain
        {
            get; set;
        }
    }

    public sealed class QuestEditPostModel : QuestMutationPostModel
    {
        public string Title { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Objectives { get; set; } = string.Empty;
        public long? MinLevel
        {
            get; set;
        }

        public long? QuestLevel
        {
            get; set;
        }

        public long? PrevQuestId
        {
            get; set;
        }

        public long? NextQuestId
        {
            get; set;
        }

        public long? ExclusiveGroup
        {
            get; set;
        }

        public long? NextQuestInChain
        {
            get; set;
        }
    }

    public sealed class QuestDeletePostModel : QuestMutationPostModel
    {
    }

    public sealed class QuestRelationPostModel : QuestMutationPostModel
    {
        public string Kind { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }
    }

    public sealed class QuestEditRelationPostModel : QuestMutationPostModel
    {
        public string OriginalKind { get; set; } = string.Empty;
        public long OriginalEntityEntry
        {
            get; set;
        }

        public string Kind { get; set; } = string.Empty;
        public long EntityEntry
        {
            get; set;
        }
    }

    public sealed class EventViewerViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool IsAvailable
        {
            get; set;
        }

        public IReadOnlyList<string> AvailableTables { get; set; } = new List<string>();
        public PagedResult<EventSummary> Events { get; set; } = new PagedResult<EventSummary>();
        public long? SelectedEntry
        {
            get; set;
        }

        public EventDetail? SelectedEvent
        {
            get; set;
        }

        public EventEditorCapabilities EditorCapabilities { get; set; } = new EventEditorCapabilities();
        public bool CanCreateEvents
        {
            get; set;
        }

        public bool CanEditEvents
        {
            get; set;
        }

        public bool CanDeleteEvents
        {
            get; set;
        }

        public bool CanAddMembers
        {
            get; set;
        }

        public bool CanEditMembers
        {
            get; set;
        }

        public bool CanRemoveMembers
        {
            get; set;
        }
    }

    public abstract class EventMutationPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public long Entry
        {
            get; set;
        }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int MemberPage { get; set; } = 1;
        public int MemberPageSize { get; set; } = 20;
    }

    public sealed class EventCreatePostModel : EventMutationPostModel
    {
        public long NewEntry
        {
            get; set;
        }

        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public long? Occurrence
        {
            get; set;
        }

        public long? Length
        {
            get; set;
        }

        public long? Holiday
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class EventEditPostModel : EventMutationPostModel
    {
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public long? Occurrence
        {
            get; set;
        }

        public long? Length
        {
            get; set;
        }

        public long? Holiday
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class EventDeletePostModel : EventMutationPostModel
    {
    }

    public sealed class EventMemberPostModel : EventMutationPostModel
    {
        public string Source { get; set; } = string.Empty;
        public long Guid
        {
            get; set;
        }

        public long EventValue
        {
            get; set;
        }
    }

    public sealed class EventEditMemberPostModel : EventMutationPostModel
    {
        public string Source { get; set; } = string.Empty;
        public long OriginalGuid
        {
            get; set;
        }

        public long OriginalEventValue
        {
            get; set;
        }

        public long Guid
        {
            get; set;
        }

        public long EventValue
        {
            get; set;
        }
    }

    public sealed class DoubleSpawnViewModel
    {
        public string Database { get; set; } = string.Empty;
        public string CatalogName { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public bool HasSearched
        {
            get; set;
        }

        public bool HasCreatureTable
        {
            get; set;
        }

        public bool HasGameObjectTable
        {
            get; set;
        }

        public bool HasCreatureEventTable
        {
            get; set;
        }

        public bool HasGameObjectEventTable
        {
            get; set;
        }

        public string Scope { get; set; } = DoubleSpawnScopes.Both;
        public string MatchMode { get; set; } = DoubleSpawnMatchModes.SameEntry;
        public string PoolFilter { get; set; } = DoubleSpawnPoolFilters.All;
        public string LinkedGameObjectFilter { get; set; } = DoubleSpawnLinkedGameObjectFilters.All;
        public string Search { get; set; } = string.Empty;
        public int? MapId
        {
            get; set;
        }

        public double DistanceTolerance { get; set; } = 0.1d;
        public bool ExcludeEventRelated { get; set; } = true;
        public IReadOnlyList<int> MapIds { get; set; } = new List<int>();
        public DoubleSpawnSearchResult Result { get; set; } = new DoubleSpawnSearchResult();

        public bool IsAvailable
        {
            get
            {
                return HasCreatureTable || HasGameObjectTable;
            }
        }
    }

    public sealed class TableBrowseViewModel
    {
        public TableMetadata Table { get; set; } = new TableMetadata();
        public PagedResult<TableRow> Page { get; set; } = new PagedResult<TableRow>();
        public string Search { get; set; } = string.Empty;
        public string Sort { get; set; } = string.Empty;
        public bool Descending
        {
            get; set;
        }

        public string DisplayName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = "bi-table";
        public int VisibleColumnCount { get; set; } = 9;
        public int CellPreviewLength { get; set; } = 80;
        public bool ShowTechnicalColumnNames { get; set; } = true;
        public bool CanBrowseDatabaseStructure
        {
            get; set;
        }

        public bool CanCreate
        {
            get; set;
        }

        public bool CanEdit
        {
            get; set;
        }

        public bool CanDelete
        {
            get; set;
        }
    }

    public sealed class TableEditViewModel
    {
        public TableMetadata Table { get; set; } = new TableMetadata();
        public IDictionary<string, string?> Values { get; set; } = new Dictionary<string, string?>();
        public ISet<string> NullColumns { get; set; } = new HashSet<string>();
        public string KeyToken { get; set; } = string.Empty;
        public string ReturnToken { get; set; } = string.Empty;
        public string SubmitAction { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public bool IsCreate
        {
            get; set;
        }

        public string DisplayName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = "bi-table";
        public string RecordTitle { get; set; } = string.Empty;
        public string RecordIdentifier { get; set; } = string.Empty;
        public MangosSchemaProfile SchemaProfile { get; set; } = new MangosSchemaProfile();
        public IReadOnlyDictionary<string, MangosFieldEditorDefinition> FieldEditors
        {
            get; set;
        } =
            new Dictionary<string, MangosFieldEditorDefinition>();
        public IReadOnlyList<WorldEditorFieldGroup> FieldGroups { get; set; } = new List<WorldEditorFieldGroup>();
        public IReadOnlyList<WorldEditorPreviewField> PreviewFields { get; set; } = new List<WorldEditorPreviewField>();
    }

    public sealed class WorldEditorFieldGroup
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-sliders";
        public string Description { get; set; } = string.Empty;
        public IReadOnlyList<ColumnMetadata> Columns { get; set; } = new List<ColumnMetadata>();
    }

    public sealed class WorldEditorPreviewField
    {
        public string ColumnName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class TableEditPostModel
    {
        public string Database { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string KeyToken { get; set; } = string.Empty;
        public string ReturnToken { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public Dictionary<string, string?> Values { get; set; } = new Dictionary<string, string?>();
        public string[] NullColumns { get; set; } = System.Array.Empty<string>();
        public string ChangeSql { get; set; } = string.Empty;
    }
}
