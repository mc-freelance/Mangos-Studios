// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MangosDatabaseStudio.App.Services
{
    public interface IUserAvatarStorage
    {
        Task<string> SaveAsync(Guid userId, IFormFile file, CancellationToken cancellationToken);
        AvatarFile? Open(string fileName);
        void Delete(string? fileName);
    }

    public sealed class AvatarFile : IDisposable
    {
        // Handle avatar file within the iuser avatar storage workflow.
        public AvatarFile(Stream stream, string contentType)
        {
            Stream = stream;
            ContentType = contentType;
        }

        public Stream Stream
        {
            get;
        }

        public string ContentType
        {
            get;
        }

        // Handle dispose within the iuser avatar storage workflow.
        public void Dispose()
        {
            Stream.Dispose();
        }
    }
}
