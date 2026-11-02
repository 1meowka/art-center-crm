#nullable enable

using VKR.Utilities;

namespace VKR.Models;

public sealed class CoursePackageLookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? IdDirection { get; set; }
    public string? DirectionName { get; set; }
    public decimal DurationMonths { get; set; } = 9;
    public int? LessonsCount { get; set; }
    public short TimesPerWeek { get; set; } = 1;

    public string DurationLabel => ContractDurationHelper.FormatDuration(DurationMonths);
    public string TimesLabel => TimesPerWeek switch
    {
        1 => "1 раз/нед.",
        2 => "2 раза/нед.",
        3 => "3 раза/нед.",
        4 => "4 раза/нед.",
        _ => $"{TimesPerWeek} раз/нед."
    };
    public string VariantLabel
    {
        get
        {
            var parts = new System.Collections.Generic.List<string> { TimesLabel };
            if (LessonsCount.HasValue)
                parts.Add($"{LessonsCount} зан.");
            parts.Add(DurationLabel);
            return string.Join(" · ", parts);
        }
    }

    public string DisplayName
    {
        get
        {
            var parts = new System.Collections.Generic.List<string> { Name, DurationLabel, TimesLabel };
            if (LessonsCount.HasValue)
                parts.Add($"{LessonsCount} зан.");
            if (!string.IsNullOrWhiteSpace(DirectionName))
                parts.Add(DirectionName);
            return string.Join(" · ", parts);
        }
    }
}
