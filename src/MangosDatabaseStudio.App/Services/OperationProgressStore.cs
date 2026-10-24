// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Concurrent;
using MangosDatabaseStudio.Application.Operations;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class OperationProgressStore
    {
        private static readonly TimeSpan CompletedRetention = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ActiveRetention = TimeSpan.FromHours(1);
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private long _cleanupCounter;

        // Handle begin within the operation progress store workflow.
        public void Begin(string owner, string operationId, string stage, string detail)
        {
            if (!IsValidOperationId(operationId) || string.IsNullOrWhiteSpace(owner))
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            _entries[operationId] = new Entry(owner, new OperationProgressSnapshot
            {
                OperationId = operationId,
                State = "running",
                Stage = stage ?? string.Empty,
                Detail = detail ?? string.Empty,
                StartedAtUtc = now,
                UpdatedAtUtc = now
            });
            MaybeCleanup(now);
        }

        // Handle report within the operation progress store workflow.
        public void Report(string owner, string operationId, OperationProgressUpdate update)
        {
            if (update == null || !_entries.TryGetValue(operationId, out Entry? entry) || !entry.IsOwnedBy(owner))
            {
                return;
            }

            entry.Update(snapshot =>
            {
                snapshot.State = "running";
                snapshot.Stage = update.Stage;
                snapshot.Detail = update.Detail;
                snapshot.Current = update.Current;
                snapshot.Total = update.Total;
                snapshot.Unit = update.Unit;
                snapshot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            });
        }

        // Handle complete within the operation progress store workflow.
        public void Complete(string owner, string operationId, string detail = "")
        {
            if (!_entries.TryGetValue(operationId, out Entry? entry) || !entry.IsOwnedBy(owner))
            {
                return;
            }

            entry.Update(snapshot =>
            {
                snapshot.State = "completed";
                snapshot.Stage = "Complete";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    snapshot.Detail = detail;
                }

                snapshot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            });
        }

        // Handle fail within the operation progress store workflow.
        public void Fail(string owner, string operationId, string detail)
        {
            if (!_entries.TryGetValue(operationId, out Entry? entry) || !entry.IsOwnedBy(owner))
            {
                return;
            }

            entry.Update(snapshot =>
            {
                snapshot.State = "failed";
                snapshot.Stage = "Operation stopped";
                snapshot.Detail = detail ?? string.Empty;
                snapshot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            });
        }

        // Attempt to get according to the available model or service contract.
        public bool TryGet(string owner, string operationId, out OperationProgressSnapshot snapshot)
        {
            snapshot = new OperationProgressSnapshot();
            if (!IsValidOperationId(operationId) || !_entries.TryGetValue(operationId, out Entry? entry) || !entry.IsOwnedBy(owner))
            {
                return false;
            }

            snapshot = entry.Copy();
            return true;
        }

        // Determine whether valid operation id according to the available model or service contract.
        public static bool IsValidOperationId(string? operationId)
        {
            return !string.IsNullOrWhiteSpace(operationId) && Guid.TryParseExact(operationId, "N", out _);
        }

        // Handle maybe cleanup within the operation progress store workflow.
        private void MaybeCleanup(DateTimeOffset now)
        {
            if (System.Threading.Interlocked.Increment(ref _cleanupCounter) % 32 != 0)
            {
                return;
            }

            foreach (var pair in _entries)
            {
                OperationProgressSnapshot snapshot = pair.Value.Copy();
                TimeSpan age = now - snapshot.UpdatedAtUtc;
                bool expired = string.Equals(snapshot.State, "running", StringComparison.OrdinalIgnoreCase)
                    ? age > ActiveRetention
                    : age > CompletedRetention;
                if (expired)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }
        }

        private sealed class Entry
        {
            private readonly object _sync = new();
            private readonly string _owner;
            private readonly OperationProgressSnapshot _snapshot;

            // Handle entry within the operation progress store workflow.
            public Entry(string owner, OperationProgressSnapshot snapshot)
            {
                _owner = owner;
                _snapshot = snapshot;
            }

            // Determine whether owned by according to the available model or service contract.
            public bool IsOwnedBy(string owner) => string.Equals(_owner, owner, StringComparison.Ordinal);

            // Apply a change to the tracked progress snapshot safely.
            public void Update(Action<OperationProgressSnapshot> update)
            {
                lock (_sync)
                {
                    update(_snapshot);
                }
            }

            // Handle copy within the operation progress store workflow.
            public OperationProgressSnapshot Copy()
            {
                lock (_sync)
                {
                    return new OperationProgressSnapshot
                    {
                        OperationId = _snapshot.OperationId,
                        State = _snapshot.State,
                        Stage = _snapshot.Stage,
                        Detail = _snapshot.Detail,
                        Current = _snapshot.Current,
                        Total = _snapshot.Total,
                        Unit = _snapshot.Unit,
                        StartedAtUtc = _snapshot.StartedAtUtc,
                        UpdatedAtUtc = _snapshot.UpdatedAtUtc
                    };
                }
            }
        }
    }

    public sealed class OperationProgressSnapshot
    {
        public string OperationId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public long? Current
        {
            get; set;
        }

        public long? Total
        {
            get; set;
        }

        public string Unit { get; set; } = string.Empty;
        public DateTimeOffset StartedAtUtc
        {
            get; set;
        }

        public DateTimeOffset UpdatedAtUtc
        {
            get; set;
        }
    }
}
