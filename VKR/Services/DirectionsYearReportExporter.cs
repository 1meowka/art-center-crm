#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public sealed class DirectionsYearReportExporter
{
    private const int HeaderRow = 3;
    private const int FirstDataRow = 4;
    private const int DirectionColumn = 4;
    private const int MetricColumn = 5;
    private const int FirstMonthColumn = 6;

    private static readonly string[] AcademicMonthNames =
    [
        "август", "сентябрь", "октябрь", "ноябрь", "декабрь",
        "январь", "февраль", "март", "апрель", "май"
    ];

    private static readonly int[] AcademicMonths = [8, 9, 10, 11, 12, 1, 2, 3, 4, 5];

    private static readonly XLColor[] DirectionFillColors =
    [
        XLColor.FromHtml("#FFF200"),
        XLColor.FromHtml("#F4C2C2"),
        XLColor.FromHtml("#C6EFCE"),
        XLColor.FromHtml("#BDD7EE"),
        XLColor.FromHtml("#F8CBAD"),
        XLColor.FromHtml("#E2EFDA"),
        XLColor.FromHtml("#DDEBF7"),
        XLColor.FromHtml("#FFE699")
    ];

    public string Export(DirectionsYearReportData data, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        AppendToWorkbook(workbook, data);
        workbook.SaveAs(outputPath);

        return outputPath;
    }

    public void AppendToWorkbook(XLWorkbook workbook, DirectionsYearReportData data)
    {
        var sheetName = workbook.Worksheets.Any(w => string.Equals(w.Name, "общ", StringComparison.OrdinalIgnoreCase))
            ? "Свод по направлениям"
            : "общ";
        var ws = workbook.Worksheets.Add(sheetName);
        BuildSheet(ws, data);
    }

    public static string SuggestFileName(DirectionsYearReportRequest request) =>
        $"БЛП_{request.AcademicYearStart}-{request.AcademicYearStart + 1}_направления.xlsx";

    public static int ResolveDefaultAcademicYearStart()
    {
        var today = DateTime.Today;
        return today.Month >= 8 ? today.Year : today.Year - 1;
    }

    public static int MonthToColumn(int month) =>
        month switch
        {
            8 => FirstMonthColumn,
            9 => FirstMonthColumn + 1,
            10 => FirstMonthColumn + 2,
            11 => FirstMonthColumn + 3,
            12 => FirstMonthColumn + 4,
            1 => FirstMonthColumn + 5,
            2 => FirstMonthColumn + 6,
            3 => FirstMonthColumn + 7,
            4 => FirstMonthColumn + 8,
            5 => FirstMonthColumn + 9,
            _ => -1
        };

    private static void BuildSheet(IXLWorksheet ws, DirectionsYearReportData data)
    {
        var yearStart = data.AcademicYearStart;
        var yearEnd = yearStart + 1;

        ws.Column(DirectionColumn).Width = 14;
        ws.Column(MetricColumn).Width = 10;
        for (var col = FirstMonthColumn; col < FirstMonthColumn + AcademicMonths.Length; col++)
            ws.Column(col).Width = 11;

        WriteMonthHeaders(ws);

        var statsLookup = data.Stats.ToDictionary(
            x => (x.IdDirection, x.Year, x.Month),
            x => x);

        var totalsLookup = data.Totals.ToDictionary(
            x => (x.Year, x.Month),
            x => x);

        var row = FirstDataRow;
        for (var i = 0; i < data.Directions.Count; i++)
        {
            var direction = data.Directions[i];
            var fillColor = DirectionFillColors[i % DirectionFillColors.Length];
            WriteDirectionBlock(
                ws,
                row,
                direction.DirectionName,
                fillColor,
                (year, month) =>
                {
                    statsLookup.TryGetValue((direction.IdDirection, year, month), out var stat);
                    return stat;
                },
                yearStart,
                yearEnd);

            row += 3;
        }

        if (data.Directions.Count > 0)
            row--;

        WriteTotalBlock(ws, row, totalsLookup, yearStart, yearEnd);
        ws.SheetView.FreezeRows(HeaderRow);
    }

    private static void WriteMonthHeaders(IXLWorksheet ws)
    {
        for (var i = 0; i < AcademicMonthNames.Length; i++)
        {
            var cell = ws.Cell(HeaderRow, FirstMonthColumn + i);
            cell.Value = AcademicMonthNames[i];
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ApplyBorder(cell);
        }
    }

    private static void WriteDirectionBlock(
        IXLWorksheet ws,
        int startRow,
        string directionName,
        XLColor fillColor,
        Func<int, int, DirectionMonthStat?> resolveStat,
        int yearStart,
        int yearEnd)
    {
        ws.Range(startRow, DirectionColumn, startRow + 1, DirectionColumn).Merge();
        var directionCell = ws.Cell(startRow, DirectionColumn);
        directionCell.Value = directionName;
        directionCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        directionCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        directionCell.Style.Font.Bold = true;

        WriteMetricRow(ws, startRow, "сумма", fillColor, (year, month) =>
        {
            var stat = resolveStat(year, month);
            return stat?.Amount;
        }, yearStart, yearEnd);

        WriteMetricRow(ws, startRow + 1, "человек", fillColor, (year, month) =>
        {
            var stat = resolveStat(year, month);
            return stat?.PeopleCount;
        }, yearStart, yearEnd);

        var blockRange = ws.Range(startRow, DirectionColumn, startRow + 1, FirstMonthColumn + AcademicMonths.Length - 1);
        blockRange.Style.Fill.BackgroundColor = fillColor;
    }

    private static void WriteMetricRow(
        IXLWorksheet ws,
        int row,
        string metricLabel,
        XLColor fillColor,
        Func<int, int, decimal?> resolveValue,
        int yearStart,
        int yearEnd)
    {
        var metricCell = ws.Cell(row, MetricColumn);
        metricCell.Value = metricLabel;
        metricCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        if (fillColor.Color.IsEmpty == false)
            metricCell.Style.Fill.BackgroundColor = fillColor;
        ApplyBorder(metricCell);

        for (var i = 0; i < AcademicMonths.Length; i++)
        {
            var month = AcademicMonths[i];
            var year = month >= 8 ? yearStart : yearEnd;
            var col = FirstMonthColumn + i;
            var cell = ws.Cell(row, col);
            var value = resolveValue(year, month);
            if (value.HasValue && value.Value != 0)
                cell.Value = value.Value;

            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (fillColor.Color.IsEmpty == false)
                cell.Style.Fill.BackgroundColor = fillColor;
            if (metricLabel == "сумма")
                cell.Style.NumberFormat.Format = "#,##0";
            ApplyBorder(cell);
        }
    }

    private static void WriteTotalBlock(
        IXLWorksheet ws,
        int startRow,
        Dictionary<(int Year, int Month), DirectionMonthTotal> totalsLookup,
        int yearStart,
        int yearEnd)
    {
        ws.Range(startRow, DirectionColumn, startRow + 1, DirectionColumn).Merge();
        var directionCell = ws.Cell(startRow, DirectionColumn);
        directionCell.Value = "ВСЕГО";
        directionCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        directionCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        directionCell.Style.Font.Bold = true;
        ApplyBorder(directionCell);

        WriteMetricRow(ws, startRow, "сумма", XLColor.NoColor, (year, month) =>
        {
            totalsLookup.TryGetValue((year, month), out var total);
            return total?.Amount;
        }, yearStart, yearEnd);

        WriteMetricRow(ws, startRow + 1, "человек", XLColor.NoColor, (year, month) =>
        {
            totalsLookup.TryGetValue((year, month), out var total);
            return total?.PeopleCount;
        }, yearStart, yearEnd);

        var blockRange = ws.Range(startRow, DirectionColumn, startRow + 1, FirstMonthColumn + AcademicMonths.Length - 1);
        ApplyBorder(blockRange);
    }

    private static void ApplyBorder(IXLCell cell)
    {
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void ApplyBorder(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }
}
