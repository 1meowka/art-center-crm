#nullable enable

namespace VKR.Utilities;

public static class PasswordHasher
{
    private const int WorkFactor = 12;

    public static bool IsBcryptHash(string? hash) =>
        !string.IsNullOrWhiteSpace(hash)
        && hash.StartsWith("$2", StringComparison.Ordinal);

    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string hash)
    {
        if (!IsBcryptHash(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}
