#nullable enable
using System.Linq;
using System.Text;

namespace VKR.Utilities;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const string Hint = "Не менее 8 символов, буквы и цифры.";

    public static void ValidateOrThrow(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Введите пароль.");

        if (password.Length < MinLength)
            throw new InvalidOperationException($"Пароль должен содержать не менее {MinLength} символов.");

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);

        if (!hasLetter || !hasDigit)
            throw new InvalidOperationException("Пароль должен содержать буквы и цифры.");
    }
}
