#nullable enable
using System;

namespace VKR.Utilities;

/// <summary>Оплата по рассрочке — до 20-го числа месяца, следующего за месяцем начисления.</summary>
public static class DebtDueDateHelper
{
    public static DateTime CalculateDueDate(DateTime occurrenceDate) =>
        new DateTime(occurrenceDate.Year, occurrenceDate.Month, 1).AddMonths(1).AddDays(19);

    public static DateTime ResolveDueDate(DateTime? dueDate, DateTime? occurrenceDate) =>
        dueDate?.Date
        ?? (occurrenceDate.HasValue ? CalculateDueDate(occurrenceDate.Value) : DateTime.Today);

    public static bool IsDeferActive(DateTime? deferUntil) =>
        deferUntil.HasValue && deferUntil.Value.Date >= DateTime.Today;

    public static DateTime GetEffectiveDueDate(DateTime? dueDate, DateTime? occurrenceDate, DateTime? deferUntil)
    {
        var baseDue = ResolveDueDate(dueDate, occurrenceDate);
        if (IsDeferActive(deferUntil))
            return deferUntil!.Value.Date;
        return baseDue;
    }

    public static int GetDaysOverdue(DateTime effectiveDueDate, decimal outstanding, bool isDeferred)
    {
        if (outstanding <= 0 || isDeferred)
            return 0;
        return Math.Max(0, (DateTime.Today - effectiveDueDate.Date).Days);
    }
}
