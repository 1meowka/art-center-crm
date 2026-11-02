#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VKR.Models;

public sealed class StudyGroupListItem
{
    public int IdGroup { get; set; }
    public short GroupNumber { get; set; }
    public int IdDirection { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public int IdTeacher { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int StudentsCount { get; set; }
    public int LessonsPerWeek { get; set; }
    public short? MaxStudents { get; set; }
    public string? AgeBandLabel { get; set; }

    public string Title => $"{DirectionName} — группа {GroupNumber}";
    public string ComboDisplay => $"{DirectionName} — гр.{GroupNumber} · {TeacherName}";
    public string CapacityText => MaxStudents.HasValue && MaxStudents > 0
        ? $"макс. {MaxStudents}"
        : "без лимита";
    public string StatusText => IsActive ? "активна" : "архив";
    public string StudentsText => StudentsCount == 1 ? "1 ученик" : StudentsCount switch
    {
        0 => "нет учеников",
        2 or 3 or 4 => $"{StudentsCount} ученика",
        _ => $"{StudentsCount} учеников"
    };

    public bool ShowHistoryButton { get; set; }
}

public sealed class StudyGroupEditModel
{
    public int IdGroup { get; set; }
    public short GroupNumber { get; set; } = 1;
    public int? IdDirection { get; set; }
    public int? IdTeacher { get; set; }
    public bool IsActive { get; set; } = true;
    public short? AgeMin { get; set; }
    public short? AgeMax { get; set; }
    public string? AgeBandLabel { get; set; }
    public short? MaxStudents { get; set; }
}

public sealed class GroupMemberItem
{
    public int IdMembership { get; set; }
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public DateTime EnrollmentDate { get; set; }
    public DateTime? ExpulsionDate { get; set; }
    public string EnrollmentText => EnrollmentDate.ToString("dd.MM.yyyy");
}

public sealed class ScheduleItem
{
    public int IdSchedule { get; set; }
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public int IdClassroom { get; set; }
    public string ClassroomNumber { get; set; } = string.Empty;
    public short IdBranch { get; set; } = 1;
    public short DayOfWeek { get; set; }
    public TimeSpan TimeStart { get; set; }
    public TimeSpan TimeEnd { get; set; }
    public string LessonType { get; set; } = "regular";
    public bool IsActive { get; set; } = true;

    public string LessonTypeText => LessonTypeInfo.GetLabel(LessonType);

    public string DayName => DayOfWeek switch
    {
        1 => "Понедельник",
        2 => "Вторник",
        3 => "Среда",
        4 => "Четверг",
        5 => "Пятница",
        6 => "Суббота",
        7 => "Воскресенье",
        _ => "—"
    };
    public string ShortDayName => DayOfWeek switch
    {
        1 => "ПН",
        2 => "ВТ",
        3 => "СР",
        4 => "ЧТ",
        5 => "ПТ",
        6 => "СБ",
        7 => "ВС",
        _ => "—"
    };
    public string TimeRangeText => $"{TimeStart:hh\\:mm} – {TimeEnd:hh\\:mm}";
    public string ClassroomText => string.IsNullOrWhiteSpace(ClassroomNumber) ? "—" : $"каб. {ClassroomNumber}";

    public DateTime? LessonDate { get; set; }
    public string DayWithDateText => LessonDate.HasValue
        ? $"{DayName}, {LessonDate:dd.MM.yyyy}"
        : DayName;
    public bool IsToday => LessonDate?.Date == DateTime.Today;
}

public sealed class ScheduleDayColumn : INotifyPropertyChanged
{
    private DateTime _date;

    public short DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string ShortDayName { get; set; } = string.Empty;
    public ObservableCollection<ScheduleItem> Lessons { get; } = new();
    public bool HasLessons => Lessons.Count > 0;

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
            OnChanged(nameof(DayHeaderText));
            OnChanged(nameof(IsToday));
        }
    }

    public string DateText => Date == default ? "—" : Date.ToString("dd.MM");
    public string DateLongText => Date == default ? "—" : Date.ToString("dd.MM.yyyy");
    public string DayHeaderText => Date == default ? DayName : $"{DayName} · {DateLongText}";
    public bool IsToday => Date != default && Date.Date == DateTime.Today;

    private int _reportStudentsTotal;
    private int _reportTrialsTotal;

    public int ReportStudentsTotal
    {
        get => _reportStudentsTotal;
        set
        {
            if (_reportStudentsTotal == value) return;
            _reportStudentsTotal = value;
            OnChanged();
            OnChanged(nameof(DayReportSummaryText));
            OnChanged(nameof(HasDayReport));
        }
    }

    public int ReportTrialsTotal
    {
        get => _reportTrialsTotal;
        set
        {
            if (_reportTrialsTotal == value) return;
            _reportTrialsTotal = value;
            OnChanged();
            OnChanged(nameof(DayReportSummaryText));
            OnChanged(nameof(HasDayReport));
        }
    }

    public bool HasDayReport => ReportStudentsTotal > 0 || ReportTrialsTotal > 0;
    public string DayReportSummaryText => HasDayReport
        ? $"{ReportStudentsTotal} чел. · {ReportTrialsTotal} проб."
        : "отчёт не заполнен";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ScheduleEditModel
{
    public int IdSchedule { get; set; }
    public int IdGroup { get; set; }
    public int IdClassroom { get; set; }
    public short DayOfWeek { get; set; } = 1;
    public TimeSpan TimeStart { get; set; } = new(15, 0, 0);
    public TimeSpan TimeEnd { get; set; } = new(16, 30, 0);
    public bool IsActive { get; set; } = true;
    public string LessonType { get; set; } = "regular";
}
