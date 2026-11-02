#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class UserRepository
{
    public async Task<IReadOnlyList<UserListItem>> GetUsersAsync(string? search = null, bool onlyActive = false)
    {
        DatabaseSchema.EnsureIfPossible();

        var result = new List<UserListItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    u.id_user, u.login,
    u.last_name, u.first_name, u.middle_name, u.email,
    u.is_active, u.last_login_at,
    r.role_code, r.role_name
FROM app_user u
JOIN role r ON r.id_role = u.id_role
WHERE (@search IS NULL OR
       LOWER(u.login) LIKE LOWER('%' || @search || '%') OR
       LOWER(TRIM(u.last_name || ' ' || u.first_name || COALESCE(' ' || u.middle_name, ''))) LIKE LOWER('%' || @search || '%') OR
       LOWER(COALESCE(u.email, '')) LIKE LOWER('%' || @search || '%'))
  AND (@only_active = false OR u.is_active = true)
ORDER BY u.is_active DESC, u.last_name, u.first_name, u.login;", connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActive;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new UserListItem
            {
                IdUser = reader.GetInt32(reader.GetOrdinal("id_user")),
                Login = reader.GetString(reader.GetOrdinal("login")),
                LastName = reader.GetString(reader.GetOrdinal("last_name")),
                FirstName = reader.GetString(reader.GetOrdinal("first_name")),
                MiddleName = ReadNullable(reader, "middle_name"),
                Email = ReadNullable(reader, "email"),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                LastLoginAt = ReadNullableDateTime(reader, "last_login_at"),
                RoleCode = reader.GetString(reader.GetOrdinal("role_code")),
                RoleName = reader.GetString(reader.GetOrdinal("role_name"))
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<RoleItem>> GetRolesAsync()
    {
        var result = new List<RoleItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT DISTINCT ON (LOWER(TRIM(role_code)))
    id_role, role_code, role_name
FROM role
ORDER BY LOWER(TRIM(role_code)), id_role;", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new RoleItem
            {
                IdRole = reader.GetInt32(reader.GetOrdinal("id_role")),
                RoleCode = reader.GetString(reader.GetOrdinal("role_code")),
                RoleName = reader.GetString(reader.GetOrdinal("role_name"))
            });
        }

        return LookupDeduper.ByRoleCode(result);
    }

    public async Task<IReadOnlyList<LookupItem>> GetLinkableTeachersAsync(int? excludeUserId = null, int? currentTeacherId = null)
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    t.id_teacher,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name
FROM public.teacher t
WHERE t.is_active = TRUE
  AND (
        @current_teacher IS NOT NULL AND t.id_teacher = @current_teacher
        OR NOT EXISTS (
            SELECT 1
              FROM app_user u
             WHERE u.id_teacher = t.id_teacher
               AND (@exclude_user IS NULL OR u.id_user <> @exclude_user)
        )
      )
ORDER BY t.last_name, t.first_name, t.middle_name;", connection);
        command.Parameters.Add("exclude_user", NpgsqlDbType.Integer).Value =
            excludeUserId.HasValue ? excludeUserId.Value : DBNull.Value;
        command.Parameters.Add("current_teacher", NpgsqlDbType.Integer).Value =
            currentTeacherId.HasValue ? currentTeacherId.Value : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LookupItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id_teacher")),
                Name = reader.GetString(reader.GetOrdinal("teacher_name"))
            });
        }

        return LookupDeduper.ByName(result);
    }

    public async Task<UserEditModel?> GetUserAsync(int idUser)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT u.id_user, u.login, u.last_name, u.first_name, u.middle_name, u.email,
       u.is_active, u.id_role, u.id_teacher, r.role_code
FROM app_user u
JOIN role r ON r.id_role = u.id_role
WHERE u.id_user = @id_user;", connection);
        command.Parameters.Add("id_user", NpgsqlDbType.Integer).Value = idUser;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new UserEditModel
        {
            IdUser = reader.GetInt32(reader.GetOrdinal("id_user")),
            Login = reader.GetString(reader.GetOrdinal("login")),
            LastName = reader.GetString(reader.GetOrdinal("last_name")),
            FirstName = reader.GetString(reader.GetOrdinal("first_name")),
            MiddleName = ReadNullable(reader, "middle_name"),
            Email = ReadNullable(reader, "email"),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
            IdRole = reader.GetInt32(reader.GetOrdinal("id_role")),
            IdTeacher = ReadNullableInt(reader, "id_teacher"),
            RoleCode = reader.GetString(reader.GetOrdinal("role_code"))
        };
    }

    public async Task<UserProfileDetails?> GetProfileAsync(int idUser)
    {
        DatabaseSchema.EnsureIfPossible();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    u.id_user, u.login, u.last_name, u.first_name, u.middle_name, u.email,
    u.is_active, u.last_login_at, u.id_teacher,
    r.role_code, r.role_name,
    CASE
        WHEN t.id_teacher IS NULL THEN NULL
        ELSE TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, ''))
    END AS teacher_full_name
