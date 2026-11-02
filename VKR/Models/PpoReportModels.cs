#nullable enable
using System;

namespace VKR.Models;

public enum PpoPaymentChannel
{
    Terminal,
    Qr,
    Sber,
    Cash
}

public sealed class PpoDailyIncomeRow
{
    public DateTime Date { get; set; }
    public PpoPaymentChannel Channel { get; set; }
    public decimal Amount { get; set; }
}

public sealed class PpoExpenseExportRow
{
    public int IdExpense { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string VendorName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Details { get; set; } = "";
    public int OrgCode { get; set; }
    public int MonthCode => ExpenseDate.Month;
}

public sealed class PpoReportRequest
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
}
