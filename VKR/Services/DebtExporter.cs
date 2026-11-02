#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public sealed class DebtExporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public string Export(IReadOnlyList<DebtorItem> debtors, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Дебиторка");
        ws.Cell(1, 1).Value = "Дебиторская задолженность";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Выгрузка от {DateTime.Now:dd.MM.yyyy HH:mm} · строк: {debtors.Count}";

        var headers = new[]
        {
            "Клиент", "№ договора", "Направление", "Период", "Начислено", "Оплачено", "Остаток",
            "Дата начисления", "Срок оплаты (до 20-го)", "Дней просрочки", "Статус",
            "Отсрочка до", "Комментарий / отсрочка"
        };

        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(4, c + 1).Value = headers[c];
            ws.Cell(4, c + 1).Style.Font.Bold = true;
            ws.Cell(4, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        }

        var row = 5;
        foreach (var item in debtors)
        {
            ws.Cell(row, 1).Value = item.StudentFullName;
            ws.Cell(row, 2).Value = item.ContractNumber;
            ws.Cell(row, 3).Value = item.DirectionName;
            ws.Cell(row, 4).Value = item.PeriodText;
            ws.Cell(row, 5).Value = item.DebtAmount;
            ws.Cell(row, 6).Value = item.PaidAmount;
            ws.Cell(row, 7).Value = item.Outstanding;
            ws.Cell(row, 8).Value = item.OccurrenceDateText;
            ws.Cell(row, 9).Value = item.DueDateText;
            ws.Cell(row, 10).Value = item.IsDeferred ? 0 : item.DaysOverdue;
            ws.Cell(row, 11).Value = item.OverdueLevelText;
            ws.Cell(row, 12).Value = item.DeferUntil?.ToString("dd.MM.yyyy") ?? "—";
            ws.Cell(row, 13).Value = item.IsDeferred ? item.DeferInfoText : item.CommentText;
            row++;
        }

        ws.Columns(1, headers.Length).AdjustToContents();
        workbook.SaveAs(outputPath);
        return outputPath;
    }

    public static string SuggestFileName() =>
        $"Дебиторка_{DateTime.Today:yyyy-MM-dd}.xlsx";
}
