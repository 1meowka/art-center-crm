#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VKR.Utilities;

public static class PaymentKindNames
{
    public const string Contract = "заключение договора";
    public const string Trial = "пробное занятие";
    public const string OneOff = "разовое занятие";
    public const string Workshop = "мастер-класс";

    public static IReadOnlyList<string> All { get; } =
        new[] { Contract, Trial, OneOff, Workshop };

    public static bool UsesContract(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
        || kind.Trim().Equals(Contract, StringComparison.CurrentCultureIgnoreCase);

    public static bool NeedsEventText(string? kind) => !UsesContract(kind);
}

public static class PaymentPeriodNames
{
    public static IReadOnlyList<string> Months { get; } =
        CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.MonthNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => char.ToUpper(x[0], CultureInfo.GetCultureInfo("ru-RU")) + x[1..])
            .ToList();

    public static short? TryMonthNumber(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        var key = label.Trim().ToLowerInvariant();
        for (short i = 1; i <= 12; i++)
        {
            var name = CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.GetMonthName(i);
            if (key.Contains(name, StringComparison.CurrentCultureIgnoreCase)
                || name.Contains(key, StringComparison.CurrentCultureIgnoreCase))
                return i;
        }
        return null;
    }
}
