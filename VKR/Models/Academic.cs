#nullable enable
using System;

namespace VKR.Models;

public sealed class GroupTeacherHistoryItem
{
    public int IdHistory { get; set; }
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string? TeacherOldName { get; set; }
    public string TeacherNewName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
    public string? Comment { get; set; }

    public string ChangedAtText => ChangedAt.ToString("dd.MM.yyyy HH:mm");
}

public sealed class StudentGroupHistoryItem
{
    public int IdHistory { get; set; }
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string? GroupFromTitle { get; set; }
    public string GroupToTitle { get; set; } = string.Empty;
    public string? TeacherFromName { get; set; }
    public string TeacherToName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
    public string? Reason { get; set; }

    public string TransferDateText => TransferDate.ToString("dd.MM.yyyy");
    public string ChangedAtText => ChangedAt.ToString("dd.MM.yyyy HH:mm");
}

public sealed class GroupHistoryTimelineItem
{
    public DateTime EventAt { get; set; }
    public string EventCategory { get; set; } = string.Empty;
    public int? IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? TeacherName { get; set; }
    public string? ChangedByName { get; set; }

    public string EventAtText => EventAt.ToString("dd.MM.yyyy HH:mm");
    public string EventTypeText => EventCategory switch
    {
        "teacher_change" => "Смена преподавателя",
        "enroll" => "Зачисление",
        "transfer" => "Перевод",
        "expel" => "Отчисление",
        _ => EventCategory
    };
}

public sealed class GroupDailyReportRow
{
    public int IdReport { get; set; }
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public short StudentsPresent { get; set; }
    public short TrialPresent { get; set; }
    public string? Comment { get; set; }

    public string ReportDateText => ReportDate.ToString("dd.MM.yyyy");
    public string SummaryText => TrialPresent > 0
        ? $"{StudentsPresent} чел. · {TrialPresent} проб."
        : $"{StudentsPresent} чел.";
}

public sealed class GroupDailyReportEditRow
{
    public int IdReport { get; set; }
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public short StudentsPresent { get; set; }
    public short TrialPresent { get; set; }
    public string? Comment { get; set; }
}

public sealed class GroupDailyReportDaySummary
{
    public int GroupsReported { get; set; }
    public int StudentsTotal { get; set; }
    public int TrialsTotal { get; set; }

    public string SummaryText => GroupsReported == 0
        ? "Отчёт за день не заполнен"
        : $"За день: {StudentsTotal} чел., {TrialsTotal} проб. ({GroupsReported} групп)";
}

public sealed class TrialFollowupDueItem
{
    public int IdFollowup { get; set; }
    public int IdTrial { get; set; }
    public int? IdLead { get; set; }
    public DateTime DueAt { get; set; }
    public string ChildFullName { get; set; } = string.Empty;
    public string ClientFullName { get; set; } = string.Empty;
    public string ParentPhone { get; set; } = string.Empty;
    public string? ChildPhone { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string TaskStatus { get; set; } = "позвонить_после_пробного";
    public bool OnControl { get; set; }
    public DateTime? NextReminderAt { get; set; }

    public string DueAtText => DueAt.ToString("dd.MM.yyyy HH:mm");
    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string PrimaryPhoneText => string.IsNullOrWhiteSpace(ParentPhone) ? "—" : ParentPhone;
    public string FallbackPhoneText => string.IsNullOrWhiteSpace(ChildPhone) ? "—" : ChildPhone;
    public string TaskStatusText => TaskStatus switch
    {
        "позвонить_после_пробного" => "Позвонить после пробного",
        "на_контроле" => "На контроле",
        "не_дозвонились" => "Не дозвонились",
        _ => TaskStatus
    };
    public string TaskKey => $"trial_followup:{IdTrial}";
}

public sealed class TrialFollowupCompleteInput
{
    public int IdFollowup { get; set; }
    public string? FeedbackCode { get; set; }
    public string? FeedbackText { get; set; }
    public int? IdCompletedBy { get; set; }
    public bool OnControl { get; set; }
    public bool RescheduleNextDay { get; set; }
    public bool MarkCompleted { get; set; } = true;
}
