#nullable enable
using System.Collections.Generic;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public static class DictionaryExcelExporter
{
    public static void Write(string path, DictionaryKind kind, IReadOnlyList<DictionaryEntry> entries)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(DictionaryKindInfo.Title(kind));
        sheet.Cell(1, 1).Value = "Наименование";
        sheet.Cell(1, 2).Value = "Подпись";
        var row = 2;
        foreach (var entry in entries)
        {
            sheet.Cell(row, 1).Value = entry.Name;
            sheet.Cell(row, 2).Value = entry.Subtitle ?? "";
            row++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}
