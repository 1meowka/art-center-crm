#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace VKR.Services;

public static class ContractPricing
{
    public static decimal CalculateTotal(decimal price, decimal discountPercent, decimal discountAmount)
    {
        if (price < 0) price = 0;
        if (discountPercent < 0) discountPercent = 0;
        if (discountAmount < 0) discountAmount = 0;
        if (discountPercent > 100) discountPercent = 100;

        var percentAmount = price > 0 && discountPercent > 0
            ? Math.Round(price * discountPercent / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var totalDiscount = percentAmount + discountAmount;
        if (totalDiscount > price)
            totalDiscount = price;

        return Math.Max(0, price - totalDiscount);
    }

    public static string BuildDiscountComment(
        decimal price,
        decimal discountPercent,
        decimal percentAmount,
        decimal discountAmount,
        decimal total)
    {
        if (price <= 0 || (discountPercent <= 0 && discountAmount <= 0))
            return string.Empty;

        var parts = new List<string>();
        if (discountPercent > 0)
            parts.Add($"{discountPercent.ToString("0.##", CultureInfo.InvariantCulture)}% ({percentAmount.ToString("0.##", CultureInfo.InvariantCulture)} ₽)");
        if (discountAmount > 0)
            parts.Add($"{discountAmount.ToString("0.##", CultureInfo.InvariantCulture)} ₽");

        return $"Применена скидка: {string.Join(" + ", parts)}. Сумма договора: {total.ToString("0.##", CultureInfo.InvariantCulture)} ₽.";
    }

    public static string? StripDiscountComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        var lines = comment
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Применена скидка:", StringComparison.OrdinalIgnoreCase))
                kept.Add(trimmed);
        }

        return kept.Count == 0 ? null : string.Join(Environment.NewLine, kept);
    }
}
