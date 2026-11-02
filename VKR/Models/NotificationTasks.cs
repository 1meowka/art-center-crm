#nullable enable
using System;

namespace VKR.Models;

public static class NotificationTaskKeys
{
    public static string Satisfaction(int idContract, int periodNumber)
        => $"sat:{idContract}:{periodNumber}";

    public static string ContractRenewal(int idContract, DateTime contractEndDate)
        => $"renew:{idContract}:{contractEndDate:yyyy-MM-dd}";
}

/// <summary>Задача: за 7 дней до окончания договора узнать, продлят ли.</summary>
public sealed class ContractRenewalDueItem
{
    public int IdStudent { get; set; }
    public int IdContract { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public DateTime ContractEndDate { get; set; }
    public DateTime RemindFromDate { get; set; }
    public int DaysUntilEnd { get; set; }

    public string TaskKey => NotificationTaskKeys.ContractRenewal(IdContract, ContractEndDate);
    public string EndDateText => ContractEndDate.ToString("dd.MM.yyyy");
    public string RemindFromText => RemindFromDate.ToString("dd.MM.yyyy");
    public string DaysLeftText => DaysUntilEnd == 0 ? "сегодня" : DaysUntilEnd == 1 ? "1 день" : $"{DaysUntilEnd} дн.";
}

public sealed class ContractRenewalInput
{
    public int IdContract { get; set; }
    public int IdStudent { get; set; }
    public DateTime ContractEndDate { get; set; }
    public bool? WillRenew { get; set; }
    public string? Comment { get; set; }
    public int? IdCompletedBy { get; set; }
}
