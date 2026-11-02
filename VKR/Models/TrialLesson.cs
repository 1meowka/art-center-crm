#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VKR.Models;

public sealed class TrialLessonSlotItem
{
    public int IdSchedule { get; set; }
    public int IdEvent { get; set; }
    public int IdGroup { get; set; }
    public int IdDirection { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public short GroupNumber { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string LessonType { get; set; } = "regular";
    public string TeacherName { get; set; } = string.Empty;
    public string ClassroomNumber { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public TimeSpan TimeEnd { get; set; }

    public int Capacity { get; set; }
    public int EnrolledCount { get; set; }
    public int TrialBookedCount { get; set; }
    public int OccupiedSeats => EnrolledCount + TrialBookedCount;
    public int FreeSeats { get; set; }
    public bool IsFull { get; set; }

    public bool IsCalendarEvent => IdEvent > 0;
    public string GroupTitle => IsCalendarEvent ? EventTitle : $"{DirectionName} — гр. {GroupNumber}";
    public string SlotTypeText => IsCalendarEvent
        ? CalendarEventTypeInfo.GetLabel(EventType)
        : LessonTypeInfo.GetLabel(LessonType);
    public string TimeRangeText => $"{ScheduledAt:HH:mm} – {TimeEnd:hh\\:mm}";
    public string DateText => ScheduledAt.ToString("dd.MM.yyyy");
    public string WeekDayText => ScheduledAt.ToString("dddd");
    public string ClassroomText => string.IsNullOrWhiteSpace(ClassroomNumber) ? "—" : $"каб. {ClassroomNumber}";
    public string OccupancyText => IsFull ? "мест нет" : $"свободно {FreeSeats} из {Capacity}";
    public string SummaryText => $"{DateText}, {TimeRangeText}, {GroupTitle}, {TeacherName}";
}

public sealed class TrialAvailableDateItem
{
    public DateTime Date { get; set; }
    public int SlotsCount { get; set; }

    public string Label => Date.ToString("dd.MM.yyyy");
    public string WeekDayText => Date.ToString("dddd");
    public string Subtitle => SlotsCount switch
    {
        0 => "нет слотов",
        1 => "1 слот",
        2 or 3 or 4 => $"{SlotsCount} слота",
        _ => $"{SlotsCount} слотов"
    };

    public override string ToString() => $"{Label} ({WeekDayText}) — {Subtitle}";
}

public sealed class TrialContractCandidateItem
{
    public int IdTrial { get; set; }
    public int IdLead { get; set; }
    public int? IdDirection { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? Result { get; set; }
    public string ChildFullName { get; set; } = string.Empty;
    public string ClientFullName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public string LeadStatus { get; set; } = string.Empty;
    public string? FollowupFeedback { get; set; }

    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? "ожидает итог" : Result!;
    public string ReadinessText => Result switch
    {
        "заключил_договор" => "готов к договору",
        "понравилось" => "понравилось",
        _ when !string.IsNullOrWhiteSpace(FollowupFeedback) => "связались — ждёт договор",
        _ => "пробное состоялось"
    };
}

public sealed class TrialLessonListItem
{
    public int IdTrial { get; set; }
    public int IdLead { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? Result { get; set; }
    public string ChildFullName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public string LeadStatus { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public string ClassroomText { get; set; } = "—";
    public TimeSpan? TimeEnd { get; set; }

    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string DateText => ScheduledAt.ToString("dd.MM.yyyy");
    public string TimeRangeText => TimeEnd.HasValue
        ? $"{ScheduledAt:HH:mm} – {TimeEnd.Value:hh\\:mm}"
        : ScheduledAt.ToString("HH:mm");
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? "запланировано" : Result!;
    public string CallText => $"{ChildFullName} — {ContactPhone}";
}

public sealed class TrialSlotBookingSummary
{
    public DateTime ScheduledAt { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string ClassroomText { get; set; } = "—";
    public int BookedCount { get; set; }
    public string AttendeesText { get; set; } = string.Empty;
    public TimeSpan? TimeEnd { get; set; }

    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string TimeRangeText => TimeEnd.HasValue
        ? $"{ScheduledAt:HH:mm} – {TimeEnd.Value:hh\\:mm}"
        : ScheduledAt.ToString("HH:mm");
    public string CountText => BookedCount switch
    {
        1 => "1 запись",
        2 or 3 or 4 => $"{BookedCount} записи",
        _ => $"{BookedCount} записей"
    };
}

public sealed class TrialJournalDayColumn : INotifyPropertyChanged
{
    private DateTime _date;

    public short DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string ShortDayName { get; set; } = string.Empty;
    public ObservableCollection<TrialJournalSlotCard> Slots { get; } = new();

    public DateTime Date
    {
        get => _date;
        set
        {
            if (_date == value) return;
            _date = value;
            OnChanged();
            OnChanged(nameof(DateText));
            OnChanged(nameof(DateLongText));
            OnChanged(nameof(IsToday));
        }
    }

    public string DateText => Date == default ? "—" : Date.ToString("dd.MM");
    public string DateLongText => Date == default ? "—" : Date.ToString("dd.MM.yyyy");
    public bool IsToday => Date != default && Date.Date == DateTime.Today;
    public int SlotsCount => Slots.Count;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TrialJournalSlotCard : INotifyPropertyChanged
{
    private bool _isSelected;

    public DateTime ScheduledAt { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string ClassroomText { get; set; } = "—";
    public TimeSpan? TimeEnd { get; set; }
    public int BookedCount { get; set; }
    public string AttendeesText { get; set; } = string.Empty;
    public List<TrialLessonListItem> Bookings { get; set; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnChanged();
        }
    }

    public string TimeRangeText => TimeEnd.HasValue
        ? $"{ScheduledAt:HH:mm} – {TimeEnd.Value:hh\\:mm}"
        : ScheduledAt.ToString("HH:mm");
    public string DayWithDateText => ScheduledAt.ToString("dddd, dd.MM.yyyy");
    public bool IsToday => ScheduledAt.Date == DateTime.Today;
    public string CountText => BookedCount switch
    {
        1 => "1 пробное",
        2 or 3 or 4 => $"{BookedCount} пробных",
        _ => $"{BookedCount} пробных"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TrialDirectionOption
{
    public int IdDirection { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public override string ToString() => Name;
}

public sealed class TrialLessonBookModel
{
    public int IdLead { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int IdSchedule { get; set; }
    public int IdEvent { get; set; }
    public int IdDirection { get; set; }
    public int IdGroup { get; set; }
    public int IdTeacher { get; set; }
}

public sealed class TrialLeadHistoryItem
{
    public int IdTrial { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? Result { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public string TeacherName { get; set; } = "—";
    public string GroupDisplay { get; set; } = "—";
    public bool HasFeedback { get; set; }
    public string? FeedbackText { get; set; }

    public string ScheduledText => ScheduledAt.ToString("dd.MM.yyyy HH:mm");
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? "запланировано" : Result!;
    public string FeedbackDisplay => HasFeedback
        ? (string.IsNullOrWhiteSpace(FeedbackText) ? "есть" : FeedbackText!)
        : "нет";
}

public sealed class TrialLeadStats
{
    public int TotalTrials { get; set; }
    public int WithFeedback { get; set; }
    public bool ConvertedToContract { get; set; }

    public string SummaryText => ConvertedToContract
        ? $"Пробных: {TotalTrials}, обратная связь: {WithFeedback} — договор заключён ✓"
        : $"Пробных: {TotalTrials}, обратная связь: {WithFeedback}, договор: нет";
}
