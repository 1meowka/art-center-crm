#nullable enable

namespace VKR.Models;

/// <summary>Контакты и тексты бота для конкретного филиала.</summary>
public sealed class MaxBotBranchInfo
{
    public int IdBranch { get; set; }
    public string ShortName { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string? ContactsNote { get; set; }
}
