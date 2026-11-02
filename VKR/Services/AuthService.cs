#nullable enable
using System;
using System.Collections.Concurrent;
using Npgsql;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, LoginAttemptState> Attempts = new();

    public Task<AuthResult> AuthenticateAsync(string login, string password) =>
        Task.FromResult(Authenticate(login, password));

    public AuthResult Authenticate(string login, string password)
    {
        var loginKey = NormalizeLogin(login);
        if (string.IsNullOrEmpty(loginKey))
            return AuthResult.Fail("Введите логин и пароль.");

        if (TryGetLockoutMessage(loginKey, out var lockoutMessage))
            return AuthResult.Fail(lockoutMessage);

        DatabaseSchema.EnsureIfPossible();

        const string sql = @"
SELECT u.id_user, u.login, u.password_hash,
       u.last_name, u.first_name, u.middle_name, u.email,
       u.is_active, u.id_teacher,
       r.role_code, r.role_name
  FROM app_user u
  JOIN role r ON r.id_role = u.id_role
 WHERE LOWER(TRIM(u.login)) = @login_key";

        using var conn = Db.CreateConnection();
        conn.Open();

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@login_key", loginKey);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            RegisterFailedAttempt(loginKey);
            return AuthResult.Fail("Неверный логин или пароль.");
        }

        var isActive = reader.GetBoolean(reader.GetOrdinal("is_active"));
        if (!isActive)
        {
            return AuthResult.Fail(
                "Учётная запись заблокирована. Обратитесь к администратору, чтобы включить доступ.");
        }

        var passwordHashOrdinal = reader.GetOrdinal("password_hash");
        if (reader.IsDBNull(passwordHashOrdinal))
        {
            return AuthResult.Fail(
                "Для учётной записи не задан пароль. Администратор должен задать новый пароль.");
        }

        var passwordHash = reader.GetString(passwordHashOrdinal);
        if (!PasswordHasher.IsBcryptHash(passwordHash))
        {
            return AuthResult.Fail(
                "Пароль учётной записи сохранён в устаревшем формате. Администратор должен задать новый пароль.");
        }

        if (!PasswordHasher.Verify(password, passwordHash))
        {
            RegisterFailedAttempt(loginKey);
            return AuthResult.Fail("Неверный логин или пароль.");
        }

        var user = new AppUser
        {
            IdUser = reader.GetInt32(reader.GetOrdinal("id_user")),
            Login = reader.GetString(reader.GetOrdinal("login")),
            LastName = reader.GetString(reader.GetOrdinal("last_name")),
            FirstName = reader.GetString(reader.GetOrdinal("first_name")),
            MiddleName = reader.IsDBNull(reader.GetOrdinal("middle_name"))
                ? null
                : reader.GetString(reader.GetOrdinal("middle_name")),
            Email = reader.IsDBNull(reader.GetOrdinal("email"))
                ? null
                : reader.GetString(reader.GetOrdinal("email")),
            IdTeacher = reader.IsDBNull(reader.GetOrdinal("id_teacher"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("id_teacher")),
            RoleCode = reader.GetString(reader.GetOrdinal("role_code")),
            RoleName = reader.GetString(reader.GetOrdinal("role_name"))
        };

        reader.Close();

        using var updateCmd = new NpgsqlCommand(
            "UPDATE app_user SET last_login_at = CURRENT_TIMESTAMP WHERE id_user = @id", conn);
        updateCmd.Parameters.AddWithValue("@id", user.IdUser);
        updateCmd.ExecuteNonQuery();

        Attempts.TryRemove(loginKey, out _);
        return AuthResult.Success(user);
    }

    private static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();

    private static bool TryGetLockoutMessage(string loginKey, out string message)
    {
        message = string.Empty;
        if (!Attempts.TryGetValue(loginKey, out var state) || DateTime.UtcNow >= state.LockoutUntil)
            return false;

        var minutesLeft = Math.Max(1, (int)Math.Ceiling((state.LockoutUntil - DateTime.UtcNow).TotalMinutes));
        message = $"Слишком много неудачных попыток для этого логина. Повторите через {minutesLeft} мин.";
        return true;
    }

    private static void RegisterFailedAttempt(string loginKey)
    {
        var now = DateTime.UtcNow;
        var failed = 1;

        if (Attempts.TryGetValue(loginKey, out var current))
        {
            if (now < current.LockoutUntil)
                return;

            failed = current.FailedAttempts + 1;
        }

        if (failed >= MaxFailedAttempts)
        {
            Attempts[loginKey] = new LoginAttemptState(0, now.Add(LockoutDuration));
            return;
        }

        Attempts[loginKey] = new LoginAttemptState(failed, DateTime.MinValue);
    }

    private sealed record LoginAttemptState(int FailedAttempts, DateTime LockoutUntil);
}
