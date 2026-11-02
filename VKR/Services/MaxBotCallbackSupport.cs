#nullable enable
using System.Text.Json;
using VKR.Models;

namespace VKR.Services;

public static class MaxBotCallbackSupport
{
    public static string? TryGetPayload(JsonElement callback) =>
        callback.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.String
            ? payloadEl.GetString()
            : null;

    public static long? TryResolveChatId(JsonElement update, JsonElement callback)
    {
        if (update.TryGetProperty("message", out var message) && TryGetChatId(message) is long chatId)
            return chatId;

        if (callback.TryGetProperty("message", out message) && TryGetChatId(message) is long callbackMessageChatId)
            return callbackMessageChatId;

        if (callback.TryGetProperty("user", out var user)
            && user.TryGetProperty("user_id", out var userIdEl)
            && userIdEl.TryGetInt64(out var userId))
            return userId;

        return TryGetChatId(update);
    }

    private static long? TryGetChatId(JsonElement element)
    {
        if (element.TryGetProperty("chat_id", out var chatEl) && chatEl.TryGetInt64(out var chatId))
            return chatId;

        if (element.TryGetProperty("recipient", out var recipient)
            && recipient.TryGetProperty("chat_id", out chatEl)
            && chatEl.TryGetInt64(out chatId))
            return chatId;

        if (element.TryGetProperty("recipient", out recipient)
            && recipient.TryGetProperty("user_id", out chatEl)
            && chatEl.TryGetInt64(out chatId))
            return chatId;

        return null;
    }
}
