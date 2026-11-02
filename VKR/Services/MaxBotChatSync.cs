#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VKR.Models;

namespace VKR.Services;

/// <summary>
/// Синхронизация списка диалогов и сообщений без лишней перерисовки UI.
/// </summary>
public static class MaxBotChatSync
{
    public static bool SameConversationSnapshot(
        IReadOnlyList<MaxBotConversation> current,
        IReadOnlyList<MaxBotConversation> incoming)
    {
        if (current.Count != incoming.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = incoming[i];
            if (left.IdSubscriber != right.IdSubscriber
                || left.UnreadCount != right.UnreadCount
                || left.LastMessageAt != right.LastMessageAt
                || !string.Equals(left.LastMessagePreview, right.LastMessagePreview, StringComparison.Ordinal)
                || !string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static void UpdateConversationList(
        ObservableCollection<MaxBotConversation> target,
        IReadOnlyList<MaxBotConversation> incoming)
    {
        if (SameConversationSnapshot(target, incoming))
            return;

        if (SameOrderAndIds(target, incoming))
        {
            for (var i = 0; i < incoming.Count; i++)
                target[i].SyncFrom(incoming[i]);
            return;
        }

        var preserved = target.ToDictionary(c => c.IdSubscriber);
        target.Clear();
        foreach (var item in incoming)
        {
            if (preserved.TryGetValue(item.IdSubscriber, out var existing))
            {
                existing.SyncFrom(item);
                target.Add(existing);
            }
            else
            {
                target.Add(item);
            }
        }
    }

    public static bool NeedsMessageReload(
        int displayedCount,
        int displayedLastMessageId,
        IReadOnlyList<MaxBotMessage> messages)
    {
        if (messages.Count == 0)
            return displayedCount > 0;

        if (messages.Count != displayedCount)
            return true;

        return messages[^1].IdMessage != displayedLastMessageId;
    }

    public static bool CanAppendMessages(
        int displayedCount,
        int displayedLastMessageId,
        IReadOnlyList<MaxBotMessage> messages)
    {
        if (displayedCount <= 0 || displayedCount >= messages.Count)
            return false;

        return messages[displayedCount - 1].IdMessage == displayedLastMessageId;
    }

    private static bool SameOrderAndIds(
        IReadOnlyList<MaxBotConversation> current,
        IReadOnlyList<MaxBotConversation> incoming)
    {
        if (current.Count != incoming.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (current[i].IdSubscriber != incoming[i].IdSubscriber)
                return false;
        }

        return true;
    }
}
