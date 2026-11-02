#nullable enable

namespace VKR.Utilities;

/// <summary>Разбор и форматирование ФИО в одной строке.</summary>
public static class FioHelper
{
    public sealed record ParsedFio(string LastName, string FirstName, string? MiddleName);

    public static string FormatFullName(string? lastName, string? firstName, string? middleName)
    {
        var parts = new[] { lastName, firstName, middleName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        return string.Join(" ", parts);
    }

    public static string FormatFromStored(string firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName))
            return firstName.Trim();
        return FormatFullName(lastName, firstName, null);
    }

    /// <summary>Фамилия — первое слово, имя — второе, остальное — отчество.</summary>
    public static ParsedFio Parse(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return new ParsedFio(string.Empty, string.Empty, null);

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => new ParsedFio(string.Empty, string.Empty, null),
            1 => new ParsedFio(string.Empty, InputRules.CapitalizeFirstLetter(parts[0]), null),
            2 => new ParsedFio(
                InputRules.CapitalizeFirstLetter(parts[0]),
                InputRules.CapitalizeFirstLetter(parts[1]),
                null),
            _ => new ParsedFio(
                InputRules.CapitalizeFirstLetter(parts[0]),
                InputRules.CapitalizeFirstLetter(parts[1]),
                InputRules.CapitalizeFirstLetter(string.Join(" ", parts.Skip(2))))
        };
    }

    /// <summary>Если указано только одно из имён — дублирует его во второе поле для хранения в БД.</summary>
    public static (string LastName, string FirstName) NormalizeStoredNames(string? lastName, string? firstName)
    {
        var ln = lastName?.Trim() ?? string.Empty;
        var fn = firstName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(ln) && string.IsNullOrEmpty(fn))
            return (string.Empty, string.Empty);
        if (string.IsNullOrEmpty(ln))
            return (fn, fn);
        if (string.IsNullOrEmpty(fn))
            return (ln, ln);
        return (ln, fn);
    }

    public static (string LastName, string FirstName) NormalizeStoredNamesFromFullName(string? fullName)
    {
        var parsed = Parse(fullName);
        return NormalizeStoredNames(parsed.LastName, parsed.FirstName);
    }
}
