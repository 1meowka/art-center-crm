#nullable enable
using System;
using System.Collections.Generic;

namespace VKR.Models;

public sealed class AdminReportRequest
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int AcademicYearStart { get; set; }
}

public sealed class AdminReportData
{
    public AdminReportRequest Request { get; set; } = new();
    public List<LeadSourceStatRow> LeadSources { get; set; } = new();
    public List<DirectionGroupReport> DirectionGroups { get; set; } = new();
    public List<CampStudentRow> CampStudents { get; set; } = new();
}

public sealed class LeadSourceStatRow
{
    public string SourceName { get; set; } = "Не указан";
    public int LeadsCount { get; set; }
    public int ConvertedLeadsCount { get; set; }
    public int NewStudentsCount { get; set; }
    public int ContractsCount { get; set; }
    public decimal Revenue { get; set; }
}

public sealed class DirectionGroupReport
{
    public string DirectionName { get; set; } = string.Empty;
    public List<GroupReportSection> Groups { get; set; } = new();
}

public sealed class GroupReportSection
{
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string ScheduleText { get; set; } = string.Empty;
    public List<GroupStudentReportRow> Students { get; set; } = new();
    public decimal[] MonthlyTotals { get; set; } = new decimal[10];
}

public sealed class GroupStudentReportRow
{
    public int RowNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public decimal? DiscountPercent { get; set; }
    public decimal? ContractAmount { get; set; }
    public string MotherName { get; set; } = string.Empty;
    public string MotherPhone { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string FatherPhone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string AgeGroup { get; set; } = string.Empty;
    public string ResidenceAddress { get; set; } = string.Empty;
    public string SchoolInfo { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string BirthCertificate { get; set; } = string.Empty;
    public DateTime? ArrivalDate { get; set; }
    public DateTime? DepartureDate { get; set; }
    public int? FullYears { get; set; }
    public decimal[] MonthlyPayments { get; set; } = new decimal[10];
    public string SourceName { get; set; } = string.Empty;
}

public sealed class CampStudentRow
{
    public string SourceName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ShiftInfo { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Representative1Name { get; set; } = string.Empty;
    public string Representative1Phone { get; set; } = string.Empty;
    public string Representative2Name { get; set; } = string.Empty;
    public string Representative2Phone { get; set; } = string.Empty;
    public string PaymentNote { get; set; } = string.Empty;
    public string BirthCertificate { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string Comment { get; set; } = string.Empty;
}
