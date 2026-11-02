#nullable enable
using System;

namespace VKR.Models;

public sealed class CalendarEventListItem
{
    public int IdEvent { get; set; }
    public string EventType { get; set; } = "master_class";
    public string Title { get; set; } = string.Empty;
    public int? IdDirection { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public int? IdTeacher { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public int? IdClassroom { get; set; }
    public string ClassroomNumber { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public TimeSpan? TimeEnd { get; set; }
    public short? MaxParticipants { get; set; }
    public bool IsActive { get; set; } = true;

    public string EventTypeText => CalendarEventTypeInfo.GetLabel(EventType);
    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string TimeRangeText => TimeEnd.HasValue
        ? $"{ScheduledAt:HH:mm} – {TimeEnd.Value:hh\\:mm}"
        : ScheduledAt.ToString("HH:mm");
    public string ClassroomText => string.IsNullOrWhiteSpace(ClassroomNumber) ? "—" : $"каб. {ClassroomNumber}";
    public string CapacityText => MaxParticipants.HasValue && MaxParticipants > 0
        ? $"макс. {MaxParticipants}"
        : "без лимита";
    public string StatusText => IsActive ? "активно" : "архив";
    public string Subtitle => $"{EventTypeText} · {DirectionName} · {TeacherName} · {ClassroomText}";
}

public sealed class CalendarEventEditModel
{
    public int IdEvent { get; set; }
    public string EventType { get; set; } = "master_class";
    public string Title { get; set; } = string.Empty;
    public int? IdDirection { get; set; }
    public int? IdTeacher { get; set; }
    public int? IdClassroom { get; set; }
    public DateTime ScheduledAt { get; set; } = DateTime.Today.AddHours(15);
    public TimeSpan? TimeEnd { get; set; } = new(16, 30, 0);
    public short? MaxParticipants { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class CalendarEventTypeInfo
{
    public static IReadOnlyList<CodeOption> Options { get; } = new[]
    {
        new CodeOption("master_class", "Мастер-класс"),
        new CodeOption("trial_open", "Открытое пробное")
    };

    public static string GetLabel(string? code) => code switch
    {
        "master_class" => "Мастер-класс",
        "trial_open" => "Открытое пробное",
        _ => "Событие"
    };
}

public static class LessonTypeInfo
{
    public static IReadOnlyList<CodeOption> Options { get; } = new[]
    {
        new CodeOption("regular", "Обычное занятие"),
        new CodeOption("trial_open", "Слот для пробных"),
        new CodeOption("master_class", "Мастер-класс (еженед.)")
    };

    public static string GetLabel(string? code) => code switch
    {
        "regular" => "Обычное",
        "trial_open" => "Пробное",
        "master_class" => "Мастер-класс",
        _ => "Занятие"
    };

    public static CodeOption? FindByCode(string? code)
    {
        foreach (var opt in Options)
            if (string.Equals(opt.Code, code, StringComparison.OrdinalIgnoreCase))
                return opt;
        return Options[0];
    }
}

public sealed class CodeOption
{
    public CodeOption(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }
    public string Label { get; }
    public override string ToString() => Label;
}
