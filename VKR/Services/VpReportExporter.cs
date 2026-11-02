#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public sealed class VpReportExporter
{
    private const int SalesSummaryRow = 18;
    private const int SalesFirstDataRow = 19;
    private const int ContractsFirstDataRow = 3;
    private const int TrialsFirstDataRow = 4;
    private const int NewcomersFirstDataRow = 5;
    private const int DebtorsFirstDataRow = 10;
    private const int ChurnedFirstDataRow = 5;

    private static readonly int[] PlanMetricRows = [6, 7, 8, 9, 10, 11, 13, 15, 16, 17, 18, 20, 21, 22, 23, 24];
    private static readonly int[] IntensityBlockStarts = [4, 25];

    public string Export(
        PpoReportRequest request,
        IReadOnlyList<VpSalesRegisterLine> salesLines,
        IReadOnlyList<VpContractExportRow> contracts,
        IReadOnlyList<VpContractExportRow> academicYearContracts,
        IReadOnlyList<VpTrialLessonRow> trials,
        IReadOnlyList<VpNewcomerRow> newcomers,
        IReadOnlyList<VpDebtorExportRow> debtors,
        IReadOnlyList<VpChurnedRow> churned,
        IReadOnlyList<VpMonthBucketMetrics> monthMetrics,
        VpKpiPlanTargets? kpiPlan,
        int academicYearStart,
        string outputPath)
    {
        var templatePath = ResolveTemplatePath();
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Не найден шаблон ВП.", templatePath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook(templatePath);

        FillSalesRegister(workbook.Worksheet("ведомость продаж"), request, salesLines);
        FillContractsTable(workbook.Worksheet("табл договоров"), contracts);
        FillAcademicYearStudents(workbook, academicYearContracts, academicYearStart);
        FillTrialLessons(workbook.Worksheet("1.Пробные занятия"), trials);
        FillNewcomers(workbook.Worksheet("2.Новички"), newcomers);
        FillDebtors(workbook.Worksheet("3.Дебиторка"), debtors);
        FillChurned(workbook.Worksheet("4.Ушедшие (предыдущий)"), churned);
        FillPlans(workbook.Worksheet("планы"), monthMetrics, kpiPlan);
        FillIntensity(workbook.Worksheet("интенсивность"), monthMetrics, kpiPlan);

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    public static string SuggestFileName(PpoReportRequest request) =>
        request.DateFrom.Date == request.DateTo.Date
            ? $"Операционный_отчёт_{request.DateFrom:yyyyMMdd}.xlsx"
            : $"Операционный_отчёт_{request.DateFrom:yyyyMMdd}_{request.DateTo:yyyyMMdd}.xlsx";

    private static string ResolveTemplatePath() =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "Templates", "VpReportTemplate.xlsx");

    private static void FillSalesRegister(IXLWorksheet ws, PpoReportRequest request, IReadOnlyList<VpSalesRegisterLine> lines)
    {
        ws.Cell(7, 1).Value = request.DateFrom;
        ws.Cell(7, 2).Value = request.DateTo;
        ClearRange(ws, SalesSummaryRow, SalesSummaryRow, 1, 18);
        ClearDataArea(ws, SalesFirstDataRow, 1, 18);

        var filtered = lines
            .Where(x => x.Date.Date >= request.DateFrom.Date && x.Date.Date <= request.DateTo.Date)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Kind)
            .ToList();

        var row = SalesFirstDataRow;
        foreach (var dayGroup in filtered.GroupBy(x => x.Date.Date))
        {
            var seq = 1;
            decimal svcCash = 0, svcSber = 0, svcTerminal = 0, svcQr = 0;
            decimal shopCash = 0, shopSber = 0, shopTerminal = 0, shopQr = 0;

            foreach (var line in dayGroup.OrderBy(x => x.Kind).ThenBy(x => x.Amount))
            {
                ws.Cell(row, 1).Value = VpReportSupport.FormatRegisterDate(line.Date);
                ws.Cell(row, 2).Value = seq;
                ws.Cell(row, 3).Value = line.ClientFullName;
                ws.Cell(row, 4).Value = line.ServiceTypeName;
                ws.Cell(row, 5).Value = line.DirectionName;
                ws.Cell(row, 6).Value = line.TeacherName;
                ws.Cell(row, 7).Value = line.PeriodText;
                ws.Cell(row, 8).Value = line.ManagerName;
                ws.Cell(row, 9).Value = (double)line.Amount;
                ApplyChannel(ws, row, 10, line, PpoPaymentChannel.Cash, ref svcCash, ref shopCash);
                ApplyChannel(ws, row, 11, line, PpoPaymentChannel.Sber, ref svcSber, ref shopSber);
                ApplyChannel(ws, row, 12, line, PpoPaymentChannel.Terminal, ref svcTerminal, ref shopTerminal);
                ApplyChannel(ws, row, 13, line, PpoPaymentChannel.Qr, ref svcQr, ref shopQr);
                ws.Cell(row, 14).Value = line.CashReceiptIssued ? "да" : "";
                ws.Cell(row, 15).Value = line.Note;
                ws.Cell(row, 16).Value = line.DiscountText;
                ws.Cell(row, 17).Value = line.SourceName;
                ws.Cell(row, 18).Value = line.ClientCategoryCode;
                row++;
                seq++;
            }

            ws.Cell(row, 1).Value = VpReportSupport.FormatRegisterDate(dayGroup.Key);
            ws.Cell(row, 2).Value = "УСЛУГИ";
            ws.Cell(row, 3).Value = "МЕНЕДЖЕР ФИО";
            if (svcCash > 0) ws.Cell(row, 10).Value = (double)svcCash;
            if (svcSber > 0) ws.Cell(row, 11).Value = (double)svcSber;
            if (svcTerminal > 0) ws.Cell(row, 12).Value = (double)svcTerminal;
            if (svcQr > 0) ws.Cell(row, 13).Value = (double)svcQr;
            row++;

            var shopTotal = shopCash + shopSber + shopTerminal + shopQr;
            if (shopTotal > 0)
            {
                ws.Cell(row, 1).Value = VpReportSupport.FormatRegisterDate(dayGroup.Key);
                ws.Cell(row, 2).Value = "МАГАЗИН";
                if (shopCash > 0) ws.Cell(row, 10).Value = (double)shopCash;
                if (shopSber > 0) ws.Cell(row, 11).Value = (double)shopSber;
                if (shopTerminal > 0) ws.Cell(row, 12).Value = (double)shopTerminal;
                if (shopQr > 0) ws.Cell(row, 13).Value = (double)shopQr;
                row++;
            }
        }
    }

    private static void ApplyChannel(
        IXLWorksheet ws,
        int row,
        int col,
        VpSalesRegisterLine line,
        PpoPaymentChannel targetChannel,
        ref decimal serviceTotal,
        ref decimal shopTotal)
    {
        if (line.Channel != targetChannel)
            return;

        ws.Cell(row, col).Value = (double)line.Amount;
        if (line.Kind == "shop")
            shopTotal += line.Amount;
        else
            serviceTotal += line.Amount;
    }

    private static void FillContractsTable(IXLWorksheet ws, IReadOnlyList<VpContractExportRow> contracts)
    {
        ClearDataArea(ws, ContractsFirstDataRow, 1, 16);
        var row = ContractsFirstDataRow;
        var seq = 1;
        foreach (var item in contracts)
        {
            ws.Cell(row, 1).Value = seq;
            ws.Cell(row, 2).Value = item.StudentFullName;
            ws.Cell(row, 3).Value = item.DiscountText;
            ws.Cell(row, 4).Value = (double)item.TotalAmount;
            ws.Cell(row, 5).Value = VpReportSupport.FormatShortDate(item.ContractDate);
            ws.Cell(row, 6).Value = item.DirectionName;
            ws.Cell(row, 7).Value = item.TeacherName;
            ws.Cell(row, 8).Value = item.Phone;
            ws.Cell(row, 9).Value = item.TimesPerWeek;
            ws.Cell(row, 10).Value = item.GroupName;
            ws.Cell(row, 11).Value = item.TaxNote;
            ws.Cell(row, 12).Value = item.ClientCategoryCode;
            ws.Cell(row, 13).Value = item.TerminationText;
            ws.Cell(row, 14).Value = VpReportSupport.FormatBirthDate(item.BirthDate);
            ws.Cell(row, 15).Value = item.SourceName;
            ws.Cell(row, 16).Value = item.ManagerName;
            row++;
            seq++;
        }
    }

    private static void FillAcademicYearStudents(
        XLWorkbook workbook,
        IReadOnlyList<VpContractExportRow> contracts,
        int academicYearStart)
    {
        var sheetName = $"{academicYearStart % 100:D2}-{(academicYearStart + 1) % 100:D2}";
        var ws = GetOrCreateWorksheet(workbook, sheetName, "табл договоров");
        FillContractsTable(ws, contracts);
    }

    private static IXLWorksheet GetOrCreateWorksheet(XLWorkbook workbook, string name, string cloneFrom)
    {
        if (workbook.Worksheets.TryGetWorksheet(name, out var existing))
            return existing;

        if (workbook.Worksheets.TryGetWorksheet(cloneFrom, out var template))
            return template.CopyTo(name);

        return workbook.Worksheets.Add(name);
    }

    private static void FillTrialLessons(IXLWorksheet ws, IReadOnlyList<VpTrialLessonRow> trials)
    {
        ClearDataArea(ws, TrialsFirstDataRow, 1, 12);
        var row = TrialsFirstDataRow;
        foreach (var monthGroup in trials.GroupBy(x => new { x.ScheduledAt.Year, x.ScheduledAt.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            ws.Cell(row, 3).Value = VpReportSupport.MonthTitleLower(monthGroup.Key.Month);
            row++;

            var seq = 1;
            foreach (var item in monthGroup.OrderBy(x => x.ScheduledAt))
            {
                ws.Cell(row, 1).Value = seq;
                ws.Cell(row, 2).Value = item.ScheduledAt;
                ws.Cell(row, 3).Value = item.ChildFullName;
                ws.Cell(row, 4).Value = item.AgeText;
                ws.Cell(row, 5).Value = item.ParentInfo;
                ws.Cell(row, 6).Value = item.Phone;
                ws.Cell(row, 7).Value = item.DirectionName;
                ws.Cell(row, 8).Value = item.TeacherName;
                ws.Cell(row, 9).Value = item.ContractDate;
                ws.Cell(row, 10).Value = item.Note;
                ws.Cell(row, 11).Value = item.SourceName;
                row++;
                seq++;
            }

            row++;
        }
    }

    private static void FillNewcomers(IXLWorksheet ws, IReadOnlyList<VpNewcomerRow> newcomers)
    {
        ClearDataArea(ws, NewcomersFirstDataRow, 1, 8);
        var row = NewcomersFirstDataRow;
        foreach (var monthGroup in newcomers.GroupBy(x => new { x.VisitDate.Year, x.VisitDate.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            ws.Cell(row, 2).Value = VpReportSupport.MonthTitleUpper(monthGroup.Key.Month);
            row++;

            var seq = 1;
            foreach (var item in monthGroup.OrderBy(x => x.VisitDate))
            {
                ws.Cell(row, 1).Value = seq;
                ws.Cell(row, 2).Value = VpReportSupport.FormatShortDate(item.VisitDate);
                ws.Cell(row, 3).Value = item.ClientFullName;
                ws.Cell(row, 4).Value = item.Amount > 0 ? (double)item.Amount : "";
                ws.Cell(row, 5).Value = item.TeacherName;
                ws.Cell(row, 6).Value = item.DirectionName;
                ws.Cell(row, 7).Value = item.ManagerName;
                ws.Cell(row, 8).Value = item.Comment;
                row++;
                seq++;
            }

            row++;
        }
    }

    private static void FillDebtors(IXLWorksheet ws, IReadOnlyList<VpDebtorExportRow> debtors)
    {
        ClearDataArea(ws, DebtorsFirstDataRow, 1, 8);
        var row = DebtorsFirstDataRow;
        foreach (var monthGroup in debtors.GroupBy(x => new { x.OccurrenceDate.Year, x.OccurrenceDate.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            ws.Cell(row, 1).Value = VpReportSupport.MonthDebtTitle(monthGroup.Key.Month);
            row++;

            var seq = 1;
            foreach (var item in monthGroup.OrderBy(x => x.OccurrenceDate))
            {
                ws.Cell(row, 1).Value = seq;
                ws.Cell(row, 2).Value = item.DirectionName;
                ws.Cell(row, 3).Value = item.ClientFullName;
                ws.Cell(row, 4).Value = (double)item.DebtAmount;
                ws.Cell(row, 5).Value = item.PaidAmount > 0 ? (double)item.PaidAmount : "";
                ws.Cell(row, 6).Value = item.ManagerName;
                ws.Cell(row, 7).Value = item.Note;
                if (item.RepaymentDate.HasValue)
                    ws.Cell(row, 8).Value = VpReportSupport.FormatShortDate(item.RepaymentDate.Value);
                row++;
                seq++;
            }

            row++;
        }
    }

    private static void FillChurned(IXLWorksheet ws, IReadOnlyList<VpChurnedRow> churned)
    {
        ClearDataArea(ws, ChurnedFirstDataRow, 1, 9);
        var row = ChurnedFirstDataRow;
        foreach (var monthGroup in churned.GroupBy(x => new { x.TerminationDate.Year, x.TerminationDate.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month))
        {
            ws.Cell(row, 2).Value = VpReportSupport.MonthTitleLower(monthGroup.Key.Month);
            row++;

            var seq = 1;
            foreach (var item in monthGroup.OrderBy(x => x.TerminationDate))
            {
                ws.Cell(row, 1).Value = seq;
                ws.Cell(row, 2).Value = (double)item.Amount;
                ws.Cell(row, 3).Value = item.ClientFullName;
                ws.Cell(row, 4).Value = VpReportSupport.FormatShortDate(item.TerminationDate);
                ws.Cell(row, 6).Value = item.TeacherDirectionText;
                ws.Cell(row, 7).Value = item.ManagerName;
                ws.Cell(row, 9).Value = item.Comment;
                row++;
                seq++;
            }

            row++;
        }
    }

    private static void FillPlans(
        IXLWorksheet ws,
        IReadOnlyList<VpMonthBucketMetrics> monthMetrics,
        VpKpiPlanTargets? kpiPlan)
    {
        ClearPlansSheet(ws);
        if (monthMetrics.Count == 0)
            return;

        var metrics = monthMetrics[^1];
        ws.Cell(3, 2).Value = VpReportSupport.MonthTitleLower(metrics.Month);
        if (kpiPlan != null && !string.IsNullOrWhiteSpace(kpiPlan.PlanName))
            ws.Cell(3, 8).Value = $"план: {kpiPlan.PlanName}";

        WriteMetricRow(ws, 6, metrics.ServicePaymentsByWeek, metrics.ServicePaymentsTotal, kpiPlan?.ServiceRevenue);
        WriteMetricRow(ws, 7, metrics.ShopSalesByWeek, metrics.ShopSalesTotal, null);
        WriteMetricValue(ws, 8, metrics.TrialLessons, null);
        WriteMetricValue(ws, 9, metrics.ContractsCount, kpiPlan?.ContractsCount);
        WriteMetricValue(ws, 10, metrics.UniqueClients, kpiPlan?.NewStudents);
        WriteMetricValue(ws, 11, metrics.NewContracts, kpiPlan?.ContractsCount);
        WriteMetricValue(ws, 13, metrics.ActiveGroups, null);
        WriteMetricValue(ws, 15, metrics.ChurnedCount, kpiPlan?.MaxChurn);
        WriteMetricValue(ws, 16, metrics.DebtorsCount, null);
        WriteMetricValue(ws, 17, (double)metrics.DebtorsAmount, null);
        WriteMetricValue(ws, 18, (double)metrics.AverageCheck, null);
        WriteMetricValue(ws, 23, metrics.AttendanceCount, kpiPlan?.AttendancePercent);
    }

    private static void FillIntensity(
        IXLWorksheet ws,
        IReadOnlyList<VpMonthBucketMetrics> monthMetrics,
        VpKpiPlanTargets? kpiPlan)
    {
        foreach (var blockStart in IntensityBlockStarts)
            ClearIntensityBlock(ws, blockStart);

        var blockStarts = IntensityBlockStarts;
        for (var i = 0; i < monthMetrics.Count && i < blockStarts.Length; i++)
        {
            var block = blockStarts[i];
            var metrics = monthMetrics[i];
            var plan = i == monthMetrics.Count - 1 ? kpiPlan : null;

            ws.Cell(block, 1).Value = $"{VpReportSupport.MonthTitleUpper(metrics.Month)} {metrics.Year}";
            WriteIntensityMetric(ws, block + 1, metrics.ServicePaymentsByWeek, metrics.ServicePaymentsTotal, plan?.ServiceRevenue);
            WriteIntensityMetric(ws, block + 2, metrics.ShopSalesByWeek, metrics.ShopSalesTotal, null);
            WriteIntensityValue(ws, block + 6, metrics.UniqueClients, plan?.NewStudents);
            WriteIntensityValue(ws, block + 7, metrics.ContractsCount, plan?.ContractsCount);
            WriteIntensityValue(ws, block + 8, (double)metrics.AverageCheck, null);
            WriteIntensityValue(ws, block + 9, metrics.TrialLessons, null);
            WriteIntensityValue(ws, block + 10, metrics.NewContracts, plan?.ContractsCount);
            WriteIntensityValue(ws, block + 13, metrics.ChurnedCount, plan?.MaxChurn);
            WriteIntensityValue(ws, block + 17, metrics.DebtorsCount, null);
            WriteIntensityValue(ws, block + 18, (double)metrics.DebtorsAmount, null);
            WriteIntensityValue(ws, block + 15, metrics.AttendanceCount, plan?.AttendancePercent);
        }
    }

    private static void WriteMetricRow(IXLWorksheet ws, int row, decimal[] buckets, decimal total, decimal? plan)
    {
        for (var i = 0; i < 4; i++)
            ws.Cell(row, 3 + i).Value = buckets[i] > 0 ? (double)buckets[i] : 0;
        ws.Cell(row, 7).Value = (double)total;
        if (plan.HasValue)
        {
            ws.Cell(row, 8).Value = (double)plan.Value;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
        }
    }

    private static void WriteMetricValue(IXLWorksheet ws, int row, double value, decimal? plan)
    {
        ws.Cell(row, 7).Value = value;
        if (plan.HasValue)
        {
            ws.Cell(row, 8).Value = (double)plan.Value;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
        }
    }

    private static void WriteIntensityMetric(IXLWorksheet ws, int row, decimal[] buckets, decimal total, decimal? plan)
    {
        for (var i = 0; i < 4; i++)
            ws.Cell(row, 2 + i).Value = buckets[i] > 0 ? (double)buckets[i] : 0;
        ws.Cell(row, 7).Value = (double)total;
        ws.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        if (plan.HasValue)
        {
            ws.Cell(row, 8).Value = (double)plan.Value;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
        }
    }

    private static void WriteIntensityValue(IXLWorksheet ws, int row, double value, decimal? plan)
    {
        ws.Cell(row, 7).Value = value;
        ws.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF200");
        if (plan.HasValue)
        {
            ws.Cell(row, 8).Value = (double)plan.Value;
            ws.Cell(row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#C6EFCE");
        }
    }

    private static void ClearPlansSheet(IXLWorksheet ws)
    {
        ws.Cell(3, 2).Value = "";
        ws.Cell(3, 8).Value = "";
        foreach (var row in PlanMetricRows)
            ClearRange(ws, row, row, 3, 8);
        ClearRange(ws, 19, 19, 2, 8);
    }

    private static void ClearIntensityBlock(IXLWorksheet ws, int blockStart)
    {
        ws.Cell(blockStart, 1).Value = "";
        for (var row = blockStart + 1; row <= blockStart + 20; row++)
            ClearRange(ws, row, row, 2, 8);
    }

    private static void ClearDataArea(IXLWorksheet ws, int firstRow, int firstCol, int lastCol)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstRow;
        if (lastRow < firstRow)
            lastRow = firstRow;
        ClearRange(ws, firstRow, lastRow, firstCol, lastCol);
    }

    private static void ClearRange(IXLWorksheet ws, int firstRow, int lastRow, int firstCol, int lastCol)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var col = firstCol; col <= lastCol; col++)
                ws.Cell(row, col).Value = "";
        }
    }
}
