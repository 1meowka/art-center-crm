#nullable enable
using System;
using System.Linq;

namespace VKR.Models;

// =====================================================================
// Договоры
// =====================================================================
public sealed class Contract
{
    public int IdContract { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime ContractDate { get; set; } = DateTime.Today;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int IdStudent { get; set; }
    public int? IdRepresentative { get; set; }
    public int? IdDirection { get; set; }
    public int? IdGroup { get; set; }
    public int IdStudentType { get; set; }
    /// <summary>Копия названия направления на момент сохранения (если справочник изменят).</summary>
    public string? DirectionNameSnapshot { get; set; }
    public int LessonsCount { get; set; }
    public short TimesPerWeek { get; set; }
    public decimal PriceNoDiscount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "активен";
    public string? Comment { get; set; }
    public int? IdCreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int IdBranch { get; set; } = 1;
}

public sealed class ContractListItem
{
    public int IdContract { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public int? IdRepresentative { get; set; }
    public string RepresentativeFullName { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public DateTime ContractDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Outstanding => Math.Max(0, TotalAmount - PaidAmount);
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public int IdBranch { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string BranchNameText => string.IsNullOrWhiteSpace(BranchName) ? "—" : BranchName;
    public string RepresentativeFullNameText =>
        string.IsNullOrWhiteSpace(RepresentativeFullName) ? "—" : RepresentativeFullName;
    public string StudentFullNameText =>
        string.IsNullOrWhiteSpace(StudentFullName) ? "—" : StudentFullName;

    public string ContractDateText => ContractDate.ToString("dd.MM.yyyy");
    public string StartDateText => StartDate?.ToString("dd.MM.yyyy") ?? "—";
    public string EndDateText => EndDate?.ToString("dd.MM.yyyy") ?? "—";
    public string TotalAmountText => TotalAmount.ToString("N2") + " ₽";
    public string PaidAmountText => PaidAmount.ToString("N2") + " ₽";
    public string OutstandingText => Outstanding.ToString("N2") + " ₽";
}

public sealed class ContractStudentInfo
{
    public int IdStudent { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public int Age
    {
        get
        {
            var today = DateTime.Today;
            var age = today.Year - BirthDate.Year;
            if (BirthDate.Date > today.AddYears(-age))
                age--;
            return age;
        }
    }
    public bool IsMinor => Age < 18;
}

// =====================================================================
// Оплаты
// =====================================================================
public sealed class Payment
{
    public int IdPayment { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public int? IdContract { get; set; }
    public int? IdStudent { get; set; }
    public decimal Amount { get; set; }
    public decimal? AmountBase { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int IdPaymentMethod { get; set; }
    public int IdServiceType { get; set; }
    public int? IdManager { get; set; }
    public bool CashReceiptIssued { get; set; }
    public short? PeriodMonth { get; set; }
    public short? PeriodYear { get; set; }
    public int? IdCreatedBy { get; set; }
    public string? Comment { get; set; }
    public DateTime? ActualDate { get; set; }
    public string? PaymentKind { get; set; }
    public string? EventText { get; set; }
    public string? PeriodLabel { get; set; }
}

public sealed class PaymentListItem
{
    public int IdPayment { get; set; }
    public DateTime PaymentDate { get; set; }
    public int? IdContract { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public int? IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? AmountBase { get; set; }
    public decimal? DiscountPercent { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string ServiceTypeName { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public short? TimesPerWeek { get; set; }
    public string? Comment { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public short? PeriodMonth { get; set; }
    public short? PeriodYear { get; set; }
    public string ManagerName { get; set; } = string.Empty;
    public int? IdBranch { get; set; }
    public DateTime? ActualDate { get; set; }
    public string? PaymentKind { get; set; }
    public string? EventText { get; set; }
    public string? PeriodLabel { get; set; }
    public string TeacherName { get; set; } = string.Empty;

    public string PaymentDateText => PaymentDate.ToString("dd.MM.yyyy");
    public string ActualDateText => ActualDate?.ToString("dd.MM.yyyy") ?? "—";
    public string PaymentKindText => string.IsNullOrWhiteSpace(PaymentKind) ? "—" : PaymentKind;
    public string EventTextDisplay => string.IsNullOrWhiteSpace(EventText) ? "—" : EventText;
    public string CourseText
    {
        get
        {
            var dir = string.IsNullOrWhiteSpace(DirectionName) ? "" : DirectionName.Trim();
            var times = TimesPerWeek is > 0 ? $"{TimesPerWeek} р/нед." : "";
            var eventPart = string.IsNullOrWhiteSpace(EventText) ? "" : EventText.Trim();
            var parts = new[] { dir, times, eventPart }.Where(x => !string.IsNullOrWhiteSpace(x));
            var text = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }
    }
    public string AmountText => Amount.ToString("N2") + " ₽";
    public string DiscountText => DiscountPercent is > 0 ? $"{DiscountPercent:0.##} %" : "—";
    public string HasContractText => IdContract is > 0 ? "да" : "нет";
    public string DirectionNameText => string.IsNullOrWhiteSpace(DirectionName) ? "—" : DirectionName;
    public string TimesPerWeekText => TimesPerWeek is > 0 ? TimesPerWeek.Value.ToString() : "—";
    public string ServiceTypeNameText => string.IsNullOrWhiteSpace(ServiceTypeName) ? "—" : ServiceTypeName;
    public string ContractNumberText => string.IsNullOrWhiteSpace(ContractNumber) ? "—" : ContractNumber;
    public string StudentFullNameText => string.IsNullOrWhiteSpace(StudentFullName) ? "—" : StudentFullName;
    public string GroupNameText => string.IsNullOrWhiteSpace(GroupName) ? "—" : GroupName;
    public string PeriodText =>
        !string.IsNullOrWhiteSpace(PeriodLabel)
            ? PeriodLabel
            : PeriodMonth is > 0 && PeriodYear is > 0
                ? $"{PeriodMonth:00}.{PeriodYear}"
                : "—";
    public string ManagerNameText => string.IsNullOrWhiteSpace(ManagerName) ? "—" : ManagerName;
}

public enum DebtorSortKey
{
    OverdueDesc,
    OverdueAsc,
    AmountDesc,
    AmountAsc,
    NameAsc,
    NameDesc,
    OccurrenceDesc,
    OccurrenceAsc
}

public enum DebtorDisplayFilter
{
    All,
    AwaitingPayment,
    Overdue,
    Deferred
}

public enum DebtOverdueLevel
{
    Fresh,
    Warning,
    Critical,
    Deferred
}

// =====================================================================
// Должники (строка таблицы public.debt)
// Заказчик требует видеть: период, название занятия, ФИО клиента,
// сумма долга, оплаченная сумма, комментарий, дата оплаты.
// =====================================================================
public sealed class DebtorItem
{
    public int IdDebt { get; set; }
    public int IdContract { get; set; }
    public int IdStudent { get; set; }

    public string StudentFullName { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;

    // «Название занятия» = направление по договору
    public string DirectionName { get; set; } = string.Empty;

    // Период (берём из договора: start_date — end_date)
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }

    // Финансы
    public decimal DebtAmount { get; set; }    // сумма долга
    public decimal PaidAmount { get; set; }    // оплачено по этому долгу
    public decimal Outstanding => Math.Max(0, DebtAmount - PaidAmount);

    public DateTime? RepaymentDate { get; set; }   // дата оплаты долга
    public DateTime? OccurrenceDate { get; set; }  // дата возникновения задолженности
    public DateTime? DueDate { get; set; }         // срок оплаты (до 20-го след. месяца)
    public DateTime? DeferUntil { get; set; }      // отсрочка до
    public string? DeferComment { get; set; }      // комментарий к отсрочке
    public string? Comment { get; set; }            // комментарий (debt.note)
    public string Status { get; set; } = string.Empty;

    public bool IsDeferred => DeferUntil.HasValue && DeferUntil.Value.Date >= DateTime.Today;

    public DateTime EffectiveDueDate => VKR.Utilities.DebtDueDateHelper.GetEffectiveDueDate(
        DueDate, OccurrenceDate, DeferUntil);

    public int DaysOverdue => VKR.Utilities.DebtDueDateHelper.GetDaysOverdue(
        EffectiveDueDate, Outstanding, IsDeferred);

    public DebtOverdueLevel OverdueLevel
    {
        get
        {
            if (IsDeferred)
                return DebtOverdueLevel.Deferred;
            if (Outstanding <= 0)
                return DebtOverdueLevel.Fresh;
            if (DateTime.Today <= EffectiveDueDate)
                return DebtOverdueLevel.Fresh;
            return DaysOverdue switch
            {
                > 7 => DebtOverdueLevel.Critical,
                >= 1 => DebtOverdueLevel.Warning,
                _ => DebtOverdueLevel.Fresh
            };
        }
    }

    public string OverdueLevelText => OverdueLevel switch
    {
        DebtOverdueLevel.Deferred => "отсрочка",
        DebtOverdueLevel.Critical => "просрочка",
        DebtOverdueLevel.Warning => "просрочка",
        _ when Outstanding > 0 && DateTime.Today <= EffectiveDueDate => "ожидает оплаты",
        _ => "до срока"
    };

    public string RowToneKey => OverdueLevel switch
    {
        DebtOverdueLevel.Deferred => "deferred",
        DebtOverdueLevel.Critical => "critical",
        DebtOverdueLevel.Warning => "warning",
        _ => "fresh"
    };

    public string DaysOverdueText
    {
        get
        {
            if (Outstanding <= 0)
                return "—";
            if (IsDeferred)
                return $"до {DeferUntil!.Value:dd.MM}";
            if (DateTime.Today <= EffectiveDueDate)
                return $"до {EffectiveDueDate:dd.MM}";
            return $"{DaysOverdue} дн.";
        }
    }

    public string DueDateText => EffectiveDueDate.ToString("dd.MM.yyyy");
    public string OccurrenceDateText => OccurrenceDate?.ToString("dd.MM.yyyy") ?? "—";
    public string DeferInfoText => IsDeferred
        ? (string.IsNullOrWhiteSpace(DeferComment) ? "отсрочка" : DeferComment)
        : CommentText;

    public string CommentText => string.IsNullOrWhiteSpace(Comment) ? "—" : Comment;

    // Форматированные значения для DataGrid (единый стиль "0,00 ₽" и dd.MM.yyyy)
    public string PeriodText
    {
        get
        {
            if (PeriodStart.HasValue && PeriodEnd.HasValue)
                return $"{PeriodStart.Value:dd.MM.yyyy} — {PeriodEnd.Value:dd.MM.yyyy}";
            if (PeriodEnd.HasValue)
                return $"до {PeriodEnd.Value:dd.MM.yyyy}";
            return "—";
        }
    }

    public string DebtAmountText      => DebtAmount.ToString("N2") + " ₽";
    public string PaidAmountText      => PaidAmount.ToString("N2") + " ₽";
    public string OutstandingText     => Outstanding.ToString("N2") + " ₽";
    public string RepaymentDateText   => RepaymentDate?.ToString("dd.MM.yyyy") ?? "—";
    public string DirectionDisplayText => string.IsNullOrWhiteSpace(DirectionName) ? "—" : DirectionName;
    public string StatusText          => string.IsNullOrWhiteSpace(Status) ? "—" : Status;

    // Просрочка: оплата по сей день не пришла, период истёк, остаток есть
    public bool IsOverdue =>
        Outstanding > 0
        && PeriodEnd.HasValue
        && PeriodEnd.Value < DateTime.Today
        && RepaymentDate == null;
    public string OverdueText => IsOverdue ? "ПРОСРОЧКА" : "—";
}

// =====================================================================
// Финансовая сводка
// =====================================================================
public sealed class FinancialSummary
{
    public decimal TotalContracts { get; set; }     // сумма по всем активным договорам
    public decimal TotalPaid { get; set; }          // оплачено
    public decimal TotalDebt { get; set; }          // долг
    public int DebtorsCount { get; set; }           // кол-во должников

    public string TotalContractsText => TotalContracts.ToString("N2") + " ₽";
    public string TotalPaidText      => TotalPaid.ToString("N2") + " ₽";
    public string TotalDebtText      => TotalDebt.ToString("N2") + " ₽";
}

// =====================================================================
// Продажи
// =====================================================================
public enum SaleSortKey
{
    DateDesc,
    DateAsc,
    AmountDesc,
    AmountAsc,
    ItemAsc,
    ItemDesc,
    StudentAsc,
    StudentDesc
}

public enum SaleTypeFilter
{
    All,
    TrialOnly,
    OtherOnly
}

public sealed class Sale
{
    public int IdSale { get; set; }
    public DateTime SaleDate { get; set; } = DateTime.Today;
    public int? IdStudent { get; set; }
    public int? IdProduct { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public int? IdPaymentMethod { get; set; }
    public string? Comment { get; set; }
}

public sealed class SaleListItem
{
    public int IdSale { get; set; }
    public DateTime SaleDate { get; set; }
    public int? IdStudent { get; set; }
    public string ClientFullName { get; set; } = string.Empty;
    public string StudentFullName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public string SaleDateText => SaleDate.ToString("dd.MM.yyyy");
    public string UnitPriceText => UnitPrice.ToString("N2") + " ₽";
    public string TotalAmountText => TotalAmount.ToString("N2") + " ₽";
    public string ClientFullNameText => string.IsNullOrWhiteSpace(ClientFullName) ? "—" : ClientFullName;
    public string StudentFullNameText => ClientFullNameText;
    public string PaymentMethodNameText => string.IsNullOrWhiteSpace(PaymentMethodName) ? "—" : PaymentMethodName;

    public bool IsTrialLesson =>
        ItemName.Contains("пробное", StringComparison.OrdinalIgnoreCase) ||
        (Comment?.Contains("пробное", StringComparison.OrdinalIgnoreCase) ?? false);

    public string SaleTypeText => IsTrialLesson ? "Пробное занятие" : "Товар/услуга";
}

// =====================================================================
// Расходы
// =====================================================================
public sealed class Expense
{
    public int IdExpense { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public int IdExpenseType { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? IdPaymentMethod { get; set; }
    public string? Comment { get; set; }
    public int? IdCreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ExpenseListItem
{
    public int IdExpense { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public string ExpenseDateText => ExpenseDate.ToString("dd.MM.yyyy");
    public string AmountText => Amount.ToString("N2") + " ₽";
    public string PaymentMethodNameText => string.IsNullOrWhiteSpace(PaymentMethodName) ? "—" : PaymentMethodName;
}

public sealed class ExpenseByCategory
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string TotalText => Total.ToString("N2") + " ₽";
}

// =====================================================================
// Прибыль за период
// =====================================================================
public sealed class ProfitSummary
{
    public decimal IncomePayments { get; set; }   // оплаты
    public decimal IncomeSales    { get; set; }   // продажи
    public decimal Expenses       { get; set; }   // расходы

    public decimal TotalIncome => IncomePayments + IncomeSales;
    public decimal Profit      => TotalIncome - Expenses;

    public string IncomePaymentsText => IncomePayments.ToString("N2") + " ₽";
    public string IncomeSalesText    => IncomeSales.ToString("N2") + " ₽";
    public string TotalIncomeText    => TotalIncome.ToString("N2") + " ₽";
    public string ExpensesText       => Expenses.ToString("N2") + " ₽";
    public string ProfitText         => Profit.ToString("N2") + " ₽";
}
