#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VKR.Models;

public sealed class TeacherListItem
{
    public const decimal HoursPerLesson = 2m;

    public int IdTeacher { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime HireDate { get; set; }
    public decimal? BaseLessonRate { get; set; }
    public bool IsActive { get; set; } = true;
    public int ActiveGroupsCount { get; set; }
    public int LessonsPerWeek { get; set; }
    public string DirectionsText { get; set; } = "—";

    public decimal WeeklyHours => LessonsPerWeek * HoursPerLesson;
    public string FullName => Student.FormatFio(LastName, FirstName, MiddleName);
    public string StatusText => IsActive ? "работает" : "архив";
    public string RateText => BaseLessonRate.HasValue
        ? $"{BaseLessonRate.Value:N0} ₽/занятие"
        : "ставка не задана";
    public string HireDateText => HireDate.ToString("dd.MM.yyyy");
    public string WeeklyHoursText => $"{WeeklyHours:0.#} ч";
}

public sealed class TeacherEditModel
{
    public int IdTeacher { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime HireDate { get; set; } = DateTime.Today;
    public decimal? BaseLessonRate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TeacherLessonItem
{
    public int IdSchedule { get; set; }
    public int IdGroup { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public short DayOfWeek { get; set; }
    public TimeSpan TimeStart { get; set; }
    public TimeSpan TimeEnd { get; set; }
    public string ClassroomNumber { get; set; } = string.Empty;
    public int StudentsCount { get; set; }

    public decimal LessonHours => TeacherListItem.HoursPerLesson;
    public string LessonHoursText => $"{LessonHours:0.#} ч";
    public string DayName => DayOfWeek switch
    {
        1 => "Понедельник", 2 => "Вторник", 3 => "Среда", 4 => "Четверг",
        5 => "Пятница", 6 => "Суббота", 7 => "Воскресенье", _ => "—"
    };
    public string ShortDayName => DayOfWeek switch
    {
        1 => "ПН", 2 => "ВТ", 3 => "СР", 4 => "ЧТ",
        5 => "ПТ", 6 => "СБ", 7 => "ВС", _ => "—"
    };
    public string TimeRangeText => $"{TimeStart:hh\\:mm} – {TimeEnd:hh\\:mm}";
    public string TimeStartShortText => $"{TimeStart:hh\\:mm}";
    public string ClassroomText => string.IsNullOrWhiteSpace(ClassroomNumber) ? "—" : $"каб. {ClassroomNumber}";
    public string LessonMetaText => $"{ClassroomText} · {StudentsText} · {LessonHoursText}";
    public string StudentsText => StudentsCount switch
    {
        0 => "нет учеников",
        1 => "1 ученик",
        2 or 3 or 4 => $"{StudentsCount} ученика",
        _ => $"{StudentsCount} учеников"
    };
}

public sealed class AttendanceRow : INotifyPropertyChanged
{
    private string _status = "не отмечено";
    private string? _comment;

    public int? IdAttendance { get; set; }
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnChanged();
        }
    }

    public string? Comment
    {
        get => _comment;
        set
        {
            if (_comment == value) return;
            _comment = value;
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TeacherSalaryReport
{
    public string TeacherName { get; set; } = string.Empty;
    public decimal BaseLessonRate { get; set; }
    public int LessonsHeld { get; set; }
    public decimal LessonHours { get; set; } = TeacherListItem.HoursPerLesson;
    public decimal TotalHours { get; set; }
    public int VisitsCount { get; set; }
    public decimal Total { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public string PeriodText => $"{FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy}";
    public string TotalText => $"{Total:N0} ₽";
    public string RateText => $"{BaseLessonRate:N0} ₽";
    public string TotalHoursText => $"{TotalHours:0.#} ч";
    public string LessonHoursText => $"{LessonHours:0.#} ч";
}

public sealed class TeacherAttendanceStatsReport
{
    public string TeacherName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int GroupsCount { get; set; }
    public int StudentsCount { get; set; }
    public int PlannedLessons { get; set; }
    public int LessonsHeld { get; set; }
    public decimal TotalHours { get; set; }
    public int Visits { get; set; }
    public int Absences { get; set; }
    public int Excused { get; set; }
    public int Late { get; set; }
    public IReadOnlyList<TeacherGroupAttendanceStatsRow> Rows { get; set; } = Array.Empty<TeacherGroupAttendanceStatsRow>();

    public int MarkedTotal => Visits + Absences + Excused;
    public string PeriodText => $"{FromDate:dd.MM.yyyy} – {ToDate:dd.MM.yyyy}";
    public string TotalHoursText => $"{TotalHours:0.#} ч";
    public string OverallPercentText => MarkedTotal == 0 ? "—" : $"{Visits * 100.0 / MarkedTotal:0.#}%";
}

public sealed class TeacherGroupAttendanceStatsRow
{
    public string GroupTitle { get; set; } = string.Empty;
    public int StudentsCount { get; set; }
    public int PlannedLessons { get; set; }
    public int LessonsHeld { get; set; }
    public decimal TotalHours { get; set; }
    public int Visits { get; set; }
    public int Absences { get; set; }
    public int Excused { get; set; }
    public int Late { get; set; }

    public int MarkedTotal => Visits + Absences + Excused;
    public string TotalHoursText => $"{TotalHours:0.#} ч";
    public string PercentText => MarkedTotal == 0 ? "—" : $"{Visits * 100.0 / MarkedTotal:0.#}%";
}
