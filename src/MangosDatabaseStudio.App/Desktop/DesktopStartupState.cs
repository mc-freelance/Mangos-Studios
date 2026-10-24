// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Native desktop startup, session isolation, or optional device-identity support.

using System;

namespace MangosDatabaseStudio.App.Desktop
{
    internal sealed class DesktopStartupState
    {
        private readonly object _gate = new();
        private int _state;
        private int _progress;
        private string _status = "Starting application";
        private string _detail = "Preparing the secure desktop workspace...";
        private string? _errorMessage;

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

        // Read snapshot as part of the local desktop lifecycle.
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
                    _errorMessage);
            }
        }

        // Handle report within the desktop startup state workflow.
        public void Report(int progress, string status, string detail)
        {
            lock (_gate)
            {
                if (_state != 0)
                {
                    return;
                }

                int normalizedProgress = Math.Max(0, Math.Min(99, progress));
                if (normalizedProgress < _progress)
                {
                    normalizedProgress = _progress;
                }

                _progress = normalizedProgress;
                _status = string.IsNullOrWhiteSpace(status) ? _status : status;
                _detail = string.IsNullOrWhiteSpace(detail) ? _detail : detail;
            }
        }

        // Handle mark ready within the desktop startup state workflow.
        public void MarkReady()
        {
            lock (_gate)
            {
                _progress = 100;
                _status = "Startup complete";
                _detail = "MaNGOS Database Studio is ready. Opening the login workspace...";
                _state = 1;
            }
        }

        // Handle mark failed within the desktop startup state workflow.
        public void MarkFailed(Exception exception)
        {
            lock (_gate)
            {
                _status = "Startup failed";
                _detail = "The application could not finish initializing.";
                _errorMessage = exception.Message;
                _state = 2;
            }
        }
    }

    internal sealed class DesktopStartupSnapshot
    {
        // Handle desktop startup snapshot within the desktop startup state workflow.
        public DesktopStartupSnapshot(
            bool isReady,
            bool hasFailed,
            int progress,
            string status,
            string detail,
            string? errorMessage)
        {
            IsReady = isReady;
            HasFailed = hasFailed;
            Progress = progress;
            Status = status;
            Detail = detail;
            ErrorMessage = errorMessage;
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
    }
}
