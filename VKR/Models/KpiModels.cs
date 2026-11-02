#nullable enable
using System;
using System.Collections.Generic;

namespace VKR.Models;

/// <summary>
/// План KPI на период
/// </summary>
public class KpiPlan
{
    public int IdKpiPlan { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string PlanName { get; set; } = "";
    
    public int PlannedLeads { get; set; }
    public int PlannedNewStudents { get; set; }
    public int PlannedContracts { get; set; }
    public decimal PlannedRevenue { get; set; }
    public decimal PlannedAttendancePercent { get; set; }
    public int PlannedMaxChurn { get; set; }
    
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Фактические KPI (рассчитанные данные)
/// </summary>
public class KpiFact
{
    public int FactLeads { get; set; }
    public int FactNewStudents { get; set; }
    public int FactContracts { get; set; }
    public decimal FactRevenue { get; set; }
    public decimal FactAttendancePercent { get; set; }
    public int FactChurn { get; set; }
    public int FactActiveDebt { get; set; }
    public decimal FactConversionPercent { get; set; }
}

/// <summary>
/// Результат KPI (с процентом выполнения и статусом)
/// </summary>
public class KpiResult
{
    public string IndicatorName { get; set; } = "";
    public string PlanValue { get; set; } = "";
    public string FactValue { get; set; } = "";
    public decimal CompletionPercent { get; set; }
    public string Status { get; set; } = ""; // "выполнено", "частично", "не выполнено", "в норме", "превышен"
    public bool IsSpecial { get; set; } // для оттока статус наоборот
}

/// <summary>
/// Прогноз оттока обучающегося.
/// </summary>
public class ChurnPrediction
{
    public int IdChurnPrediction { get; set; }
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = "";
    public string StudentPhone { get; set; } = "";
    public decimal ChurnProbability { get; set; }
    public string RiskLevel { get; set; } = "";
    public string ChurnFactors { get; set; } = "";
    public int Priority { get; set; }
    public string RecommendedAction { get; set; } = "";
    public DateTime DateCalculated { get; set; }

    public string ChurnProbabilityText => ChurnProbability.ToString("P0");
}

/// <summary>
/// Прогноз конверсии заявки.
/// </summary>
public class LeadConversionPrediction
{
    public int IdPrediction { get; set; }
    public int IdLead { get; set; }
    public string LeadFullName { get; set; } = "";
    public string LeadPhone { get; set; } = "";
    public decimal ConversionProbability { get; set; }
    public string ProbabilityLevel { get; set; } = "";
    public string ConversionFactors { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
    public DateTime DateCalculated { get; set; }

    public string ConversionProbabilityText => ConversionProbability.ToString("P0");
}

public class AiRecommendationItem
{
    public int IdRecommendation { get; set; }
    public int EntityId { get; set; }
    public string? Title { get; set; }
    public string Description { get; set; } = "";
    public string? TriggeredSigns { get; set; }
    public string? RuleCode { get; set; }
    public string Type { get; set; } = "";
    public string Priority { get; set; } = "средний";
    public string? EntityName { get; set; }
    public string? EntityType { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsActionable { get; set; } = true;
}

/// <summary>
/// Запись журнала выполненных рекомендаций администратора.
/// </summary>
public class AiRecommendationHistoryItem
{
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string EntityDisplayName { get; set; } = "";
    public string RecommendationText { get; set; } = "";
    public string? RuleCode { get; set; }
    public string RecommendationType { get; set; } = "";
    public string? TriggeredSigns { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedByName { get; set; }

    public string CompletedAtText =>
        CompletedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";

    public string RuleCodeText =>
        string.IsNullOrWhiteSpace(RuleCode) ? "—" : RuleCode;
}

/// <summary>
/// Сводные показатели ИИ-аналитики.
/// </summary>
public class AiAnalyticsSummary
{
    public int ActiveStudentsCount { get; set; }
    public int ArchivedStudentsCount { get; set; }
    public int NewStudentsInPeriod { get; set; }
    public int LeftStudentsInPeriod { get; set; }
    public decimal ChurnRate { get; set; }
    public decimal ClientGrowthRate { get; set; }
    public decimal AttendanceRate { get; set; }
    public int HighRiskStudentsCount { get; set; }
    public int MediumRiskStudentsCount { get; set; }
    public int LowRiskStudentsCount { get; set; }
    public int ActiveLeadsCount { get; set; }
    public int HighConversionLeadsCount { get; set; }
    public int MediumConversionLeadsCount { get; set; }
    public int LowConversionLeadsCount { get; set; }
    public decimal ExpectedNewClients { get; set; }
    public decimal TotalDebt { get; set; }
    public int DebtorsCount { get; set; }
    public decimal AverageDebtRatio { get; set; }
    public int ActiveRecommendationsCount { get; set; }
    public DateTime? LastAnalyticsCalculatedAt { get; set; }
}

/// <summary>
/// Аналитика по направлению занятий.
/// </summary>
public class DirectionAnalyticsItem
{
    public string DirectionName { get; set; } = "";
    public int ActiveStudentsCount { get; set; }
    public int HighRiskCount { get; set; }
    public decimal AverageChurnProbability { get; set; }
    public int ActiveLeadsCount { get; set; }
    public decimal AverageConversionProbability { get; set; }
    public decimal TotalDebt { get; set; }

    public string AverageChurnProbabilityText => AverageChurnProbability.ToString("P0");
    public string AverageConversionProbabilityText => AverageConversionProbability.ToString("P0");
}
