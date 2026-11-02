#nullable enable
using System;

namespace VKR.Utilities;

/// <summary>Отчётный период: вторник — понедельник. Во вторник анализируется завершившаяся неделя.</summary>
public static class ReportPeriodHelper
{
    public static short ToScheduleDayOfWeek(DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)date.DayOfWeek;
    }

    public static DateTime GetReportPeriodStart(DateTime date)
    {
        var d = date.Date;
        var isoDow = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
        var daysSinceTuesday = isoDow >= 2 ? isoDow - 2 : 6;
        return d.AddDays(-daysSinceTuesday);
    }

    public static DateTime GetReportPeriodEnd(DateTime periodStart) => periodStart.Date.AddDays(6);

    public static (DateTime Start, DateTime End) GetReportPeriodContaining(DateTime date)
    {
        var start = GetReportPeriodStart(date);
        return (start, GetReportPeriodEnd(start));
    }

    /// <summary>Период для анализа на указанную дату (во вторник — завершившийся вчера период).</summary>
    public static (DateTime Start, DateTime End) GetAnalysisPeriod(DateTime asOfDate)
    {
        if (asOfDate.DayOfWeek == DayOfWeek.Tuesday)
        {
            var end = asOfDate.Date.AddDays(-1);
            return (end.AddDays(-6), end);
        }

        return GetReportPeriodContaining(asOfDate);
    }

    public static string FormatPeriod(DateTime start, DateTime end) =>
        $"{start:dd.MM.yyyy} — {end:dd.MM.yyyy}";

    public static string GetPeriodCaption(DateTime asOfDate)
    {
        var (start, end) = GetAnalysisPeriod(asOfDate);
        var prefix = asOfDate.DayOfWeek == DayOfWeek.Tuesday
            ? "Анализ недели"
            : "Текущий отчётный период";
        return $"{prefix}: {FormatPeriod(start, end)}";
    }
}
