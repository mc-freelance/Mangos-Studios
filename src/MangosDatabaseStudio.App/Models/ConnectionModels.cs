// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Request and view data contracts used by the desktop web interface.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MangosDatabaseStudio.Domain.Connections;

namespace MangosDatabaseStudio.App.Models
{
    public sealed class ConnectionProfileViewModel
    {
        public Guid Id
        {
            get; set;
        }

        [Required]
        [StringLength(80)]
        [Display(Name = "Profile name")]
        public string Name { get; set; } = "Local World Database";

        [Required]
        [StringLength(255)]
        [Display(Name = "Host or IP address")]
        public string Host { get; set; } = "127.0.0.1";

        [Range(1, 65535)]
        public uint Port { get; set; } = 3306;

        [Required]
        [StringLength(128)]
        public string Username { get; set; } = "mangos";

        // Nullable by design. Existing profiles may leave this field blank to
        // preserve the password that is already stored in the application DB.
        [DataType(DataType.Password)]
        public string? Password
        {
            get; set;
        }

        [Required]
        [StringLength(128)]
        public string DefaultDatabase { get; set; } = "mangos_world";

        [Required]
        public string SslMode { get; set; } = "Preferred";

        // Handle to domain within the connection models workflow.
        public ConnectionProfile ToDomain()
        {
            return new ConnectionProfile
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                Name = Name.Trim(),
                Host = Host.Trim(),
                Port = Port,
                Username = Username.Trim(),
                Password = Password ?? string.Empty,
                DefaultDatabase = DefaultDatabase.Trim(),
                SslMode = SslMode
            };
        }

        // Handle from domain within the connection models workflow.
        public static ConnectionProfileViewModel FromDomain(ConnectionProfile profile)
        {
            return new ConnectionProfileViewModel
            {
                Id = profile.Id,
                Name = profile.Name,
                Host = profile.Host,
                Port = profile.Port,
                Username = profile.Username,
                Password = null,
                DefaultDatabase = profile.DefaultDatabase,
                SslMode = profile.SslMode
            };
        }
    }

    public sealed class ConnectionsIndexViewModel
    {
        public IReadOnlyList<ConnectionProfile> Profiles { get; set; } = new List<ConnectionProfile>();
        public Guid? ActiveId
        {
            get; set;
        }

        public bool CanSelect
        {
            get; set;
        }

        public bool CanTest
        {
            get; set;
        }

        public bool CanManage
        {
            get; set;
        }
    }
}
