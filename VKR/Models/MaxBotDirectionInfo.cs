#nullable enable

namespace VKR.Models;

public sealed class MaxBotDirectionInfo
{
    public int IdDirection { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal LessonPrice { get; set; }
    public string? Description { get; set; }
}
