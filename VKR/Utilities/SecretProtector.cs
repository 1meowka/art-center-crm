#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace VKR.Utilities;

/// <summary>
/// Шифрование секретов at-rest через Windows DPAPI (только текущий пользователь ОС).
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VKR.HappyArtist.Secrets.v1");

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText),
            Entropy,
            DataProtectionScope.CurrentUser);

        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return stored;

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        try
        {
            var payload = stored[Prefix.Length..];
            var protectedBytes = Convert.FromBase64String(payload);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // Токен был зашифрован на другом ПК или под другим пользователем Windows.
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    public static bool NeedsReentry(string stored) =>
        IsProtected(stored) && string.IsNullOrEmpty(Unprotect(stored));

    public static bool IsProtected(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal);
}
