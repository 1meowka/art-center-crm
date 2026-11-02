#nullable enable
using System;

namespace VKR.Models;

public sealed class TeacherScheduleRow
{
    public int IdTeacher { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string WeeklyLoadText { get; set; } = "0 ч/нед";
    public string MondayText { get; set; } = "—";
    public string TuesdayText { get; set; } = "—";
    public string WednesdayText { get; set; } = "—";
    public string ThursdayText { get; set; } = "—";
    public string FridayText { get; set; } = "—";
    public string SaturdayText { get; set; } = "—";
    public string SundayText { get; set; } = "—";

    public string GetDayText(short dayOfWeek) => dayOfWeek switch
    {
        1 => MondayText,
        2 => TuesdayText,
        3 => WednesdayText,
        4 => ThursdayText,
        5 => FridayText,
        6 => SaturdayText,
        7 => SundayText,
        _ => "—"
    };

    public void SetDayText(short dayOfWeek, string text)
    {
        switch (dayOfWeek)
        {
            case 1: MondayText = text; break;
            case 2: TuesdayText = text; break;
            case 3: WednesdayText = text; break;
            case 4: ThursdayText = text; break;
            case 5: FridayText = text; break;
            case 6: SaturdayText = text; break;
            case 7: SundayText = text; break;
        }
    }
}

internal sealed class TeacherScheduleSlot
{
    public int IdTeacher { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public short DayOfWeek { get; set; }
    public TimeSpan TimeStart { get; set; }
    public TimeSpan TimeEnd { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public short GroupNumber { get; set; }
    public string ClassroomNumber { get; set; } = string.Empty;

    public string CellLine =>
        $"{TimeStart:hh\\:mm}–{TimeEnd:hh\\:mm}\n{DirectionName} · гр. {GroupNumber}\nкаб. {ClassroomNumber}";

    public double DurationHours => (TimeEnd - TimeStart).TotalHours;
}
