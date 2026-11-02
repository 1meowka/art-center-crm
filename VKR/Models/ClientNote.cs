#nullable enable
using System;

namespace VKR.Models;

public sealed class ClientNote
{
    public int IdNote { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string NoteText { get; set; } = string.Empty;
    public int? IdCreatedBy { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string CreatedAtText => CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string Preview => NoteText.Length <= 120 ? NoteText : NoteText[..117] + "…";
}

public static class ClientNoteEntityType
{
    public const string Student = "student";
    public const string Lead = "lead";
    public const string Representative = "representative";
}
