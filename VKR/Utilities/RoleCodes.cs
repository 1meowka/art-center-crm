#nullable enable

namespace VKR.Utilities;

public static class RoleCodes
{
    public static string Normalize(string? roleCode) =>
        string.IsNullOrWhiteSpace(roleCode) ? string.Empty : roleCode.Trim().ToLowerInvariant();

    public static bool IsAdmin(string? roleCode)
    {
        return Normalize(roleCode) is "admin" or "administrator" or "админ" or "администратор";
    }

    public static bool IsDirector(string? roleCode)
    {
        return Normalize(roleCode) is "director" or "директор" or "руководитель";
    }

    public static bool IsSeniorManager(string? roleCode)
    {
        var code = Normalize(roleCode);
        return code is "senior_manager"
            or "senior-manager"
            or "head_manager"
            or "старший менеджер"
            or "старший_менеджер"
            or "старшийменеджер";
    }

    public static bool IsManager(string? roleCode)
    {
        if (IsSeniorManager(roleCode))
            return false;

        return Normalize(roleCode) is "manager" or "менеджер";
    }

    public static bool IsAnyManager(string? roleCode) =>
        IsManager(roleCode) || IsSeniorManager(roleCode);

    public static bool IsTeacher(string? roleCode)
    {
        return Normalize(roleCode) is "teacher" or "преподаватель";
    }
}
