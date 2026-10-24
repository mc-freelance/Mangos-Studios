// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Initialize and open encrypted application data while preserving local keys.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.App.Services.FieldData;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class ApplicationDatabase
    {
        private const int CurrentSchemaVersion = 6;
        private const string MigrationMetadataKey = "legacy_json_migrated";
        private static readonly object NativeInitializationGate = new();
        private static bool _nativeInitialized;

        private readonly string _dataDirectory;
        private readonly string _databaseDirectory;
        private readonly string _keyDirectory;
        private readonly string _legacyUsersPath;
        private readonly string _legacyConnectionsPath;
        private readonly string _legacyRolePermissionsPath;
        private readonly IDataProtector _legacyConnectionProtector;
        private readonly ILogger<ApplicationDatabase> _logger;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private string? _databaseKey;
        private bool _initialized;

        // Initialize the application database dependencies and state.
        public ApplicationDatabase(
            IWebHostEnvironment environment,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<ApplicationDatabase> logger)
        {
            _dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
            _databaseDirectory = Path.Combine(_dataDirectory, "database");
            DatabasePath = Path.Combine(_databaseDirectory, "mangos-studio.db");
            _keyDirectory = Path.Combine(_dataDirectory, "keys");
            KeyPath = Path.Combine(_keyDirectory, "application-database.key");
            _legacyUsersPath = Path.Combine(_dataDirectory, "users.json");
            _legacyConnectionsPath = Path.Combine(_dataDirectory, "connections.json");
            _legacyRolePermissionsPath = Path.Combine(_dataDirectory, "role-permissions.json");
            _legacyConnectionProtector = dataProtectionProvider.CreateProtector("MangosDatabaseStudio.ConnectionPasswords.v1");
            _logger = logger;
        }

        public string DatabasePath
        {
            get;
        }

        public string KeyPath
        {
            get;
        }

        // Create or migrate encrypted SQLite application storage without overwriting an existing database key.
        public async Task InitializeAsync(
            CancellationToken cancellationToken,
            Action<int, string, string>? reportProgress = null,
            Action<int, int, string>? reportFieldDataProgress = null)
        {
            if (_initialized)
            {
                reportProgress?.Invoke(99, "Application data ready", "The encrypted application database is already initialized.");
                return;
            }

            reportProgress?.Invoke(12, "Preparing application data", "Checking the encrypted application data folders...");
            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (_initialized)
                {
                    reportProgress?.Invoke(99, "Application data ready", "The encrypted application database is already initialized.");
                    return;
                }

                Directory.CreateDirectory(_dataDirectory);
                Directory.CreateDirectory(_databaseDirectory);
                Directory.CreateDirectory(_keyDirectory);

                reportProgress?.Invoke(22, "Loading database security", "Initializing the SQLCipher runtime used by the local application database...");
                EnsureSqlCipherInitialized();

                bool databaseAlreadyExists = File.Exists(DatabasePath);
                if (databaseAlreadyExists && !File.Exists(KeyPath))
                {
                    throw new InvalidOperationException(
                        "The encrypted application database exists, but App_Data/keys/application-database.key is missing. " +
                        "Restore the original key file from the same installation. A replacement key cannot decrypt the existing database.");
                }

                reportProgress?.Invoke(32, "Loading encryption key", databaseAlreadyExists
                    ? "Validating the existing application database key..."
                    : "Creating the encryption key for the new application database...");
                _databaseKey = databaseAlreadyExists
                    ? ReadAndValidateKey()
                    : GetOrCreateKey();

                try
                {
                    reportProgress?.Invoke(42, "Opening application database", "Opening the encrypted SQLite workspace...");
                    await using SqliteConnection connection = CreateConnection(SqliteOpenMode.ReadWriteCreate);
                    await connection.OpenAsync(cancellationToken);

                    reportProgress?.Invoke(52, "Verifying database encryption", "Confirming SQLCipher and the local database are available...");
                    await VerifySqlCipherAsync(connection, cancellationToken);

                    reportProgress?.Invoke(60, "Configuring application database", "Applying connection and integrity settings...");
                    await ConfigureConnectionAsync(connection, cancellationToken);

                    reportProgress?.Invoke(70, "Checking application schema", "Creating or upgrading the local application database structure...");
                    await CreateSchemaAsync(connection, cancellationToken);

                    reportProgress?.Invoke(78, "Checking Field Data catalog",
                        "Discovering and validating expansion-aware dropdown and bitmask definitions...");

                    try
                    {
                        await MangosFieldDataSeed.ApplyAsync(
                            connection, cancellationToken, reportFieldDataProgress);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // The catalog transaction rolls back on failure. Keep startup blocked
                        // instead of allowing editors to use incomplete Field Data.
                        throw new FieldDataSynchronizationException(exception);
                    }

                    reportProgress?.Invoke(94, "Field Data catalog ready",
                        "All built-in Field Data checks and any required SQLite updates have completed.");
                    reportProgress?.Invoke(95, "Checking existing application data", "Migrating compatible data from earlier Studio storage when needed...");
                    await MigrateLegacyJsonAsync(connection, cancellationToken);
                }
                catch (SqliteException exception) when (databaseAlreadyExists)
                {
                    throw new InvalidOperationException(
                        "The encrypted application database could not be opened. The database key may be incorrect, " +
                        "or App_Data/database/mangos-studio.db may be damaged. The application did not replace either file.",
                        exception);
                }

                reportProgress?.Invoke(97, "Securing local application files", "Applying private file permissions to the encrypted database and key...");
                ApplyPrivateFilePermissions(KeyPath);
                ApplyPrivateFilePermissions(DatabasePath);
                _initialized = true;
                reportProgress?.Invoke(99, "Finalizing desktop workspace", "Finishing application startup...");
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        // Open an initialized, encrypted SQLite connection for application state.
        public async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            if (!_initialized || string.IsNullOrEmpty(_databaseKey))
            {
                throw new InvalidOperationException("The application database has not been initialized.");
            }

            SqliteConnection connection = CreateConnection(SqliteOpenMode.ReadWriteCreate);
            try
            {
                await connection.OpenAsync(cancellationToken);
                await ConfigureConnectionAsync(connection, cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        // Configure a SQLCipher connection using the installation-specific encryption key.
        private SqliteConnection CreateConnection(SqliteOpenMode mode)
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = DatabasePath,
                Mode = mode,
                Cache = SqliteCacheMode.Shared,
                Password = _databaseKey ?? throw new InvalidOperationException("The application database key is unavailable.")
            };

            return new SqliteConnection(builder.ToString());
        }

        // Ensure sql cipher initialized in encrypted local application storage.
        private static void EnsureSqlCipherInitialized()
        {
            lock (NativeInitializationGate)
            {
                if (_nativeInitialized)
                {
                    return;
                }

                SQLitePCL.Batteries_V2.Init();
                _nativeInitialized = true;
            }
        }

        // Read or create key in encrypted local application storage.
        private string GetOrCreateKey()
        {
            if (File.Exists(KeyPath))
            {
                return ReadAndValidateKey();
            }

            string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Directory.CreateDirectory(_keyDirectory);

            try
            {
                using FileStream stream = new(KeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] bytes = Encoding.UTF8.GetBytes(key + Environment.NewLine);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            catch (IOException) when (File.Exists(KeyPath))
            {
                return ReadAndValidateKey();
            }

            ApplyPrivateFilePermissions(KeyPath);
            return key;
        }

        // Read and validate key in encrypted local application storage.
        private string ReadAndValidateKey()
        {
            string key = File.ReadAllText(KeyPath, Encoding.UTF8).Trim();
            if (key.Length != 64)
            {
                throw new InvalidOperationException(
                    "App_Data/keys/application-database.key is invalid. Restore the original 64-character database key.");
            }

            try
            {
                byte[] decoded = Convert.FromHexString(key);
                if (decoded.Length != 32)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "App_Data/keys/application-database.key is invalid. Restore the original 64-character database key.",
                    exception);
            }

            return key;
        }

        // Handle verify sql cipher async within the application database workflow.
        private static async Task VerifySqlCipherAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA cipher_version;";
            object? version = await command.ExecuteScalarAsync(cancellationToken);
            if (version == null || string.IsNullOrWhiteSpace(Convert.ToString(version)))
            {
                throw new InvalidOperationException(
                    "SQLCipher did not initialize. Verify that the SQLitePCLRaw.bundle_e_sqlcipher native runtime files were published with the application.");
            }

            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            await command.ExecuteScalarAsync(cancellationToken);
        }

        // Configure connection in encrypted local application storage.
        private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create schema in encrypted local application storage.
        private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS application_metadata (
    name TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS application_users (
    id TEXT NOT NULL PRIMARY KEY,
    username TEXT NOT NULL COLLATE NOCASE UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL,
    email TEXT NOT NULL,
    avatar_mode TEXT NOT NULL,
    avatar_file_name TEXT NULL,
    preferences_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS connection_profiles (
    id TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    host TEXT NOT NULL,
    port INTEGER NOT NULL,
    username TEXT NOT NULL,
    password TEXT NOT NULL,
    default_database TEXT NOT NULL,
    ssl_mode TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS role_permissions (
    role TEXT NOT NULL,
    permission TEXT NOT NULL,
    PRIMARY KEY (role, permission)
);

CREATE TABLE IF NOT EXISTS database_update_sources (
    connection_id TEXT NOT NULL,
    database_name TEXT NOT NULL COLLATE NOCASE,
    source_directory TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (connection_id, database_name),
    FOREIGN KEY (connection_id) REFERENCES connection_profiles(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS field_categories (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    category_key TEXT NOT NULL COLLATE NOCASE UNIQUE,
    name TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    icon TEXT NOT NULL DEFAULT 'bi-collection',
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_enabled INTEGER NOT NULL DEFAULT 1 CHECK (is_enabled IN (0, 1))
);

CREATE TABLE IF NOT EXISTS field_definitions (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    source_key TEXT NOT NULL UNIQUE,
    table_name TEXT NOT NULL COLLATE NOCASE,
    column_name TEXT NOT NULL COLLATE NOCASE,
    editor_kind TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    is_system INTEGER NOT NULL DEFAULT 1 CHECK (is_system IN (0, 1)),
    is_enabled INTEGER NOT NULL DEFAULT 1 CHECK (is_enabled IN (0, 1)),
    priority INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS field_variants (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    field_definition_id INTEGER NOT NULL,
    variant_key TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (field_definition_id) REFERENCES field_definitions(id) ON DELETE CASCADE,
    UNIQUE (field_definition_id, variant_key)
);

CREATE TABLE IF NOT EXISTS field_variant_generations (
    field_variant_id INTEGER NOT NULL,
    generation INTEGER NOT NULL,
    PRIMARY KEY (field_variant_id, generation),
    FOREIGN KEY (field_variant_id) REFERENCES field_variants(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS field_options (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    field_variant_id INTEGER NOT NULL,
    value TEXT NOT NULL,
    label TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (field_variant_id) REFERENCES field_variants(id) ON DELETE CASCADE,
    UNIQUE (field_variant_id, value)
);

CREATE TABLE IF NOT EXISTS database_changes (
    id TEXT NOT NULL PRIMARY KEY,
    user_id TEXT NOT NULL,
    username TEXT NOT NULL,
    connection_id TEXT NOT NULL,
    connection_name TEXT NOT NULL,
    database_name TEXT NOT NULL COLLATE NOCASE,
    source TEXT NOT NULL,
    operation TEXT NOT NULL,
    object_name TEXT NOT NULL,
    summary TEXT NOT NULL,
    sql_text TEXT NOT NULL,
    revert_sql_text TEXT NOT NULL DEFAULT '',
    revert_expected_rows INTEGER NOT NULL DEFAULT 0,
    contribution_eligible INTEGER NOT NULL DEFAULT 0 CHECK (contribution_eligible IN (0, 1)),
    review_status TEXT NOT NULL DEFAULT 'pending' CHECK (review_status IN ('pending', 'approved', 'ignored')),
    created_at_utc TEXT NOT NULL,
    reviewed_at_utc TEXT NULL,
    reviewed_by_user_id TEXT NULL,
    reviewed_by_username TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS ix_connection_profiles_name ON connection_profiles(name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_database_update_sources_connection ON database_update_sources(connection_id, database_name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_field_definitions_lookup
    ON field_definitions(table_name COLLATE NOCASE, column_name COLLATE NOCASE, is_enabled, priority);
CREATE INDEX IF NOT EXISTS ix_field_variant_generations_generation
    ON field_variant_generations(generation, field_variant_id);
CREATE INDEX IF NOT EXISTS ix_field_options_variant_sort
    ON field_options(field_variant_id, sort_order, id);
CREATE INDEX IF NOT EXISTS ix_database_changes_connection_created
    ON database_changes(connection_id, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_database_changes_database_status
    ON database_changes(connection_id, database_name COLLATE NOCASE, review_status, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_database_changes_contribution
    ON database_changes(connection_id, database_name COLLATE NOCASE, contribution_eligible, review_status, created_at_utc);

INSERT INTO application_metadata(name, value)
VALUES ('schema_version', $schemaVersion)
ON CONFLICT(name) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);

            await EnsureColumnAsync(
                connection,
                "database_changes",
                "revert_sql_text",
                "TEXT NOT NULL DEFAULT ''",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "database_changes",
                "revert_expected_rows",
                "INTEGER NOT NULL DEFAULT 0",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "field_definitions",
                "category_id",
                "INTEGER NULL",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "field_definitions",
                "display_name",
                "TEXT NOT NULL DEFAULT ''",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "field_definitions",
                "selection_mode",
                "TEXT NOT NULL DEFAULT 'Single'",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "field_definitions",
                "value_handling",
                "TEXT NOT NULL DEFAULT 'Direct'",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "field_definitions",
                "preserve_unknown_values",
                "INTEGER NOT NULL DEFAULT 1",
                cancellationToken);

            command.Parameters.Clear();
            command.CommandText = @"
INSERT OR IGNORE INTO field_categories(category_key, name, description, icon, sort_order, is_enabled)
VALUES ('world.creature-template', 'Creature Template', 'Editor data for creature_template fields.', 'bi-person-badge', 10, 1);

UPDATE field_definitions
SET category_id = (SELECT id FROM field_categories WHERE category_key = 'world.creature-template')
WHERE category_id IS NULL AND table_name = 'creature_template' COLLATE NOCASE;

UPDATE field_definitions
SET display_name = column_name
WHERE length(trim(display_name)) = 0;

UPDATE field_definitions
SET selection_mode = CASE WHEN editor_kind = 'BitMask' THEN 'Multiple' ELSE 'Single' END,
    value_handling = CASE WHEN editor_kind = 'BitMask' THEN 'BitwiseOr' ELSE 'Direct' END
WHERE selection_mode = 'Single' AND value_handling = 'Direct';

CREATE INDEX IF NOT EXISTS ix_field_definitions_category
    ON field_definitions(category_id, table_name COLLATE NOCASE, column_name COLLATE NOCASE);
";
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.Parameters.Clear();
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Ensure column in encrypted local application storage.
        private static async Task EnsureColumnAsync(
            SqliteConnection connection,
            string tableName,
            string columnName,
            string columnDefinition,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand inspect = connection.CreateCommand();
            inspect.CommandText = "PRAGMA table_info(" + tableName + ");";
            await using SqliteDataReader reader = await inspect.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await reader.DisposeAsync();

            await using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE " + tableName + " ADD COLUMN " + columnName + " " + columnDefinition + ";";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        // Handle migrate legacy json async within the application database workflow.
        private async Task MigrateLegacyJsonAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            if (await HasMigrationMarkerAsync(connection, cancellationToken))
            {
                ArchiveLegacyFiles();
                return;
            }

            List<ApplicationUser> users = await ReadLegacyUsersAsync(cancellationToken);
            List<LegacyConnectionProfile> connections = await ReadLegacyConnectionsAsync(cancellationToken);
            Dictionary<string, List<string>> rolePermissions = await ReadLegacyRolePermissionsAsync(cancellationToken);

            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                foreach (ApplicationUser user in users)
                {
                    NormalizeLegacyUser(user);
                    await InsertLegacyUserAsync(connection, transaction, user, cancellationToken);
                }

                foreach (LegacyConnectionProfile profile in connections)
                {
                    await InsertLegacyConnectionAsync(connection, transaction, profile, cancellationToken);
                }

                foreach (KeyValuePair<string, List<string>> role in rolePermissions)
                {
                    if (!AppRoles.IsValid(role.Key))
                    {
                        continue;
                    }

                    IReadOnlyCollection<string> normalized = AppPermissions.NormalizeForRole(role.Key, role.Value ?? new List<string>());
                    foreach (string permission in normalized)
                    {
                        await InsertLegacyPermissionAsync(connection, transaction, role.Key, permission, cancellationToken);
                    }
                }

                await using SqliteCommand markerCommand = connection.CreateCommand();
                markerCommand.Transaction = transaction;
                markerCommand.CommandText = @"
INSERT INTO application_metadata(name, value)
VALUES ($name, '1')
ON CONFLICT(name) DO UPDATE SET value = excluded.value;";
                markerCommand.Parameters.AddWithValue("$name", MigrationMetadataKey);
                await markerCommand.ExecuteNonQueryAsync(cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            if (users.Count > 0 || connections.Count > 0 || rolePermissions.Count > 0)
            {
                _logger.LogInformation(
                    "Migrated {UserCount} users, {ConnectionCount} connection profiles, and {RoleCount} role permission sets into the encrypted application database.",
                    users.Count,
                    connections.Count,
                    rolePermissions.Count);
            }

            ArchiveLegacyFiles();
        }

        // Determine whether migration marker in encrypted local application storage.
        private static async Task<bool> HasMigrationMarkerAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM application_metadata WHERE name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", MigrationMetadataKey);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return string.Equals(Convert.ToString(value), "1", StringComparison.Ordinal);
        }

        // Read legacy users in encrypted local application storage.
        private async Task<List<ApplicationUser>> ReadLegacyUsersAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_legacyUsersPath))
            {
                return new List<ApplicationUser>();
            }

            try
            {
                await using FileStream stream = File.OpenRead(_legacyUsersPath);
                return await JsonSerializer.DeserializeAsync<List<ApplicationUser>>(stream, _jsonOptions, cancellationToken)
                    ?? new List<ApplicationUser>();
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("App_Data/users.json is not valid JSON. The encrypted database migration was cancelled.", exception);
            }
        }

        // Read legacy connections in encrypted local application storage.
        private async Task<List<LegacyConnectionProfile>> ReadLegacyConnectionsAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_legacyConnectionsPath))
            {
                return new List<LegacyConnectionProfile>();
            }

            try
            {
                await using FileStream stream = File.OpenRead(_legacyConnectionsPath);
                return await JsonSerializer.DeserializeAsync<List<LegacyConnectionProfile>>(stream, _jsonOptions, cancellationToken)
                    ?? new List<LegacyConnectionProfile>();
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("App_Data/connections.json is not valid JSON. The encrypted database migration was cancelled.", exception);
            }
        }

        // Read legacy role permissions in encrypted local application storage.
        private async Task<Dictionary<string, List<string>>> ReadLegacyRolePermissionsAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_legacyRolePermissionsPath))
            {
                return new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            try
            {
                await using FileStream stream = File.OpenRead(_legacyRolePermissionsPath);
                LegacyRolePermissionDocument? document = await JsonSerializer.DeserializeAsync<LegacyRolePermissionDocument>(stream, _jsonOptions, cancellationToken);
                return document?.Roles ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("App_Data/role-permissions.json is not valid JSON. The encrypted database migration was cancelled.", exception);
            }
        }

        // Normalize legacy user in encrypted local application storage.
        private static void NormalizeLegacyUser(ApplicationUser user)
        {
            DateTime now = DateTime.UtcNow;
            if (user.Id == Guid.Empty)
            {
                user.Id = Guid.NewGuid();
            }

            user.Username = user.Username?.Trim() ?? string.Empty;
            user.Email = user.Email?.Trim() ?? string.Empty;
            user.AvatarMode = UserAvatarModes.IsValid(user.AvatarMode) ? user.AvatarMode : UserAvatarModes.Initials;
            user.Preferences = UserPreferences.Normalize(user.Preferences);
            user.CreatedAtUtc = user.CreatedAtUtc == default ? now : user.CreatedAtUtc;
            user.UpdatedAtUtc = user.UpdatedAtUtc == default ? user.CreatedAtUtc : user.UpdatedAtUtc;
        }

        // Insert legacy user in encrypted local application storage.
        private async Task InsertLegacyUserAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO application_users(
    id, username, password_hash, role, email, avatar_mode, avatar_file_name,
    preferences_json, created_at_utc, updated_at_utc)
VALUES (
    $id, $username, $passwordHash, $role, $email, $avatarMode, $avatarFileName,
    $preferencesJson, $createdAtUtc, $updatedAtUtc);";
            command.Parameters.AddWithValue("$id", user.Id.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue("$passwordHash", user.PasswordHash ?? string.Empty);
            command.Parameters.AddWithValue("$role", user.Role ?? string.Empty);
            command.Parameters.AddWithValue("$email", user.Email);
            command.Parameters.AddWithValue("$avatarMode", user.AvatarMode);
            command.Parameters.AddWithValue("$avatarFileName", (object?)user.AvatarFileName ?? DBNull.Value);
            command.Parameters.AddWithValue("$preferencesJson", JsonSerializer.Serialize(user.Preferences, _jsonOptions));
            command.Parameters.AddWithValue("$createdAtUtc", user.CreatedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", user.UpdatedAtUtc.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert legacy connection in encrypted local application storage.
        private async Task InsertLegacyConnectionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            LegacyConnectionProfile profile,
            CancellationToken cancellationToken)
        {
            string password;
            try
            {
                password = string.IsNullOrEmpty(profile.ProtectedPassword)
                    ? string.Empty
                    : _legacyConnectionProtector.Unprotect(profile.ProtectedPassword);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException(
                    "A password in App_Data/connections.json could not be decrypted with the current Data Protection keys. " +
                    "Restore the matching App_Data/keys XML files before migration.",
                    exception);
            }

            DateTime now = DateTime.UtcNow;
            DateTime createdAt = profile.CreatedAtUtc == default ? now : profile.CreatedAtUtc;
            DateTime updatedAt = profile.UpdatedAtUtc == default ? createdAt : profile.UpdatedAtUtc;

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO connection_profiles(
    id, name, host, port, username, password, default_database, ssl_mode, created_at_utc, updated_at_utc)
VALUES (
    $id, $name, $host, $port, $username, $password, $defaultDatabase, $sslMode, $createdAtUtc, $updatedAtUtc);";
            command.Parameters.AddWithValue("$id", (profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id).ToString("D"));
            command.Parameters.AddWithValue("$name", profile.Name ?? string.Empty);
            command.Parameters.AddWithValue("$host", profile.Host ?? string.Empty);
            command.Parameters.AddWithValue("$port", (long)profile.Port);
            command.Parameters.AddWithValue("$username", profile.Username ?? string.Empty);
            command.Parameters.AddWithValue("$password", password);
            command.Parameters.AddWithValue("$defaultDatabase", profile.DefaultDatabase ?? string.Empty);
            command.Parameters.AddWithValue("$sslMode", profile.SslMode ?? "Preferred");
            command.Parameters.AddWithValue("$createdAtUtc", createdAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", updatedAt.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert legacy permission in encrypted local application storage.
        private static async Task InsertLegacyPermissionAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string role,
            string permission,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO role_permissions(role, permission) VALUES ($role, $permission);";
            command.Parameters.AddWithValue("$role", role);
            command.Parameters.AddWithValue("$permission", permission);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Handle archive legacy files within the application database workflow.
        private void ArchiveLegacyFiles()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            ArchiveLegacyFile(_legacyUsersPath, timestamp);
            ArchiveLegacyFile(_legacyConnectionsPath, timestamp);
            ArchiveLegacyFile(_legacyRolePermissionsPath, timestamp);
        }

        // Handle archive legacy file within the application database workflow.
        private void ArchiveLegacyFile(string path, string timestamp)
        {
            if (!File.Exists(path))
            {
                return;
            }

            string destination = path + ".migrated-" + timestamp + ".bak";
            for (int suffix = 1; File.Exists(destination); suffix++)
            {
                destination = path + ".migrated-" + timestamp + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bak";
            }

            try
            {
                File.Move(path, destination);
                ApplyPrivateFilePermissions(destination);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "The migrated legacy file {LegacyFile} could not be archived. It will be retried on the next startup.", path);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "The migrated legacy file {LegacyFile} could not be archived. It will be retried on the next startup.", path);
            }
        }

        // Apply private file permissions in encrypted local application storage.
        private static void ApplyPrivateFilePermissions(string path)
        {
            if (!File.Exists(path) || OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        private sealed class LegacyConnectionProfile
        {
            public Guid Id
            {
                get; set;
            }

            public string Name { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public uint Port
            {
                get; set;
            }

            public string Username { get; set; } = string.Empty;
            public string ProtectedPassword { get; set; } = string.Empty;
            public string DefaultDatabase { get; set; } = string.Empty;
            public string SslMode { get; set; } = "Preferred";
            public DateTime CreatedAtUtc
            {
                get; set;
            }

            public DateTime UpdatedAtUtc
            {
                get; set;
            }
        }

        private sealed class LegacyRolePermissionDocument
        {
            public int Version
            {
                get; set;
            }

            public Dictionary<string, List<string>>? Roles
            {
                get; set;
            }
        }
    }
}
