#nullable enable
using System.IO;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

/// <summary>
/// Объединённый отчёт учебного года: источники, свод по направлениям, группы, лагерь.
/// </summary>
public sealed class YearConsolidatedReportExporter
{
    private readonly AdminReportExporter _adminExporter = new();
    private readonly DirectionsYearReportExporter _directionsExporter = new();

    public string Export(AdminReportData adminData, DirectionsYearReportData directionsData, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        _directionsExporter.AppendToWorkbook(workbook, directionsData);
        _adminExporter.AppendToWorkbook(workbook, adminData);
        workbook.SaveAs(outputPath);
        return outputPath;
    }

    public static string SuggestFileName(AdminReportRequest request) =>
        AdminReportExporter.SuggestFileName(request);
}
