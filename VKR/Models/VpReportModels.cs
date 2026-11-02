#nullable enable
using System;

namespace VKR.Models;

public sealed class VpSalesRegisterLine
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "service";
    public string ContractLabel { get; set; } = "";
    public string ClientFullName { get; set; } = "";
    public string ServiceTypeName { get; set; } = "";
    public string DirectionName { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public string PeriodText { get; set; } = "";
    public string ManagerName { get; set; } = "";
    public decimal Amount { get; set; }
    public PpoPaymentChannel Channel { get; set; }
    public bool CashReceiptIssued { get; set; }
    public string Note { get; set; } = "";
    public string DiscountText { get; set; } = "";
    public string SourceName { get; set; } = "";
    public int ClientCategoryCode { get; set; } = 3;
}

public sealed class VpContractExportRow
{
    public int IdContract { get; set; }
    public string StudentFullName { get; set; } = "";
    public string DiscountText { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public DateTime ContractDate { get; set; }
    public string DirectionName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public short TimesPerWeek { get; set; }
    public string GroupName { get; set; } = "";
    public string TaxNote { get; set; } = "";
    public int ClientCategoryCode { get; set; } = 3;
    public string TerminationText { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string SourceName { get; set; } = "";
    public string ManagerName { get; set; } = "";
}

public sealed class VpTrialLessonRow
{
    public DateTime ScheduledAt { get; set; }
    public string ChildFullName { get; set; } = "";
    public string AgeText { get; set; } = "";
    public string ParentInfo { get; set; } = "";
    public string Phone { get; set; } = "";
    public string DirectionName { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public DateTime? ContractDate { get; set; }
    public string Note { get; set; } = "";
    public string SourceName { get; set; } = "";
}

public sealed class VpNewcomerRow
{
    public DateTime VisitDate { get; set; }
    public string ClientFullName { get; set; } = "";
    public decimal Amount { get; set; }
    public string TeacherName { get; set; } = "";
    public string DirectionName { get; set; } = "";
    public string ManagerName { get; set; } = "";
    public string Comment { get; set; } = "";
}

public sealed class VpDebtorExportRow
{
    public DateTime OccurrenceDate { get; set; }
    public string DirectionName { get; set; } = "";
    public string ClientFullName { get; set; } = "";
    public decimal DebtAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string ManagerName { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime? RepaymentDate { get; set; }
}

public sealed class VpChurnedRow
{
    public decimal Amount { get; set; }
    public string ClientFullName { get; set; } = "";
    public DateTime TerminationDate { get; set; }
    public string TeacherDirectionText { get; set; } = "";
    public string ManagerName { get; set; } = "";
    public string Comment { get; set; } = "";
}

public sealed class VpMonthBucketMetrics
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal[] ServicePaymentsByWeek { get; } = new decimal[4];
    public decimal[] ShopSalesByWeek { get; } = new decimal[4];
    public decimal ServicePaymentsTotal { get; set; }
    public decimal ShopSalesTotal { get; set; }
    public int TrialLessons { get; set; }
    public int ContractsCount { get; set; }
    public int UniqueClients { get; set; }
    public int NewContracts { get; set; }
    public int ActiveGroups { get; set; }
    public int ChurnedCount { get; set; }
    public int DebtorsCount { get; set; }
    public decimal DebtorsAmount { get; set; }
    public decimal AverageCheck { get; set; }
    public int AttendanceCount { get; set; }
}

/// <summary>
/// Целевые значения KPI-плана, сопоставленные со строками листов «планы» и «интенсивность».
/// </summary>
public sealed class VpKpiPlanTargets
{
    public string PlanName { get; set; } = "";
    public decimal? ServiceRevenue { get; set; }
    public decimal? ContractsCount { get; set; }
    public decimal? NewStudents { get; set; }
    public decimal? MaxChurn { get; set; }
    public decimal? AttendancePercent { get; set; }
    public decimal? Leads { get; set; }

    public static VpKpiPlanTargets? FromPlan(KpiPlan? plan)
    {
        if (plan == null)
            return null;

        return new VpKpiPlanTargets
        {
            PlanName = plan.PlanName,
            ServiceRevenue = plan.PlannedRevenue > 0 ? plan.PlannedRevenue : null,
            ContractsCount = plan.PlannedContracts > 0 ? plan.PlannedContracts : null,
            NewStudents = plan.PlannedNewStudents > 0 ? plan.PlannedNewStudents : null,
            MaxChurn = plan.PlannedMaxChurn > 0 ? plan.PlannedMaxChurn : null,
            AttendancePercent = plan.PlannedAttendancePercent > 0 ? plan.PlannedAttendancePercent : null,
            Leads = plan.PlannedLeads > 0 ? plan.PlannedLeads : null
        };
    }
}
