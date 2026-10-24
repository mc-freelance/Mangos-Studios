// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application services, state management, and encrypted local persistence.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace MangosDatabaseStudio.App.Services
{
    public sealed class SqliteApplicationUserStore : IApplicationUserStore
    {
        private readonly ApplicationDatabase _database;
        private readonly PasswordHasher<ApplicationUser> _passwordHasher = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Initialize the sqlite application user store dependencies and state.
        public SqliteApplicationUserStore(ApplicationDatabase database)
        {
            _database = database;
        }

        // Handle any async within the sqlite application user store workflow.
        public async Task<bool> AnyAsync(CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM application_users LIMIT 1);";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;
        }

        // Read all in encrypted local application storage.
        public async Task<IReadOnlyList<ApplicationUser>> GetAllAsync(CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = UserSelectSql + " ORDER BY username COLLATE NOCASE;";
            return await ReadUsersAsync(command, cancellationToken);
        }

        // Read  in encrypted local application storage.
        public async Task<ApplicationUser?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            return await GetByIdAsync(connection, null, id, cancellationToken);
        }

        // Locate  in encrypted local application storage.
        public async Task<ApplicationUser?> FindAsync(string username, CancellationToken cancellationToken)
        {
            string normalized = username?.Trim() ?? string.Empty;
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = UserSelectSql + " WHERE username = $username COLLATE NOCASE LIMIT 1;";
            command.Parameters.AddWithValue("$username", normalized);
            return (await ReadUsersAsync(command, cancellationToken)).FirstOrDefault();
        }

        // Validate  in encrypted local application storage.
        public async Task<ApplicationUser?> ValidateAsync(string username, string password, CancellationToken cancellationToken)
        {
            ApplicationUser? user = await FindAsync(username, cancellationToken);
            if (user == null)
            {
                return null;
            }

            PasswordVerificationResult result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
            if (result == PasswordVerificationResult.Failed)
            {
                return null;
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                await RehashPasswordAsync(user.Id, password!, cancellationToken);
            }

            return user;
        }

        // Create administrator in encrypted local application storage.
        public async Task CreateAdministratorAsync(string username, string password, CancellationToken cancellationToken)
        {
            string normalizedUsername = NormalizeUsername(username);
            EnsurePassword(password);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                if (await CountUsersAsync(connection, transaction, cancellationToken) > 0)
                {
                    throw new InvalidOperationException("Initial setup has already been completed.");
                }

                ApplicationUser user = CreateUser(normalizedUsername, password, AppRoles.Administrator);
                await InsertAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Create  in encrypted local application storage.
        public async Task CreateAsync(string username, string password, string role, CancellationToken cancellationToken)
        {
            string normalizedUsername = NormalizeUsername(username);
            EnsurePassword(password);
            EnsureRole(role);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                await EnsureUniqueUsernameAsync(connection, transaction, normalizedUsername, null, cancellationToken);
                ApplicationUser user = CreateUser(normalizedUsername, password, role);
                await InsertAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Update  in encrypted local application storage.
        public async Task UpdateAsync(Guid id, string username, string role, string? newPassword, CancellationToken cancellationToken)
        {
            string normalizedUsername = NormalizeUsername(username);
            EnsureRole(role);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser user = await GetRequiredByIdAsync(connection, transaction, id, cancellationToken);
                await EnsureUniqueUsernameAsync(connection, transaction, normalizedUsername, id, cancellationToken);

                bool removingAdministrator = string.Equals(user.Role, AppRoles.Administrator, StringComparison.Ordinal) &&
                                             !string.Equals(role, AppRoles.Administrator, StringComparison.Ordinal);
                if (removingAdministrator && await CountAdministratorsAsync(connection, transaction, cancellationToken) <= 1)
                {
                    throw new InvalidOperationException("The final administrator cannot be assigned a lower role.");
                }

                user.Username = normalizedUsername;
                user.Role = role;
                if (!string.IsNullOrWhiteSpace(newPassword))
                {
                    EnsurePassword(newPassword);
                    user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
                }

                Touch(user);
                await UpdateUserAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Update profile in encrypted local application storage.
        public async Task UpdateProfileAsync(Guid id, string email, string avatarMode, string? avatarFileName, CancellationToken cancellationToken)
        {
            string normalizedEmail = email?.Trim() ?? string.Empty;
            if (!UserAvatarModes.IsValid(avatarMode))
            {
                throw new InvalidOperationException("Choose a valid avatar source.");
            }

            if (string.Equals(avatarMode, UserAvatarModes.Gravatar, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(normalizedEmail))
            {
                throw new InvalidOperationException("An email address is required to use Gravatar.");
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser user = await GetRequiredByIdAsync(connection, transaction, id, cancellationToken);
                user.Email = normalizedEmail;
                user.AvatarMode = avatarMode;
                user.AvatarFileName = string.Equals(avatarMode, UserAvatarModes.Upload, StringComparison.Ordinal)
                    ? avatarFileName
                    : null;
                Touch(user);
                await UpdateUserAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Update preferences in encrypted local application storage.
        public async Task UpdatePreferencesAsync(Guid id, UserPreferences preferences, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser user = await GetRequiredByIdAsync(connection, transaction, id, cancellationToken);
                user.Preferences = UserPreferences.Normalize(preferences);
                Touch(user);
                await UpdateUserAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Handle change password async within the sqlite application user store workflow.
        public async Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken)
        {
            EnsurePassword(newPassword);

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser user = await GetRequiredByIdAsync(connection, transaction, id, cancellationToken);
                PasswordVerificationResult current = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? string.Empty);
                if (current == PasswordVerificationResult.Failed)
                {
                    throw new InvalidOperationException("The current password is incorrect.");
                }

                user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
                Touch(user);
                await UpdateUserAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Delete  in encrypted local application storage.
        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser? user = await GetByIdAsync(connection, transaction, id, cancellationToken);
                if (user == null)
                {
                    transaction.Commit();
                    return;
                }

                if (string.Equals(user.Role, AppRoles.Administrator, StringComparison.Ordinal) &&
                    await CountAdministratorsAsync(connection, transaction, cancellationToken) <= 1)
                {
                    throw new InvalidOperationException("The final administrator account cannot be deleted.");
                }

                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM application_users WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                await command.ExecuteNonQueryAsync(cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Create user in encrypted local application storage.
        private ApplicationUser CreateUser(string username, string password, string role)
        {
            ApplicationUser user = new()
            {
                Username = username,
                Role = role,
                AvatarMode = UserAvatarModes.Initials,
                Preferences = new UserPreferences(),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            return user;
        }

        // Handle rehash password async within the sqlite application user store workflow.
        private async Task RehashPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                ApplicationUser? user = await GetByIdAsync(connection, transaction, id, cancellationToken);
                if (user == null)
                {
                    transaction.Commit();
                    return;
                }

                user.PasswordHash = _passwordHasher.HashPassword(user, password);
                Touch(user);
                await UpdateUserAsync(connection, transaction, user, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // Insert  in encrypted local application storage.
        private async Task InsertAsync(
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
            AddUserParameters(command, user);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Update user in encrypted local application storage.
        private async Task UpdateUserAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE application_users
SET username = $username,
    password_hash = $passwordHash,
    role = $role,
    email = $email,
    avatar_mode = $avatarMode,
    avatar_file_name = $avatarFileName,
    preferences_json = $preferencesJson,
    created_at_utc = $createdAtUtc,
    updated_at_utc = $updatedAtUtc
WHERE id = $id;";
            AddUserParameters(command, user);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException("The selected user no longer exists.");
            }
        }

        // Add user parameters in encrypted local application storage.
        private void AddUserParameters(SqliteCommand command, ApplicationUser user)
        {
            command.Parameters.AddWithValue("$id", user.Id.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
            command.Parameters.AddWithValue("$role", user.Role);
            command.Parameters.AddWithValue("$email", user.Email ?? string.Empty);
            command.Parameters.AddWithValue("$avatarMode", user.AvatarMode);
            command.Parameters.AddWithValue("$avatarFileName", (object?)user.AvatarFileName ?? DBNull.Value);
            command.Parameters.AddWithValue("$preferencesJson", JsonSerializer.Serialize(UserPreferences.Normalize(user.Preferences), _jsonOptions));
            command.Parameters.AddWithValue("$createdAtUtc", user.CreatedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", user.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        }

        // Read required by id in encrypted local application storage.
        private async Task<ApplicationUser> GetRequiredByIdAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid id,
            CancellationToken cancellationToken)
        {
            return await GetByIdAsync(connection, transaction, id, cancellationToken)
                ?? throw new InvalidOperationException("The selected user no longer exists.");
        }

        // Read by id in encrypted local application storage.
        private async Task<ApplicationUser?> GetByIdAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            Guid id,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UserSelectSql + " WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            return (await ReadUsersAsync(command, cancellationToken)).FirstOrDefault();
        }

        // Read users in encrypted local application storage.
        private async Task<List<ApplicationUser>> ReadUsersAsync(SqliteCommand command, CancellationToken cancellationToken)
        {
            List<ApplicationUser> users = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                UserPreferences? preferences = null;
                string preferencesJson = reader.GetString(7);
                try
                {
                    preferences = JsonSerializer.Deserialize<UserPreferences>(preferencesJson, _jsonOptions);
                }
                catch (JsonException)
                {
                }

                users.Add(new ApplicationUser
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Username = reader.GetString(1),
                    PasswordHash = reader.GetString(2),
                    Role = reader.GetString(3),
                    Email = reader.GetString(4),
                    AvatarMode = reader.GetString(5),
                    AvatarFileName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Preferences = UserPreferences.Normalize(preferences),
                    CreatedAtUtc = ParseUtc(reader.GetString(8)),
                    UpdatedAtUtc = ParseUtc(reader.GetString(9))
                });
            }

            return users;
        }

        // Count users in encrypted local application storage.
        private static async Task<long> CountUsersAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM application_users;";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        // Count administrators in encrypted local application storage.
        private static async Task<long> CountAdministratorsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM application_users WHERE role = $role;";
            command.Parameters.AddWithValue("$role", AppRoles.Administrator);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        // Ensure unique username in encrypted local application storage.
        private static async Task EnsureUniqueUsernameAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string username,
            Guid? exceptId,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = exceptId.HasValue
                ? "SELECT EXISTS(SELECT 1 FROM application_users WHERE username = $username COLLATE NOCASE AND id <> $id LIMIT 1);"
                : "SELECT EXISTS(SELECT 1 FROM application_users WHERE username = $username COLLATE NOCASE LIMIT 1);";
            command.Parameters.AddWithValue("$username", username);
            if (exceptId.HasValue)
            {
                command.Parameters.AddWithValue("$id", exceptId.Value.ToString("D"));
            }

            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidOperationException("Another user already has that username.");
            }
        }

        // Handle touch within the sqlite application user store workflow.
        private static void Touch(ApplicationUser user)
        {
            user.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Parse utc in encrypted local application storage.
        private static DateTime ParseUtc(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.UtcNow;
        }

        // Normalize username in encrypted local application storage.
        private static string NormalizeUsername(string username)
        {
            string normalized = username?.Trim() ?? string.Empty;
            if (normalized.Length < 3 || normalized.Length > 64)
            {
                throw new InvalidOperationException("Usernames must contain between 3 and 64 characters.");
            }

            return normalized;
        }

        // Ensure password in encrypted local application storage.
        private static void EnsurePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 128)
            {
                throw new InvalidOperationException("Passwords must contain between 12 and 128 characters.");
            }
        }

        // Ensure role in encrypted local application storage.
        private static void EnsureRole(string role)
        {
            if (!AppRoles.IsValid(role))
            {
                throw new InvalidOperationException("The selected application role is not valid.");
            }
        }

        private const string UserSelectSql = @"
SELECT id, username, password_hash, role, email, avatar_mode, avatar_file_name,
       preferences_json, created_at_utc, updated_at_utc
FROM application_users";
    }
}
