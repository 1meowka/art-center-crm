#nullable enable
using System;

namespace VKR.Models;

public sealed class SatisfactionLevelRef
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsPositive { get; set; }
    public override string ToString() => Name;
}

public sealed class DissatisfactionReasonRef
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}

/// <summary>Задача администратора: узнать, как ребёнку занимается (ежемесячно от даты договора).</summary>
public sealed class SatisfactionDueItem
{
    public int IdStudent { get; set; }
    public int IdContract { get; set; }
    public int IdDirection { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public DateTime ContractStart { get; set; }
    public DateTime DueDate { get; set; }
    public int PeriodNumber { get; set; }
    public int DaysOverdue { get; set; }

    public string TaskKey => NotificationTaskKeys.Satisfaction(IdContract, PeriodNumber);
    public string DueDateText => DueDate.ToString("dd.MM.yyyy");
    public string ContractStartText => ContractStart.ToString("dd.MM.yyyy");
    public string PeriodText => $"Месяц {PeriodNumber}";
    public string OverdueText => DaysOverdue <= 0 ? "сегодня" : $"+{DaysOverdue} дн.";
}

public sealed class SatisfactionSurveyInput
{
    public int IdStudent { get; set; }
    public int IdContract { get; set; }
    public int IdDirection { get; set; }
    public DateTime DueDate { get; set; }
    public int PeriodNumber { get; set; }
    public int IdSatisfactionLevel { get; set; }
    public int? IdDissatisfactionReason { get; set; }
    public int? SuggestedDirectionId { get; set; }
    public string? Comment { get; set; }
    public int? IdCompletedBy { get; set; }
}
