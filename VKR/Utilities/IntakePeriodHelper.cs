#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace VKR.Utilities;

public enum IntakeSeason
{
    Winter = 1,
    Spring = 2,
    Summer = 3,
    Autumn = 4
}

public sealed class IntakePeriod
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int Count { get; set; }

    public override string ToString() => Count > 0 ? $"{Label} ({Count})" : Label;
}

public static class IntakePeriodHelper
{
    public static string GetLabel(DateTime date)
    {
        var (season, year) = GetSeasonAndYear(date);
        return $"{GetSeasonName(season)} {year}";
    }

    public static string GetKey(DateTime date)
    {
        var (season, year) = GetSeasonAndYear(date);
        return $"{year}-{(int)season}";
    }

    public static IntakePeriod CreatePeriod(int year, IntakeSeason season, int count = 0)
    {
        var (from, to) = GetDateRange(year, season);
        return new IntakePeriod
        {
            Key = $"{year}-{(int)season}",
            Label = $"{GetSeasonName(season)} {year}",
            From = from,
            To = to,
            Count = count
        };
    }

    public static IntakePeriod? ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var parts = key.Split('-');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var seasonInt) ||
            !Enum.IsDefined(typeof(IntakeSeason), seasonInt))
            return null;

        return CreatePeriod(year, (IntakeSeason)seasonInt);
    }

    public static IReadOnlyList<IntakePeriod> BuildPeriodList(IEnumerable<DateTime> arrivalDates)
    {
        var grouped = arrivalDates
            .Select(d => d.Date)
            .GroupBy(GetKey)
            .Select(g =>
            {
                var sample = g.First();
                var (season, year) = GetSeasonAndYear(sample);
                var period = CreatePeriod(year, season, g.Count());
                return period;
            })
            .OrderByDescending(p => p.From)
            .ToList();

        return grouped;
    }

    public static IReadOnlyList<IntakePeriod> BuildRecentPeriods(int yearsBack = 3, int yearsForward = 1)
    {
        var currentYear = DateTime.Today.Year;
        var periods = new List<IntakePeriod>();

        for (var year = currentYear - yearsBack; year <= currentYear + yearsForward; year++)
        {
            foreach (IntakeSeason season in Enum.GetValues(typeof(IntakeSeason)))
                periods.Add(CreatePeriod(year, season));
        }

        return periods.OrderByDescending(p => p.From).ToList();
    }

    private static (IntakeSeason season, int year) GetSeasonAndYear(DateTime date)
    {
        return date.Month switch
        {
            12 => (IntakeSeason.Winter, date.Year + 1),
            1 or 2 => (IntakeSeason.Winter, date.Year),
            3 or 4 or 5 => (IntakeSeason.Spring, date.Year),
            6 or 7 or 8 => (IntakeSeason.Summer, date.Year),
            9 or 10 or 11 => (IntakeSeason.Autumn, date.Year),
            _ => (IntakeSeason.Autumn, date.Year)
        };
    }

    private static (DateTime from, DateTime to) GetDateRange(int year, IntakeSeason season) => season switch
    {
        IntakeSeason.Winter => (new DateTime(year - 1, 12, 1), new DateTime(year, 2, DateTime.DaysInMonth(year, 2))),
        IntakeSeason.Spring => (new DateTime(year, 3, 1), new DateTime(year, 5, 31)),
        IntakeSeason.Summer => (new DateTime(year, 6, 1), new DateTime(year, 8, 31)),
        IntakeSeason.Autumn => (new DateTime(year, 9, 1), new DateTime(year, 11, 30)),
        _ => (new DateTime(year, 1, 1), new DateTime(year, 12, 31))
    };

    private static string GetSeasonName(IntakeSeason season) => season switch
    {
        IntakeSeason.Winter => "Зима",
        IntakeSeason.Spring => "Весна",
        IntakeSeason.Summer => "Лето",
        IntakeSeason.Autumn => "Осень",
        _ => "—"
    };
}
