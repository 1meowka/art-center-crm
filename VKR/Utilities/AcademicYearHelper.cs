#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace VKR.Utilities;

public static class AcademicYearHelper
{
    public static int CurrentStart
    {
        get
        {
            var today = DateTime.Today;
            return today.Month >= 8 ? today.Year : today.Year - 1;
        }
    }

    public static string Label(int startYear) => $"{startYear}/{startYear + 1}";

    public static (DateTime From, DateTime To) DateRange(int startYear) =>
        (new DateTime(startYear, 8, 1), new DateTime(startYear + 1, 7, 31));

    public static IReadOnlyList<int> NearbyStarts(int countBack = 3, int countForward = 1)
    {
        var current = CurrentStart;
        return Enumerable.Range(current - countBack, countBack + countForward + 1).ToList();
    }
}
