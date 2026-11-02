#nullable enable
using System;
using System.Globalization;
using VKR.Models;

namespace VKR.Services;

public static class VpReportSupport
{
    private static readonly string[] MonthNamesUpper =
    [
        "", "ЯНВАРЬ", "ФЕВРАЛЬ", "МАРТ", "АПРЕЛЬ", "МАЙ", "ИЮНЬ",
        "ИЮЛЬ", "АВГУСТ", "СЕНТЯБРЬ", "ОКТЯБРЬ", "НОЯБРЬ", "ДЕКАБРЬ"
    ];

    private static readonly string[] MonthNamesLower =
    [
        "", "январь", "февраль", "март", "апрель", "май", "июнь",
        "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"
    ];

    public static string FormatRegisterDate(DateTime date) =>
        date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static string FormatShortDate(DateTime date) =>
        date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static string FormatBirthDate(DateTime? date) =>
        date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";

    public static string MonthTitleUpper(int month) =>
        month is >= 1 and <= 12 ? MonthNamesUpper[month] : "";

    public static string MonthTitleLower(int month)
    {
        if (month is < 1 or > 12)
            return "";

        var name = MonthNamesLower[month];
        return char.ToUpper(name[0]) + name[1..];
    }

    public static string MonthDebtTitle(int month) =>
        month is >= 1 and <= 12 ? $"за {MonthNamesUpper[month]}" : "";

    public static int ResolveClientCategoryCode(string? typeCode, string? typeName)
    {
        var text = $"{typeCode} {typeName}".ToLowerInvariant();
        if (text.Contains("нов", StringComparison.Ordinal))
            return 1;
        if (text.Contains("верн", StringComparison.Ordinal))
            return 2;
        if (text.Contains("перез", StringComparison.Ordinal))
            return 4;
        return 3;
    }

    public static string BuildDiscountText(decimal? discountAmount, decimal? discountPercent)
    {
        if (discountAmount is > 0)
            return "акция";
        if (discountPercent is > 0)
            return $"{discountPercent.Value:0.#}%";
        return "";
    }

    public static int WeekBucketIndex(int day) => day switch
    {
        <= 9 => 0,
        <= 16 => 1,
        <= 23 => 2,
        _ => 3
    };
}
