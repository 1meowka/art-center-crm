#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VKR.Models;

namespace VKR.Services;

public static class MaxBotKeyboardBuilder
{
    public const string MenuHelp = "menu:help";
    public const string MenuDirections = "menu:directions";
    public const string MenuSchedule = "menu:schedule";
    public const string MenuContacts = "menu:contacts";

    public static string BranchPayload(int idBranch) => $"branch:{idBranch}";
    public static string ContactsBranchPayload(int idBranch) => $"contacts:{idBranch}";

    public static object[] BranchSelectionRows() =>
        BranchRows(BranchPayload);

    public static object[] ContactsBranchSelectionRows() =>
        BranchRows(ContactsBranchPayload);

    private static object[] BranchRows(Func<int, string> payload)
    {
        var rows = StudyBranch.All
            .Select(b => new[] { Callback($"📍 {b.ButtonLabel}", payload(b.IdBranch)) })
            .ToList();
        rows.Add(new[] { Callback("🏠 Главное меню", MenuHelp) });
        return Inline(rows.ToArray());
    }

    public static object[] ScheduleAfterBranchRows() =>
        Inline(
            new[] { Callback("◀ Сменить филиал", MenuSchedule) },
            new[] { Callback("🏠 Главное меню", MenuHelp) });

    public static object[] ContactsAfterBranchRows() =>
        Inline(
            new[] { Callback("◀ Сменить филиал", MenuContacts) },
            new[] { Callback("🏠 Главное меню", MenuHelp) });

    public static string DirectionPayload(int idDirection) => $"dir:{idDirection}";

    public static object[] MainMenuRow() =>
        Inline(new[] { Callback("🏠 Главное меню", MenuHelp) });

    public static object[] BackAndMenuRows() =>
        Inline(
            new[] { Callback("◀ Назад", MenuDirections) },
            new[] { Callback("🏠 Главное меню", MenuHelp) });

    public static object[] DirectionListRows(IEnumerable<(int Id, string Name)> directions)
    {
        var rows = new List<object[]>();
        var current = new List<object>();

        foreach (var (id, name) in directions)
        {
            current.Add(Callback(TrimButtonText(name), DirectionPayload(id)));
            if (current.Count >= 2)
            {
                rows.Add(current.ToArray());
                current.Clear();
            }
        }

        if (current.Count > 0)
            rows.Add(current.ToArray());

        rows.Add(new[] { Callback("🏠 Главное меню", MenuHelp) });
        return Inline(rows.ToArray());
    }

    private static object[] Inline(params object[][] buttonRows) =>
        new object[]
        {
            new
            {
                type = "inline_keyboard",
                payload = new { buttons = buttonRows }
            }
        };

    private static object Callback(string text, string payload) =>
        new { type = "callback", text, payload, intent = "default" };

    private static string TrimButtonText(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 40 ? trimmed : trimmed[..37] + "…";
    }
}
