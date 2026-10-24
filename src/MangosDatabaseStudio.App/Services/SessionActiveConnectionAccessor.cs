// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Domain.Connections;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SessionActiveConnectionAccessor : IActiveConnectionAccessor
    {
        private const string SessionKey = "ActiveConnectionId";
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConnectionProfileStore _store;

        // Initialize the session active connection accessor dependencies and state.
        public SessionActiveConnectionAccessor(IHttpContextAccessor httpContextAccessor, IConnectionProfileStore store)
        {
            _httpContextAccessor = httpContextAccessor;
            _store = store;
        }

        public Guid? ActiveConnectionId
        {
            get
            {
                string? value = _httpContextAccessor.HttpContext?.Session.GetString(SessionKey);
                return Guid.TryParse(value, out Guid id) ? id : null;
            }

            set
            {
                ISession? session = _httpContextAccessor.HttpContext?.Session;
                if (session == null)
                {
                    return;
                }

                if (value.HasValue)
                {
                    session.SetString(SessionKey, value.Value.ToString("D"));
                }
                else
                {
                    session.Remove(SessionKey);
                }
            }
        }

        // Read active according to the available model or service contract.
        public async Task<ConnectionProfile?> GetActiveAsync(CancellationToken cancellationToken)
        {
            Guid? id = ActiveConnectionId;
            return id.HasValue ? await _store.GetAsync(id.Value, cancellationToken) : null;
        }
    }
}
