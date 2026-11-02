#nullable enable
using System.Collections.Generic;

namespace VKR.Models;

public sealed class DirectionsYearReportRequest
{
    /// <summary>Год начала учебного года (август).</summary>
    public int AcademicYearStart { get; set; }
}

public sealed class DirectionYearReportDirection
{
    public int IdDirection { get; set; }
    public string DirectionName { get; set; } = "";
}

public sealed class DirectionMonthStat
{
    public int IdDirection { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public int PeopleCount { get; set; }
}

public sealed class DirectionMonthTotal
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public int PeopleCount { get; set; }
}

public sealed class DirectionsYearReportData
{
    public int AcademicYearStart { get; set; }
    public IReadOnlyList<DirectionYearReportDirection> Directions { get; set; } = [];
    public IReadOnlyList<DirectionMonthStat> Stats { get; set; } = [];
    public IReadOnlyList<DirectionMonthTotal> Totals { get; set; } = [];
}
