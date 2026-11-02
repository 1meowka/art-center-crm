#nullable enable
using System;
using System.Collections.Generic;
using VKR.Models;

namespace VKR.Services;

public static class PpoExpenseCategoryMapper
{
    public static int ResolveOrgCode(string? categoryName, string? description)
    {
        var text = $"{categoryName} {description}".ToLowerInvariant();

        if (ContainsAny(text, "хоз", "канц", "бумаг", "быт"))
            return 1;
        if (ContainsAny(text, "оборуд", "оргтех", "компьютер", "принтер"))
            return 3;
        if (ContainsAny(text, "груз", "доставк", "перевоз"))
            return 4;
        if (ContainsAny(text, "транспорт", "тaxi", "такси", "бензин", "топлив"))
            return 5;
        if (ContainsAny(text, "реклам", "маркет", "продвиж"))
            return 6;
        if (ContainsAny(text, "корпоратив", "представит"))
            return 7;
        if (ContainsAny(text, "связ", "интернет", "телефон", "мобил"))
            return 8;
        if (ContainsAny(text, "вод"))
            return 9;
        if (ContainsAny(text, "закуп", "материал", "краск", "кист", "холст", "гипс"))
            return 10;
        if (ContainsAny(text, "ремонт"))
            return 11;
        if (ContainsAny(text, "мероприят", "праздник", "выстав"))
            return 12;
        if (ContainsAny(text, "питан", "обед", "ланч", "еда", "пицц", "ресторан"))
            return 13;
        if (ContainsAny(text, "лагер"))
            return 14;

        return 2;
    }

    private static bool ContainsAny(string text, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (text.Contains(part, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public static class PpoPaymentChannelMapper
{
    public static PpoPaymentChannel Resolve(string? methodName, string? methodCode)
    {
        var text = $"{methodName} {methodCode}".ToLowerInvariant();

        if (text.Contains("qr", StringComparison.Ordinal) || text.Contains("сбп", StringComparison.Ordinal))
            return PpoPaymentChannel.Qr;
        if (text.Contains("сбер", StringComparison.Ordinal) || text.Contains("sber", StringComparison.Ordinal))
            return PpoPaymentChannel.Sber;
        if (text.Contains("нал", StringComparison.Ordinal)
            || text.Contains("cash", StringComparison.Ordinal)
            || text.Contains("налич", StringComparison.Ordinal))
            return PpoPaymentChannel.Cash;
        if (text.Contains("терм", StringComparison.Ordinal)
            || text.Contains("terminal", StringComparison.Ordinal)
            || text.Contains("карт", StringComparison.Ordinal)
            || text.Contains("эквайр", StringComparison.Ordinal))
            return PpoPaymentChannel.Terminal;

        return PpoPaymentChannel.Terminal;
    }
}
