// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Contracts and models for MaNGOS world data viewers and editors.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Connections;
using MangosDatabaseStudio.Domain.Schema;
using MangosDatabaseStudio.Domain.World;

namespace MangosDatabaseStudio.Application.World
{
    public interface IEventViewerService
    {
        Task<PagedResult<EventSummary>> SearchAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

        Task<EventDetail?> GetAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            TableMetadata? creature,
            TableMetadata? creatureTemplate,
            TableMetadata? gameObject,
            TableMetadata? gameObjectTemplate,
            long entry,
            int memberPage,
            int memberPageSize,
            CancellationToken cancellationToken);

        EventEditorCapabilities GetEditorCapabilities(
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject);

        Task<EventMutationResult> AddEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            EventTemplateValues values,
            CancellationToken cancellationToken);

        Task<EventMutationResult> UpdateEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            long entry,
            EventTemplateValues values,
            CancellationToken cancellationToken);

        Task<EventMutationResult> DeleteEventAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            TableMetadata? gameEventCreature,
            TableMetadata? gameEventGameObject,
            long entry,
            CancellationToken cancellationToken);

        Task<EventMutationResult> AddMemberAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            TableMetadata? creature,
            TableMetadata? gameObject,
            long eventEntry,
            EventMemberValues values,
            CancellationToken cancellationToken);

        Task<EventMutationResult> UpdateMemberAsync(
            ConnectionProfile profile,
            TableMetadata gameEvent,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            TableMetadata? creature,
            TableMetadata? gameObject,
            long eventEntry,
            EventMemberValues original,
            EventMemberValues replacement,
            CancellationToken cancellationToken);

        Task<EventMutationResult> RemoveMemberAsync(
            ConnectionProfile profile,
            IReadOnlyDictionary<string, TableMetadata> eventTables,
            EventMemberValues values,
            CancellationToken cancellationToken);
    }
}
