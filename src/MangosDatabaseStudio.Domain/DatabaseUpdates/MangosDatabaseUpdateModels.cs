// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// MaNGOS database update discovery and execution contracts.

using System;
using System.Collections.Generic;

namespace MangosDatabaseStudio.Domain.DatabaseUpdates
{
    public sealed class MangosDatabaseVersion : IEquatable<MangosDatabaseVersion>
    {
        public string Version { get; set; } = string.Empty;
        public string Structure { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Version) &&
                               string.IsNullOrWhiteSpace(Structure) &&
                               string.IsNullOrWhiteSpace(Content);

        public string DisplayValue => Version + " / " + Structure + " / " + Content;

        // Compare this model with another instance using its value identity.
        public bool Equals(MangosDatabaseVersion? other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(NormalizeComponent(Version), NormalizeComponent(other.Version), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizeComponent(Structure), NormalizeComponent(other.Structure), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizeComponent(Content), NormalizeComponent(other.Content), StringComparison.OrdinalIgnoreCase);
        }

        // Compare this model with another instance using its value identity.
        public override bool Equals(object? obj)
        {
            return obj is MangosDatabaseVersion other && Equals(other);
        }

        // Read hash code according to the available model or service contract.
        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(NormalizeComponent(Version), StringComparer.OrdinalIgnoreCase);
            hash.Add(NormalizeComponent(Structure), StringComparer.OrdinalIgnoreCase);
            hash.Add(NormalizeComponent(Content), StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }

        // Normalize component according to the available model or service contract.
        private static string NormalizeComponent(string? value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            // Official MaNGOS update scripts commonly zero-pad Structure and Content
            // (for example 04 / 029), while MySQL returns numeric db_version values
            // without padding (4 / 29). Treat both representations as the same
            // version without changing the value shown to the user.
            for (int i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] < '0' || normalized[i] > '9')
                {
                    return normalized;
                }
            }

            int firstNonZero = 0;
            while (firstNonZero < normalized.Length - 1 && normalized[firstNonZero] == '0')
            {
                firstNonZero++;
            }

            return normalized[firstNonZero..];
        }

        // Format the current value for logging, display, or diagnostics.
        public override string ToString()
        {
            return DisplayValue;
        }
    }

    public enum MangosDatabaseUpdatePatchStatus
    {
        Unrecognized = 0,
        Unrelated = 1,
        Applied = 2,
        Next = 3,
        Required = 4,
        Ambiguous = 5,
        Invalid = 6
    }

    public sealed class MangosDatabaseUpdatePatch
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSize
        {
            get; set;
        }

        public MangosDatabaseVersion ExpectedVersion { get; set; } = new MangosDatabaseVersion();
        public MangosDatabaseVersion NewVersion { get; set; } = new MangosDatabaseVersion();
        public string Description { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public MangosDatabaseUpdatePatchStatus Status { get; set; } = MangosDatabaseUpdatePatchStatus.Unrecognized;
        public string StatusDetail { get; set; } = string.Empty;
        public bool IsRecognized
        {
            get; set;
        }
    }

    public sealed class MangosDatabaseUpdatePlan
    {
        public string Database { get; set; } = string.Empty;
        public string SourceDirectory { get; set; } = string.Empty;
        public MangosDatabaseVersion? CurrentVersion
        {
            get; set;
        }

        public MangosDatabaseVersion? TargetVersion
        {
            get; set;
        }

        public IReadOnlyList<MangosDatabaseUpdatePatch> Patches { get; set; } = Array.Empty<MangosDatabaseUpdatePatch>();
        public IReadOnlyList<MangosDatabaseUpdatePatch> PendingPatches { get; set; } = Array.Empty<MangosDatabaseUpdatePatch>();
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        public int FilesDiscovered
        {
            get; set;
        }

        public int RecognizedFiles
        {
            get; set;
        }

        public int AppliedFiles
        {
            get; set;
        }

        public int UnrecognizedFiles
        {
            get; set;
        }

        public int UnrelatedFiles
        {
            get; set;
        }

        public bool CanExecute => CurrentVersion != null && PendingPatches.Count > 0 && Errors.Count == 0;
        public bool IsUpToDate => CurrentVersion != null && PendingPatches.Count == 0 && Errors.Count == 0;
    }

    public sealed class MangosDatabaseUpdatePatchResult
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public MangosDatabaseVersion ExpectedVersion { get; set; } = new MangosDatabaseVersion();
        public MangosDatabaseVersion NewVersion { get; set; } = new MangosDatabaseVersion();
        public bool Succeeded
        {
            get; set;
        }

        public string Message { get; set; } = string.Empty;
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public double DurationMilliseconds
        {
            get; set;
        }
    }

    public sealed class MangosDatabaseUpdateBatchResult
    {
        public string Database { get; set; } = string.Empty;
        public string SourceDirectory { get; set; } = string.Empty;
        public MangosDatabaseVersion? StartingVersion
        {
            get; set;
        }

        public MangosDatabaseVersion? FinalVersion
        {
            get; set;
        }

        public int PlannedPatchCount
        {
            get; set;
        }

        public bool Succeeded
        {
            get; set;
        }

        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<MangosDatabaseUpdatePatchResult> PatchResults { get; set; } = Array.Empty<MangosDatabaseUpdatePatchResult>();
    }
}
