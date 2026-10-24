// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Security.Claims;
using MangosDatabaseStudio.Application.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class OperationProgressReporter
    {
        public const string HeaderName = "X-MDS-Operation-Id";
        public const string QueryName = "mdsOperationId";

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly OperationProgressStore _store;
        private string _owner = string.Empty;
        private string _operationId = string.Empty;

        // Initialize the operation progress reporter dependencies and state.
        public OperationProgressReporter(IHttpContextAccessor httpContextAccessor, OperationProgressStore store)
        {
            _httpContextAccessor = httpContextAccessor;
            _store = store;
        }

        public bool IsActive => !string.IsNullOrEmpty(_operationId);

        // Handle begin within the operation progress reporter workflow.
        public IProgress<OperationProgressUpdate>? Begin(string stage, string detail)
        {
            HttpContext? context = _httpContextAccessor.HttpContext;
            if (context == null)
            {
                return null;
            }

            string operationId = ResolveOperationId(context.Request);
            string owner = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (!OperationProgressStore.IsValidOperationId(operationId) || string.IsNullOrWhiteSpace(owner))
            {
                return null;
            }

            _operationId = operationId;
            _owner = owner;
            _store.Begin(owner, operationId, stage, detail);
            return new DelegateProgress(update => _store.Report(_owner, _operationId, update));
        }

        // Handle report within the operation progress reporter workflow.
        public void Report(string stage, string detail = "", long? current = null, long? total = null, string unit = "")
        {
            if (!IsActive)
            {
                return;
            }

            _store.Report(_owner, _operationId, new OperationProgressUpdate(stage, detail, current, total, unit));
        }

        // Handle complete within the operation progress reporter workflow.
        public void Complete(string detail = "")
        {
            if (!IsActive)
            {
                return;
            }

            _store.Complete(_owner, _operationId, detail);
        }

        // Handle fail within the operation progress reporter workflow.
        public void Fail(string detail)
        {
            if (!IsActive)
            {
                return;
            }

            _store.Fail(_owner, _operationId, detail);
        }

        // Resolve operation id according to the available model or service contract.
        private static string ResolveOperationId(HttpRequest request)
        {
            if (request.Headers.TryGetValue(HeaderName, out StringValues headerValue))
            {
                string header = headerValue.ToString().Trim();
                if (OperationProgressStore.IsValidOperationId(header))
                {
                    return header;
                }
            }

            if (request.Query.TryGetValue(QueryName, out StringValues queryValue))
            {
                string query = queryValue.ToString().Trim();
                if (OperationProgressStore.IsValidOperationId(query))
                {
                    return query;
                }
            }

            return string.Empty;
        }

        private sealed class DelegateProgress : IProgress<OperationProgressUpdate>
        {
            private readonly Action<OperationProgressUpdate> _report;

            // Handle delegate progress within the operation progress reporter workflow.
            public DelegateProgress(Action<OperationProgressUpdate> report)
            {
                _report = report;
            }

            // Handle report within the operation progress reporter workflow.
            public void Report(OperationProgressUpdate value)
            {
                _report(value);
            }
        }
    }
}
