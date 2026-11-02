#nullable enable
using System;
using System.Net.Http;

namespace VKR.Utilities;

public static class MaxBotErrors
{
    public static string ForSend(Exception ex)
    {
        if (ex is InvalidOperationException ioe && !string.IsNullOrWhiteSpace(ioe.Message))
            return ioe.Message;

        if (ex is HttpRequestException)
            return "Нет связи с сервером MAX (platform-api2.max.ru). Проверьте интернет. "
                   + "На новом ПК может понадобиться установить сертификаты Минцифры: https://www.gosuslugi.ru/crt";

        if (ex is TaskCanceledException)
            return "Сервер MAX не ответил вовремя. Проверьте интернет и повторите отправку.";

        return UserFacingErrors.GetMessage(ex, "Не удалось отправить сообщение клиенту в MAX.");
    }

    public static string ExplainReplyDisabled(bool hasConversation, bool botConfigured)
    {
        if (!hasConversation)
            return "Выберите диалог слева, чтобы ответить клиенту.";

        if (!botConfigured)
            return "Отправка недоступна: укажите токен бота в разделе «MAX-бот → Настройки бота».";

        return "Enter — отправить, Shift+Enter — новая строка. Ответ уйдёт клиенту от имени бота в MAX.";
    }

    public static string MapApiStatus(int statusCode, string? body)
    {
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $"\n\nОтвет MAX: {Trim(body)}";

        return statusCode switch
        {
            401 or 403 => "Токен бота MAX недействителен или отозван. Откройте «Настройки бота» и укажите новый токен с business.max.ru." + detail,
            404 => "Чат клиента не найден в MAX. Возможно, клиент удалил переписку с ботом или ещё не написал боту." + detail,
            429 => "Слишком много запросов к MAX. Подождите минуту и повторите отправку." + detail,
            >= 500 => "Сервер MAX временно недоступен. Попробуйте отправить сообщение позже." + detail,
            _ => $"MAX не принял сообщение (код {statusCode})." + detail
        };
    }

    private static string Trim(string text) =>
        text.Length > 200 ? text[..200] + "…" : text;
}
