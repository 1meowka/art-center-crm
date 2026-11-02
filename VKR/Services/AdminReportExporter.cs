#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public sealed class AdminReportExporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#FFF200");
    private static readonly XLColor GroupFill = XLColor.FromHtml("#FFE699");

    public string Export(AdminReportData data, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        AppendToWorkbook(workbook, data);
        workbook.SaveAs(outputPath);
        return outputPath;
    }

    public void AppendToWorkbook(XLWorkbook workbook, AdminReportData data)
    {
        BuildLeadSourcesSheet(workbook, data);

        foreach (var direction in data.DirectionGroups)
            BuildDirectionSheet(workbook, direction, data.Request.AcademicYearStart);

        BuildCampSheets(workbook, data);
    }

    public static string SuggestFileName(AdminReportRequest request) =>
        $"Учебный_год_{request.AcademicYearStart}-{request.AcademicYearStart + 1}.xlsx";

    private static void BuildLeadSourcesSheet(XLWorkbook workbook, AdminReportData data)
    {
        var ws = workbook.Worksheets.Add(SafeSheetName("Источники"));
        ws.Cell(1, 1).Value = "Источники прихода";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value =
            $"Период: {data.Request.PeriodStart:dd.MM.yyyy} — {data.Request.PeriodEnd:dd.MM.yyyy}";

        var headers = new[]
        {
            "Источник", "Заявки", "Конвертировано", "Новые обучающиеся", "Договоры", "Сумма договоров, ₽"
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(4, c + 1).Value = headers[c];

        StyleHeaderRow(ws, 4, headers.Length);

        var row = 5;
        foreach (var item in data.LeadSources)
        {
            ws.Cell(row, 1).Value = item.SourceName;
            ws.Cell(row, 2).Value = item.LeadsCount;
            ws.Cell(row, 3).Value = item.ConvertedLeadsCount;
            ws.Cell(row, 4).Value = item.NewStudentsCount;
            ws.Cell(row, 5).Value = item.ContractsCount;
            ws.Cell(row, 6).Value = item.Revenue;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
            row++;
        }

        ws.Columns(1, headers.Length).AdjustToContents();
    }

    private static void BuildDirectionSheet(XLWorkbook workbook, DirectionGroupReport direction, int academicYearStart)
    {
        var ws = workbook.Worksheets.Add(SafeSheetName(direction.DirectionName));
        var row = 1;
        ws.Cell(row, 1).Value = direction.DirectionName;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        foreach (var group in direction.Groups)
        {
            if (group.Students.Count == 0)
                continue;

            ws.Range(row, 1, row, 24).Merge();
            ws.Cell(row, 1).Value = $"{group.GroupTitle}  {group.ScheduleText}".Trim();
            ws.Cell(row, 1).Style.Fill.BackgroundColor = GroupFill;
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;

            WriteGroupTableHeader(ws, row, academicYearStart);
            row++;

            foreach (var student in group.Students)
            {
                WriteGroupStudentRow(ws, row, student);
                row++;
            }

            WriteGroupTotalsRow(ws, row, group);
            row += 2;
        }

        ws.Columns(1, 24).AdjustToContents(1, 80);
    }

    private static void WriteGroupTableHeader(IXLWorksheet ws, int row, int academicYearStart)
    {
        var cols = new List<string>
        {
            "№", "ФИО", "%", "сумма по договору", "мама", "тел. мамы", "папа", "тел. папы",
            "эл. почта", "плательщик", "возрастная группа", "дом. адрес", "школа/дс",
            "дата рожд.", "свид. о рождении", "приход", "уход", "полных лет", "источник"
        };
        cols.AddRange(AdminReportRepository.AcademicMonthLabels);

        for (var i = 0; i < cols.Count; i++)
            ws.Cell(row, i + 1).Value = cols[i];

        StyleHeaderRow(ws, row, cols.Count);
        ws.Cell(row, cols.Count + 1).Value = $"уч. год {academicYearStart}/{academicYearStart + 1}";
    }

    private static void WriteGroupStudentRow(IXLWorksheet ws, int row, GroupStudentReportRow student)
    {
        var col = 1;
        ws.Cell(row, col++).Value = student.RowNumber;
        ws.Cell(row, col++).Value = student.FullName;
        if (student.DiscountPercent.HasValue) ws.Cell(row, col).Value = student.DiscountPercent.Value;
        col++;
        if (student.ContractAmount.HasValue)
        {
            ws.Cell(row, col).Value = student.ContractAmount.Value;
            ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
        }
        col++;
        ws.Cell(row, col++).Value = student.MotherName;
        ws.Cell(row, col++).Value = student.MotherPhone;
        ws.Cell(row, col++).Value = student.FatherName;
        ws.Cell(row, col++).Value = student.FatherPhone;
        ws.Cell(row, col++).Value = student.Email;
        ws.Cell(row, col++).Value = student.PayerName;
        ws.Cell(row, col++).Value = student.AgeGroup;
        ws.Cell(row, col++).Value = student.ResidenceAddress;
        ws.Cell(row, col++).Value = student.SchoolInfo;
        ws.Cell(row, col++).Value = student.BirthDate?.ToString("dd.MM.yyyy", Ru);
        ws.Cell(row, col++).Value = student.BirthCertificate;
        ws.Cell(row, col++).Value = student.ArrivalDate?.ToString("dd.MM.yyyy", Ru);
        ws.Cell(row, col++).Value = student.DepartureDate?.ToString("dd.MM.yyyy", Ru);
        if (student.FullYears.HasValue) ws.Cell(row, col).Value = student.FullYears.Value;
        col++;
        ws.Cell(row, col++).Value = student.SourceName;

        for (var i = 0; i < student.MonthlyPayments.Length; i++)
        {
            if (student.MonthlyPayments[i] > 0)
            {
                ws.Cell(row, col).Value = student.MonthlyPayments[i];
                ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
            }
            col++;
        }
    }

    private static void WriteGroupTotalsRow(IXLWorksheet ws, int row, GroupReportSection group)
    {
        ws.Cell(row, 1).Value = "итого:";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Range(row, 1, row, 19).Merge();
        ws.Range(row, 1, row, 19).Style.Fill.BackgroundColor = HeaderFill;

        var col = 20;
        for (var i = 0; i < group.MonthlyTotals.Length; i++)
        {
            if (group.MonthlyTotals[i] > 0)
            {
                ws.Cell(row, col).Value = group.MonthlyTotals[i];
                ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, col).Style.Font.Bold = true;
            }
            col++;
        }
    }

    private static void BuildCampSheets(XLWorkbook workbook, AdminReportData data)
    {
        if (data.CampStudents.Count == 0)
            return;

        var bySource = data.CampStudents
            .GroupBy(x => x.SourceName)
            .OrderBy(x => x.Key);

        foreach (var sourceGroup in bySource)
        {
            var sheetName = SafeSheetName("Лагерь · " + sourceGroup.Key);
            var ws = workbook.Worksheets.Add(sheetName);
            var row = 1;
            ws.Cell(row, 1).Value = $"Лагерь — источник: {sourceGroup.Key}";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row += 2;

            var headers = new[]
            {
                "№", "ФИО", "Смена / направление", "Телефон", "Представитель 1", "Тел. 1",
                "Представитель 2", "Тел. 2", "Свид. о рождении", "Дата рожд.", "Комментарий"
            };
            for (var c = 0; c < headers.Length; c++)
                ws.Cell(row, c + 1).Value = headers[c];
            StyleHeaderRow(ws, row, headers.Length);
            row++;

            var num = 0;
            foreach (var item in sourceGroup)
            {
                num++;
                ws.Cell(row, 1).Value = num;
                ws.Cell(row, 2).Value = item.FullName;
                ws.Cell(row, 3).Value = item.ShiftInfo;
                ws.Cell(row, 4).Value = item.Phone;
                ws.Cell(row, 5).Value = item.Representative1Name;
                ws.Cell(row, 6).Value = item.Representative1Phone;
                ws.Cell(row, 7).Value = item.Representative2Name;
                ws.Cell(row, 8).Value = item.Representative2Phone;
                ws.Cell(row, 9).Value = item.BirthCertificate;
                ws.Cell(row, 10).Value = item.BirthDate?.ToString("dd.MM.yyyy", Ru);
                ws.Cell(row, 11).Value = item.Comment;
                row++;
            }

            ws.Columns(1, headers.Length).AdjustToContents();
        }
    }

    private static void StyleHeaderRow(IXLWorksheet ws, int row, int colCount)
    {
        var range = ws.Range(row, 1, row, colCount);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = HeaderFill;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = name;
        foreach (var ch in invalid)
            cleaned = cleaned.Replace(ch, ' ');

        cleaned = cleaned.Trim();
        if (cleaned.Length > 31)
            cleaned = cleaned[..31].Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "Лист" : cleaned;
    }
}
