#nullable enable
using System;

namespace VKR.Utilities;

public static class ContractDurationHelper
{
    public static DateTime AddDuration(DateTime start, decimal durationMonths)
    {
        if (durationMonths <= 0)
            return start.Date;

        if (durationMonths == decimal.Truncate(durationMonths))
            return start.Date.AddMonths((int)durationMonths);

        var days = (int)Math.Round(durationMonths * 30.437m, MidpointRounding.AwayFromZero);
        return start.Date.AddDays(Math.Max(1, days));
    }

    public static string FormatDuration(decimal durationMonths) =>
        durationMonths == decimal.Truncate(durationMonths)
            ? $"{durationMonths:0} мес."
            : $"{durationMonths:0.#} мес.";
}
