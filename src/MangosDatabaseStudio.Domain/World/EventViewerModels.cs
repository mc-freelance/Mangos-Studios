// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System;
using System.Collections.Generic;
using MangosDatabaseStudio.Domain.Schema;

namespace MangosDatabaseStudio.Domain.World
{
    public static class EventMemberSources
    {
        public const string Creature = "game_event_creature";
        public const string GameObject = "game_event_gameobject";
    }

    public sealed class EventSummary
    {
        public long Entry
        {
            get; set;
        }

        public string Description { get; set; } = string.Empty;
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

        public long CreatureCount
        {
            get; set;
        }

        public long GameObjectCount
        {
            get; set;
        }

        public long NegativeLinkCount
        {
            get; set;
        }

        public long TotalMemberCount => CreatureCount + GameObjectCount;
    }

    public sealed class EventMember
    {
        public string Source { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public long Guid
        {
            get; set;
        }

        public long EventValue
        {
            get; set;
        }

        public long? EntityEntry
        {
            get; set;
        }

        public string ResolvedName { get; set; } = string.Empty;
        public bool IsResolved
        {
            get; set;
        }

        public long? MapId
        {
            get; set;
        }

        public bool ActiveDuringEvent => EventValue > 0;
    }

    public sealed class EventDetail
    {
        public EventSummary Event { get; set; } = new EventSummary();
        public PagedResult<EventMember> Members { get; set; } = new PagedResult<EventMember>();
    }

    public sealed class EventMemberSourceCapability
    {
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string MemberLabel { get; set; } = "Spawn GUID";
        public bool SupportsNegativeEvent
        {
            get; set;
        }
    }

    public sealed class EventEditorCapabilities
    {
        public string EntryColumn { get; set; } = "entry";
        public string? StartTimeColumn
        {
            get; set;
        }

        public string? EndTimeColumn
        {
            get; set;
        }

        public string? OccurrenceColumn
        {
            get; set;
        }

        public string? LengthColumn
        {
            get; set;
        }

        public string? HolidayColumn
        {
            get; set;
        }

        public string? DescriptionColumn
        {
            get; set;
        }

        public IReadOnlyList<EventMemberSourceCapability> MemberSources { get; set; } = Array.Empty<EventMemberSourceCapability>();
        public bool CanMutateEvents
        {
            get; set;
        }

        public bool HasStartTime => !string.IsNullOrWhiteSpace(StartTimeColumn);
        public bool HasEndTime => !string.IsNullOrWhiteSpace(EndTimeColumn);
        public bool HasOccurrence => !string.IsNullOrWhiteSpace(OccurrenceColumn);
        public bool HasLength => !string.IsNullOrWhiteSpace(LengthColumn);
        public bool HasHoliday => !string.IsNullOrWhiteSpace(HolidayColumn);
        public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionColumn);
    }

    public sealed class EventTemplateValues
    {
        public long Entry
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

    public sealed class EventMemberValues
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

    public sealed class EventMutationResult
    {
        public int AffectedRows
        {
            get; set;
        }

        public string ObjectName { get; set; } = string.Empty;
        public string SqlPreview { get; set; } = string.Empty;
        public string RevertSqlPreview { get; set; } = string.Empty;
    }
}