FROM app_user u
JOIN role r ON r.id_role = u.id_role
LEFT JOIN public.teacher t ON t.id_teacher = u.id_teacher
WHERE u.id_user = @id_user;", connection);
        command.Parameters.Add("id_user", NpgsqlDbType.Integer).Value = idUser;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new UserProfileDetails
        {
            IdUser = reader.GetInt32(reader.GetOrdinal("id_user")),
            Login = reader.GetString(reader.GetOrdinal("login")),
            LastName = reader.GetString(reader.GetOrdinal("last_name")),
            FirstName = reader.GetString(reader.GetOrdinal("first_name")),
            MiddleName = ReadNullable(reader, "middle_name"),
            Email = ReadNullable(reader, "email"),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
            LastLoginAt = ReadNullableDateTime(reader, "last_login_at"),
            IdTeacher = ReadNullableInt(reader, "id_teacher"),
            RoleCode = reader.GetString(reader.GetOrdinal("role_code")),
            RoleName = reader.GetString(reader.GetOrdinal("role_name")),
            TeacherFullName = ReadNullable(reader, "teacher_full_name")
        };
    }

    public Task ChangeOwnPasswordAsync(int idUser, string currentPassword, string newPassword) =>
        throw new InvalidOperationException(
            "Смена пароля недоступна. Новый пароль задаёт только руководитель в разделе «Администрирование».");

    public async Task<bool> IsLoginTakenAsync(string login, int? excludeUserId = null)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT EXISTS(
    SELECT 1 FROM app_user
    WHERE LOWER(login) = LOWER(@login)
      AND (@exclude_id IS NULL OR id_user <> @exclude_id)
);", connection);
        command.Parameters.Add("login", NpgsqlDbType.Text).Value = login.Trim();
        command.Parameters.Add("exclude_id", NpgsqlDbType.Integer).Value =
            excludeUserId.HasValue ? excludeUserId.Value : DBNull.Value;

        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    public async Task<int> CreateUserAsync(UserEditModel user, string password)
    {
        ValidateUser(user, password, isNew: true);
        await EnsureTeacherLinkValidAsync(user);

        if (await IsLoginTakenAsync(user.Login))
            throw new InvalidOperationException("Такой логин уже занят.");

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
INSERT INTO app_user (login, password_hash, last_name, first_name, middle_name, email, id_role, is_active, id_teacher)
VALUES (@login, @password_hash, @last_name, @first_name, @middle_name, @email, @id_role, @is_active, @id_teacher)
RETURNING id_user;", connection);
        FillUserParameters(command, user);
        command.Parameters.Add("password_hash", NpgsqlDbType.Text).Value =
            PasswordHasher.Hash(password);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task UpdateUserAsync(UserEditModel user, string? newPassword)
    {
        ValidateUser(user, newPassword, isNew: false);
        await EnsureTeacherLinkValidAsync(user);

        if (await IsLoginTakenAsync(user.Login, user.IdUser))
            throw new InvalidOperationException("Такой логин уже занят.");

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var sql = string.IsNullOrWhiteSpace(newPassword)
            ? @"
UPDATE app_user
SET login = @login, last_name = @last_name, first_name = @first_name,
    middle_name = @middle_name, email = @email, id_role = @id_role,
    is_active = @is_active, id_teacher = @id_teacher
WHERE id_user = @id_user;"
            : @"
UPDATE app_user
SET login = @login, password_hash = @password_hash, last_name = @last_name, first_name = @first_name,
    middle_name = @middle_name, email = @email, id_role = @id_role,
    is_active = @is_active, id_teacher = @id_teacher
WHERE id_user = @id_user;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("id_user", NpgsqlDbType.Integer).Value = user.IdUser;
        FillUserParameters(command, user);
        if (!string.IsNullOrWhiteSpace(newPassword))
            command.Parameters.Add("password_hash", NpgsqlDbType.Text).Value =
                PasswordHasher.Hash(newPassword);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
            throw new InvalidOperationException("Пользователь не найден.");
    }

    private static async Task EnsureTeacherLinkValidAsync(UserEditModel user)
    {
        if (!IsTeacherRoleCode(user.RoleCode))
        {
            user.IdTeacher = null;
            return;
        }

        if (!user.IdTeacher.HasValue)
            return;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT EXISTS(
    SELECT 1
      FROM app_user
     WHERE id_teacher = @id_teacher
       AND (@exclude_user IS NULL OR id_user <> @exclude_user)
);", connection);
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = user.IdTeacher.Value;
        command.Parameters.Add("exclude_user", NpgsqlDbType.Integer).Value =
            user.IdUser > 0 ? user.IdUser : DBNull.Value;

        if (Convert.ToBoolean(await command.ExecuteScalarAsync()))
            throw new InvalidOperationException("Выбранный преподаватель уже привязан к другой учётной записи.");
    }

    private static bool IsTeacherRoleCode(string roleCode) => RoleCodes.IsTeacher(roleCode);

    private static void ValidateUser(UserEditModel user, string? password, bool isNew)
    {
        user.Login = (user.Login ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(user.Login))
            user.Login = $"user{DateTime.Now:HHmmss}";
        if (string.IsNullOrWhiteSpace(user.LastName))
            user.LastName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(user.FirstName))
            user.FirstName = OptionalFieldDefaults.EmptyName;
        if (user.IdRole <= 0)
            user.IdRole = 1;

        if (isNew && string.IsNullOrWhiteSpace(password))
            password = "Studio123";
        if (!string.IsNullOrWhiteSpace(password))
            PasswordPolicy.ValidateOrThrow(password);
    }

    private static void FillUserParameters(NpgsqlCommand command, UserEditModel user)
    {
        command.Parameters.Add("login", NpgsqlDbType.Text).Value = user.Login.Trim();
        command.Parameters.Add("last_name", NpgsqlDbType.Text).Value = user.LastName.Trim();
        command.Parameters.Add("first_name", NpgsqlDbType.Text).Value = user.FirstName.Trim();
        command.Parameters.Add("middle_name", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(user.MiddleName) ? DBNull.Value : user.MiddleName.Trim();
        command.Parameters.Add("email", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(user.Email) ? DBNull.Value : user.Email.Trim();
        command.Parameters.Add("id_role", NpgsqlDbType.Integer).Value = user.IdRole;
        command.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = user.IsActive;
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
            user.IdTeacher.HasValue ? user.IdTeacher.Value : DBNull.Value;
    }

    private static string? ReadNullable(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime? ReadNullableDateTime(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
