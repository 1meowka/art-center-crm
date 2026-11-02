#nullable enable
using System.Net.Mail;

namespace VKR.Utilities;

public static class EmailValidator
{
    public static bool IsValidOptional(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return true;

        try
        {
            _ = new MailAddress(email.Trim());
            return email.Contains('@');
        }
        catch
        {
            return false;
        }
    }

    public static bool IsValidRequired(string email) =>
        !string.IsNullOrWhiteSpace(email) && IsValidOptional(email);
}
