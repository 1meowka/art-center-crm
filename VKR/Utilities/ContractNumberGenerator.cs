#nullable enable
using System;

namespace VKR.Utilities;

public static class ContractNumberGenerator
{
    /// <summary>Учебный год: сентябрь — август, формат 2526.</summary>
    public static string BuildAcademicYearPrefix(DateTime date)
    {
        var startYear = date.Month >= 9 ? date.Year : date.Year - 1;
        return $"{startYear % 100:00}{(startYear + 1) % 100:00}";
    }

    public static string FormatNumber(string academicPrefix, int sequence) =>
        $"{academicPrefix}-{sequence:0000}";
}
