#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public sealed class PpoReportExporter
{
    private static readonly string[] MonthNames =
    [
        "", "январь", "февраль", "март", "апрель", "май", "июнь",
        "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"
    ];

    private static readonly Dictionary<int, int> MonthBlockStartRows = new()
    {
        [8] = 4, [9] = 9, [10] = 14, [11] = 19, [12] = 24,
        [1] = 29, [2] = 34, [3] = 39, [4] = 44, [5] = 49, [6] = 54, [7] = 59
    };

    public string Export(
        PpoReportRequest request,
        IReadOnlyList<PpoDailyIncomeRow> income,
        IReadOnlyList<PpoExpenseExportRow> expenses,
        string outputPath)
    {
        var templatePath = ResolveTemplatePath();
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Не найден шаблон ППО.", templatePath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook(templatePath);
        FillIncomeSheet(workbook.Worksheet("ДУ"), request, income);
        FillExpensesSheet(workbook.Worksheet("расходы"), request, expenses);
        workbook.SaveAs(outputPath);

        return outputPath;
    }

    public static string SuggestFileName(PpoReportRequest request) =>
        request.DateFrom.Date == request.DateTo.Date
            ? $"Финансы_{request.DateFrom:yyyyMMdd}.xlsx"
            : $"Финансы_{request.DateFrom:yyyyMMdd}_{request.DateTo:yyyyMMdd}.xlsx";

    private static string ResolveTemplatePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Resources", "Templates", "PpoReportTemplate.xlsx");
    }

    private static void FillIncomeSheet(IXLWorksheet ws, PpoReportRequest request, IReadOnlyList<PpoDailyIncomeRow> income)
    {
        ClearIncomeData(ws, request);

        var grouped = income
            .Where(x => x.Date.Date >= request.DateFrom.Date && x.Date.Date <= request.DateTo.Date)
            .GroupBy(x => (x.Date.Date, x.Channel))
            .ToDictionary(g => g.Key, g => g.Sum(v => v.Amount));

        foreach (var ((date, channel), amount) in grouped)
        {
            if (!MonthBlockStartRows.TryGetValue(date.Month, out var blockStart))
                continue;

            var row = blockStart + ChannelOffset(channel);
            var col = DayColumn(date.Day);
            ws.Cell(row, col).Value = (double)amount;
        }

        foreach (var month in MonthsInRange(request))
        {
            if (!MonthBlockStartRows.TryGetValue(month, out var blockStart))
                continue;

            var totalRow = blockStart + 4;
            for (var day = 1; day <= DateTime.DaysInMonth(YearForMonth(request, month), month); day++)
            {
                if (!IsDayInRequest(request, month, day))
                    continue;

                var col = DayColumn(day);
                var sum = 0m;
                for (var offset = 0; offset <= 3; offset++)
                {
                    var cell = ws.Cell(blockStart + offset, col);
                    if (cell.TryGetValue(out double val))
                        sum += (decimal)val;
                }

                ws.Cell(totalRow, col).Value = (double)sum;
            }
        }
    }

    private static void ClearIncomeData(IXLWorksheet ws, PpoReportRequest request)
    {
        foreach (var (month, blockStart) in MonthBlockStartRows)
        {
            for (var offset = 0; offset <= 4; offset++)
            {
                for (var day = 1; day <= 31; day++)
                    ws.Cell(blockStart + offset, DayColumn(day)).Value = 0;
            }
        }
    }

    private static void FillExpensesSheet(IXLWorksheet ws, PpoReportRequest request, IReadOnlyList<PpoExpenseExportRow> expenses)
    {
        const int firstDataRow = 9;
        const int lastTemplateRow = 134;

        for (var row = firstDataRow; row <= lastTemplateRow; row++)
        {
            ws.Cell(row, 2).Value = "";
            ws.Cell(row, 3).Value = "";
            ws.Cell(row, 4).Value = "";
            ws.Cell(row, 5).Value = "";
            ws.Cell(row, 6).Value = "";
            ws.Cell(row, 7).Value = "";
            ws.Cell(row, 9).Value = "";
            ws.Cell(row, 10).Value = "";
        }

        var filtered = expenses
            .Where(x => x.ExpenseDate.Date >= request.DateFrom.Date && x.ExpenseDate.Date <= request.DateTo.Date)
            .OrderBy(x => x.ExpenseDate)
            .ThenBy(x => x.IdExpense)
            .ToList();

        var rowIndex = firstDataRow;
        foreach (var monthGroup in filtered.GroupBy(x => new { x.ExpenseDate.Year, x.ExpenseDate.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            var monthTotal = monthGroup.Sum(x => x.Amount);
            var monthName = MonthNames[monthGroup.Key.Month];
            if (monthGroup.Key.Month == 1 || monthGroup.Key.Month == 2 || monthGroup.Key.Month == 3)
                monthName = char.ToUpper(monthName[0]) + monthName[1..];

            ws.Cell(rowIndex, 5).Value = monthName;
            ws.Cell(rowIndex, 6).Value = (double)monthTotal;
            rowIndex++;

            var seq = 1;
            foreach (var item in monthGroup)
            {
                ws.Cell(rowIndex, 2).Value = seq;
                ws.Cell(rowIndex, 3).Value = item.IdExpense.ToString(CultureInfo.InvariantCulture);
                ws.Cell(rowIndex, 4).Value = item.ExpenseDate;
                ws.Cell(rowIndex, 5).Value = item.VendorName;
                ws.Cell(rowIndex, 6).Value = (double)item.Amount;
                ws.Cell(rowIndex, 7).Value = item.Details;
                ws.Cell(rowIndex, 9).Value = item.OrgCode;
                ws.Cell(rowIndex, 10).Value = item.MonthCode;
                rowIndex++;
                seq++;
            }

            rowIndex++;
        }
    }

    private static int ChannelOffset(PpoPaymentChannel channel) => channel switch
    {
        PpoPaymentChannel.Terminal => 0,
        PpoPaymentChannel.Qr => 1,
        PpoPaymentChannel.Sber => 2,
        PpoPaymentChannel.Cash => 3,
        _ => 0
    };

    private static int DayColumn(int day) => 2 + day;

    private static IEnumerable<int> MonthsInRange(PpoReportRequest request)
    {
        var cursor = new DateTime(request.DateFrom.Year, request.DateFrom.Month, 1);
        var end = new DateTime(request.DateTo.Year, request.DateTo.Month, 1);
        while (cursor <= end)
        {
            yield return cursor.Month;
            cursor = cursor.AddMonths(1);
        }
    }

    private static int YearForMonth(PpoReportRequest request, int month)
    {
        if (request.DateFrom.Year == request.DateTo.Year)
            return request.DateFrom.Year;

        if (month >= request.DateFrom.Month && request.DateFrom.Year != request.DateTo.Year)
            return request.DateFrom.Year;

        return request.DateTo.Year;
    }

    private static bool IsDayInRequest(PpoReportRequest request, int month, int day)
    {
        var year = YearForMonth(request, month);
        var date = new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
        if (day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateTime(year, month, day);
        return date >= request.DateFrom.Date && date <= request.DateTo.Date;
    }
}
