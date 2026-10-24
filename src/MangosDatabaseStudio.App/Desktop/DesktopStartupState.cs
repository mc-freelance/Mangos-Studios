// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Track the desktop initialization gate, Field Data progress, and safe recovery advice.

using System;
using System.Collections.Generic;
using System.Linq;
using MangosDatabaseStudio.App.Services.FieldData;

namespace MangosDatabaseStudio.App.Desktop
{
    internal sealed class DesktopStartupState
    {
        private static readonly string[] FieldDataRecoverySteps =
        {
            "Close Studio and back up App_Data/database/mangos-studio.db together with App_Data/keys/application-database.key before changing either file.",
            "Check that the disk has free space, that App_Data is writable by your user, and that another process is not holding the database open.",
            "If you recently changed C# Field Data sources, inspect the reported field for duplicate values, invalid bitmask flags, or overlapping expansion mappings. Correct the source and rebuild.",
            "If the SQLite database is damaged, restore the database AND its matching original encryption key from a known-good backup. Do not replace the key or delete the database to fix an upgrade.",
            "Restart Studio after addressing the issue. If it still fails, copy the diagnostic details and report the error without uploading your database or encryption keys."
        };

        private static readonly string[] GeneralRecoverySteps =
        {
            "Check the diagnostic message below and make sure the installation and App_Data directory are writable.",
            "Keep the encrypted database and its matching encryption key together when making backups or restoring files.",
            "Restart Studio after resolving the underlying problem. If the error continues, copy the diagnostic details for a bug report; never share your encryption keys."
        };

        private readonly object _gate = new();
        private int _state;
        private int _progress;
        private string _status = "Starting application";
        private string _detail = "Preparing the secure desktop workspace...";
        private string? _errorMessage;
        private string? _failureArea;
        private IReadOnlyList<string> _recoverySteps = Array.Empty<string>();
        private bool _fieldDataVisible;
        private int _fieldDataCompleted;
        private int _fieldDataTotal;
        private string _fieldDataCurrent = "";

        public bool IsReady
        {
            get
            {
                lock (_gate)
                {
                    return _state == 1;
                }
            }
        }

        public bool HasFailed
        {
            get
            {
                lock (_gate)
                {
                    return _state == 2;
                }
            }
        }

        // Capture one consistent snapshot for the authenticated startup status endpoint.
        public DesktopStartupSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new DesktopStartupSnapshot(
                    _state == 1,
                    _state == 2,
                    _progress,
                    _status,
                    _detail,
                    _errorMessage,
                    _failureArea,
                    _recoverySteps,
                    _fieldDataVisible,
                    _fieldDataCompleted,
                    _fieldDataTotal,
                    _fieldDataCurrent);
            }
        }

        // Report ordinary startup stages without regressing the visible progress bar.
        public void Report(int progress, string status, string detail)
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return;
                }

                _progress = Math.Max(_progress, Math.Clamp(progress, 0, 99));
                _status = string.IsNullOrWhiteSpace(status) ? _status : status;
                _detail = string.IsNullOrWhiteSpace(detail) ? _detail : detail;
            }
        }

        // Display which coded definition is being reconciled and how many are complete.
        // Progress is capped below the final startup stages until SQLite commits successfully.
        public void ReportFieldData(int completed, int total, string current)
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return;
                }

                _fieldDataVisible = true;
                _fieldDataTotal = Math.Max(0, total);
                _fieldDataCompleted = Math.Clamp(completed, 0, _fieldDataTotal);
                _fieldDataCurrent = current;

                int denominator = Math.Max(1, _fieldDataTotal);
                _progress = Math.Max(_progress, 79 + (13 * _fieldDataCompleted / denominator));
                _status = _fieldDataCompleted == _fieldDataTotal
                    ? "Finalizing Field Data catalog"
                    : "Updating Field Data (" + _fieldDataCompleted + "/" + _fieldDataTotal + ")";
                _detail = current;
            }
        }

        // Only a successful completion of every initialization stage unlocks the workspace.
        public void MarkReady()
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return;
                }

                _progress = 100;
                _status = "Startup complete";
                _detail = "MaNGOS Database Studio is ready. Opening the login workspace...";
                _state = 1;
            }
        }

        // Any failure is terminal for this launch; do not open editors on partial data.
        public void MarkFailed(Exception exception)
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return;
                }

                bool fieldDataFailure = exception is FieldDataSynchronizationException;
                _failureArea = fieldDataFailure ? "Field Data synchronization" : "Application initialization";
                _status = fieldDataFailure ? "Field Data update failed" : "Startup failed";
                _detail = fieldDataFailure
                    ? "The Field Data update did not complete. Studio is locked to prevent access to incomplete catalog data."
                    : "The application could not finish initializing. Studio remains locked.";
                _errorMessage = FormatExceptionChain(exception);
                _recoverySteps = fieldDataFailure ? FieldDataRecoverySteps : GeneralRecoverySteps;
                _state = 2;
            }
        }

        // Include source-specific nested error messages but never copy a stack trace or secrets.
        private static string FormatExceptionChain(Exception exception)
        {
            List<string> messages = new();
            Exception? current = exception;

            while (current != null && messages.Count < 5)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) &&
                    !messages.Contains(current.Message, StringComparer.Ordinal))
                {
                    messages.Add(current.Message);
                }

                current = current.InnerException;
            }

            string message = string.Join(" Reason: ", messages);
            return message.Length > 2000 ? message[..2000] + "..." : message;
        }
    }

    internal sealed class DesktopStartupSnapshot
    {
        // Present a single immutable-in-practice startup state to the native window.
        public DesktopStartupSnapshot(
            bool isReady,
            bool hasFailed,
            int progress,
            string status,
            string detail,
            string? errorMessage,
            string? failureArea,
            IReadOnlyList<string> recoverySteps,
            bool fieldDataVisible,
            int fieldDataCompleted,
            int fieldDataTotal,
            string fieldDataCurrent)
        {
            IsReady = isReady;
            HasFailed = hasFailed;
            Progress = progress;
            Status = status;
            Detail = detail;
            ErrorMessage = errorMessage;
            FailureArea = failureArea;
            RecoverySteps = recoverySteps;
            FieldDataVisible = fieldDataVisible;
            FieldDataCompleted = fieldDataCompleted;
            FieldDataTotal = fieldDataTotal;
            FieldDataCurrent = fieldDataCurrent;
        }

        public bool IsReady
        {
            get;
        }

        public bool HasFailed
        {
            get;
        }

        public int Progress
        {
            get;
        }

        public string Status
        {
            get;
        }

        public string Detail
        {
            get;
        }

        public string? ErrorMessage
        {
            get;
        }

        public string? FailureArea
        {
            get;
        }

        public IReadOnlyList<string> RecoverySteps
        {
            get;
        }

        public bool FieldDataVisible
        {
            get;
        }

        public int FieldDataCompleted
        {
            get;
        }

        public int FieldDataTotal
        {
            get;
        }

        public string FieldDataCurrent
        {
            get;
        }
    }
}
