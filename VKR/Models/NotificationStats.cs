#nullable enable
using System;

namespace VKR.Models;

public enum NotificationStatsSection
{
    Satisfaction,
    Renewal,
}

public sealed class SatisfactionHistoryItem
{
    public string StudentName { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public int PeriodNumber { get; set; }
    public string LevelName { get; set; } = string.Empty;
    public bool IsPositive { get; set; }
    public string ReasonName { get; set; } = string.Empty;
    public string SuggestedDirectionName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public string CompletedByName { get; set; } = string.Empty;

    public string PeriodText => $"Месяц {PeriodNumber}";
    public string CompletedAtText => CompletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string ReasonText => string.IsNullOrWhiteSpace(ReasonName) ? "—" : ReasonName;
    public string SuggestedDirectionText => string.IsNullOrWhiteSpace(SuggestedDirectionName) ? "—" : SuggestedDirectionName;
    public string CommentText => string.IsNullOrWhiteSpace(Comment) ? "—" : Comment;
    public string CompletedByText => string.IsNullOrWhiteSpace(CompletedByName) ? "—" : CompletedByName;
}

public sealed class SatisfactionStatsSummary
{
    public int Total { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int NeutralCount { get; set; }

    public string PositivePercentText => FormatShare(PositiveCount);
    public string NegativePercentText => FormatShare(NegativeCount);
    public string NeutralPercentText => FormatShare(NeutralCount);

    private string FormatShare(int count) =>
        Total <= 0 ? "0%" : $"{count * 100.0 / Total:0.#}%";
}

public sealed class ContractRenewalHistoryItem
{
    public string StudentName { get; set; } = string.Empty;
    public string ContractNumber { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public DateTime ContractEndDate { get; set; }
    public bool? WillRenew { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public string CompletedByName { get; set; } = string.Empty;

    public string EndDateText => ContractEndDate.ToString("dd.MM.yyyy");
    public string CompletedAtText => CompletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string WillRenewText => WillRenew switch
    {
        true => "Да, продлит",
        false => "Нет, не продлит",
        _ => "Не указано",
    };
    public string CommentText => string.IsNullOrWhiteSpace(Comment) ? "—" : Comment;
    public string CompletedByText => string.IsNullOrWhiteSpace(CompletedByName) ? "—" : CompletedByName;
}

public sealed class ContractRenewalStatsSummary
{
    public int Total { get; set; }
    public int YesCount { get; set; }
    public int NoCount { get; set; }
    public int UnknownCount { get; set; }

    public string RenewalRateText
    {
        get
        {
            var decided = YesCount + NoCount;
            return decided <= 0 ? "—" : $"{YesCount * 100.0 / decided:0.#}%";
        }
    }

    public string YesShareText => FormatShare(YesCount);
    public string NoShareText => FormatShare(NoCount);
    public string UnknownShareText => FormatShare(UnknownCount);

    private string FormatShare(int count) =>
        Total <= 0 ? "0%" : $"{count * 100.0 / Total:0.#}%";
}
