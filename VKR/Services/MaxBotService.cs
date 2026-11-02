#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class MaxBotService
{
    private static readonly TimeSpan BroadcastDelay = TimeSpan.FromMilliseconds(50);

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly MaxBotRepository _repository;
    private readonly MaxBotContentService _contentService;

    private static string ApiBaseUrl => MaxBotOptions.ApiBaseUrl(App.Configuration);

    public MaxBotService(MaxBotRepository repository, MaxBotContentService contentService)
    {
        _repository = repository;
        _contentService = contentService;
    }

    public async Task<MaxBotConnectionInfo> TestConnectionAsync(string botToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/me", botToken);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MaxBotConnectionInfo
                {
                    Success = false,
                    ErrorMessage = $"API MAX вернул {(int)response.StatusCode}: {TrimError(body)}"
                };
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var name = TryGetString(root, "name") ?? TryGetString(root, "first_name");
            var username = TryGetString(root, "username") ?? TryGetString(root, "nick");

            return new MaxBotConnectionInfo
            {
                Success = true,
                BotName = name,
                BotUsername = username
            };
        }
        catch (Exception ex)
        {
            return new MaxBotConnectionInfo
            {
                Success = false,
                ErrorMessage = DescribeConnectionFailure(ex)
            };
        }
    }

    /// <summary>
    /// Публикует подсказки команд в профиле бота (меню «/» в MAX).
    /// </summary>
    public async Task SyncBotCommandsAsync(string botToken, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            commands = new[]
            {
                new { name = "start", description = "Приветствие и меню" },
                new { name = "help", description = "Список всех команд" },
                new { name = "расписание", description = "Занятия на текущую неделю" },
                new { name = "контакты", description = "Адрес, телефон и e-mail" },
                new { name = "направления", description = "Направления и стоимость" },
                new { name = "скидки", description = "Скидки и льготы" },
                new { name = "privacy", description = "Политика конфиденциальности" },
                new { name = "stop", description = "Удалить мои данные" }
            }
        });

        using var request = CreateRequest(HttpMethod.Patch, "/me", botToken, payload);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                MaxBotErrors.MapApiStatus((int)response.StatusCode, TrimError(body)));
    }

    public async Task<MaxBotSendResult> SendBroadcastAsync(
        string messageText,
        int? idTemplate,
        string templateTitle,
        string audienceType,
        int? idSubscriber,
        int sentByUserId,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken)
            ?? throw new InvalidOperationException("Настройки бота MAX не заданы.");

        if (!settings.IsConfigured)
            throw new InvalidOperationException("Укажите токен бота MAX в настройках раздела «Чат-бот MAX».");

        var recipients = await ResolveRecipientsAsync(audienceType, idSubscriber, cancellationToken);
        if (recipients.Count == 0)
        {
            throw new InvalidOperationException(
                audienceType == "single"
                    ? "Выберите получателя из списка подписчиков."
                    : "Нет подписчиков бота. Попросите клиента написать боту в MAX — после первого сообщения он появится в списке.");
        }

        var successCount = 0;
        var failCount = 0;
        var done = 0;

        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SendMessageAsync(settings.BotToken, recipient.MaxChatId, messageText, cancellationToken: cancellationToken);
                successCount++;
            }
            catch (Exception)
            {
                failCount++;
            }

            done++;
            progress?.Report((done, recipients.Count));

            if (done < recipients.Count)
                await Task.Delay(BroadcastDelay, cancellationToken);
        }

        return new MaxBotSendResult
        {
            TotalCount = recipients.Count,
            SuccessCount = successCount,
            FailCount = failCount
        };
    }

    public Task SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(botToken, chatId, text, attachments: null, userId: null, cancellationToken);

    public Task SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        object[]? attachments,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(botToken, chatId, text, attachments, userId: null, cancellationToken);

    public async Task SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        object[]? attachments,
        long? userId,
        CancellationToken cancellationToken = default)
    {
        var payload = attachments is { Length: > 0 }
            ? JsonSerializer.Serialize(new { text, attachments })
            : JsonSerializer.Serialize(new { text });

        var chatError = await TryPostMessageAsync(botToken, $"chat_id={chatId}", payload, cancellationToken);
        if (chatError == null)
            return;

        if (userId.HasValue)
        {
            var userError = await TryPostMessageAsync(botToken, $"user_id={userId.Value}", payload, cancellationToken);
            if (userError == null)
                return;

            throw new InvalidOperationException(
                MaxBotErrors.MapApiStatus(userError.Value.StatusCode, userError.Value.Body));
        }

        throw new InvalidOperationException(
            MaxBotErrors.MapApiStatus(chatError.Value.StatusCode, chatError.Value.Body));
    }

    private static async Task<(int StatusCode, string Body)?> TryPostMessageAsync(
        string botToken,
        string query,
        string jsonBody,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/messages?{query}", botToken, jsonBody);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
            return null;

        return ((int)response.StatusCode, TrimError(body));
    }

    private async Task<IReadOnlyList<MaxBotSubscriber>> ResolveRecipientsAsync(
        string audienceType,
        int? idSubscriber,
        CancellationToken cancellationToken)
    {
        if (audienceType == "single")
        {
            if (idSubscriber is not int id)
                return Array.Empty<MaxBotSubscriber>();

            foreach (var subscriber in await _repository.GetSubscribersAsync(cancellationToken))
            {
                if (subscriber.IdSubscriber == id)
                    return new[] { subscriber };
            }

            return Array.Empty<MaxBotSubscriber>();
        }

        return await _repository.GetSubscribersAsync(cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string botToken,
        string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, ApiBaseUrl + path);
        request.Headers.TryAddWithoutValidation("Authorization", botToken.Trim());

        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return request;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string TrimError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "Пустой ответ API MAX.";

        return body.Length > 240 ? body[..240] + "…" : body;
    }

    private static string DescribeConnectionFailure(Exception ex)
    {
        var message = ex.Message;
        if (ex.InnerException is { Message: { Length: > 0 } inner })
            message = inner;

        if (message.Contains("proxy", StringComparison.OrdinalIgnoreCase)
            || message.Contains("127.0.0.1", StringComparison.Ordinal))
        {
            return "Не удалось подключиться к API MAX через прокси Windows. "
                   + "Отключите прокси в «Параметры → Сеть → Прокси» или укажите рабочий прокси.";
        }

        if (message.Contains("EOF", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || message.Contains("transport stream", StringComparison.OrdinalIgnoreCase)
            || message.Contains("handshake", StringComparison.OrdinalIgnoreCase)
            || message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("сертификат", StringComparison.OrdinalIgnoreCase))
        {
            return "Не удалось установить защищённое соединение с API MAX (platform-api2.max.ru).\n\n"
                   + "На новом компьютере часто не хватает сертификатов Минцифры:\n"
                   + "1. Откройте https://www.gosuslugi.ru/crt\n"
                   + "2. Скачайте и установите корневой и выпускающий сертификаты\n"
                   + "   (или запустите install_certificates.cmd от администратора)\n"
                   + "3. Перезапустите программу и снова нажмите «Проверить».\n\n"
                   + "Также проверьте интернет, VPN и антивирус/файрвол.";
        }

        if (ex is HttpRequestException)
            return "Нет связи с platform-api2.max.ru. Проверьте интернет и повторите попытку.";

        return UserFacingErrors.GetMessage(ex, "Не удалось подключиться к API MAX.");
    }

    public async Task<int> PollAndProcessUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        if (settings is not { IsConfigured: true })
            return 0;

        var marker = await _repository.GetUpdatesMarkerAsync(cancellationToken);
        const string updateTypes = "message_created,bot_started,message_callback";
        var path = marker.HasValue
            ? $"/updates?timeout=15&marker={marker.Value}&types={updateTypes}"
            : $"/updates?timeout=15&types={updateTypes}";

        using var request = CreateRequest(HttpMethod.Get, path, settings.BotToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return 0;

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var processed = 0;
        long? nextMarker = marker;

        if (root.TryGetProperty("marker", out var markerEl) && markerEl.TryGetInt64(out var m))
            nextMarker = m;

        if (root.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Array)
        {
            foreach (var update in updates.EnumerateArray())
            {
                if (await TryProcessUpdateAsync(update, settings, cancellationToken))
                    processed++;
            }
        }

        if (nextMarker != marker)
            await _repository.SaveUpdatesMarkerAsync(nextMarker, cancellationToken);

        return processed;
    }

    public async Task SendChatReplyAsync(
        MaxBotConversation conversation,
        string text,
        CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken)
            ?? throw new InvalidOperationException("Настройки бота MAX не заданы.");

        if (!settings.IsConfigured)
            throw new InvalidOperationException(
                "Отправка недоступна: укажите токен бота MAX в разделе «MAX-бот → Настройки бота».");

        await SendMessageAsync(
            settings.BotToken,
            conversation.MaxChatId,
            text,
            attachments: null,
            conversation.MaxUserId,
            cancellationToken);

        await _repository.SaveMessageAsync(new MaxBotMessage
        {
            MaxChatId = conversation.MaxChatId,
            IdSubscriber = conversation.IdSubscriber,
            Direction = "out",
            Body = text,
            SentAt = DateTime.UtcNow,
            IsRead = true
        }, cancellationToken);
    }

    private async Task<bool> TryProcessUpdateAsync(
        JsonElement update,
        MaxBotSettings settings,
        CancellationToken cancellationToken)
    {
        var updateType = TryGetString(update, "update_type") ?? string.Empty;

        if (updateType == "message_callback")
            return await TryProcessCallbackAsync(update, settings, cancellationToken);

        if (updateType is not ("message_created" or "bot_started"))
            return false;

        long? chatId = null;
        string? text = null;
        string displayName = "Клиент MAX";
        long? userId = null;
        var isBotStarted = updateType == "bot_started";

        if (update.TryGetProperty("message", out var message))
        {
            chatId = TryGetChatId(message) ?? TryGetChatId(update);
            if (message.TryGetProperty("body", out var bodyEl)
                && bodyEl.TryGetProperty("text", out var textEl))
                text = textEl.GetString();

            if (message.TryGetProperty("sender", out var sender))
            {
                displayName = TryGetString(sender, "name")
                    ?? TryGetString(sender, "username")
                    ?? displayName;
                if (sender.TryGetProperty("user_id", out var uidEl) && uidEl.TryGetInt64(out var uid))
                    userId = uid;
            }
        }

        if (isBotStarted)
        {
            chatId ??= TryGetChatId(update);
            if (update.TryGetProperty("user", out var user))
            {
                displayName = TryGetString(user, "name") ?? TryGetString(user, "username") ?? displayName;
                if (user.TryGetProperty("user_id", out var uidEl) && uidEl.TryGetInt64(out var uid))
                    userId = uid;
            }

            text ??= "/start";
        }

        if (!chatId.HasValue)
            return false;

        await UpsertAndTrackIncomingAsync(chatId.Value, displayName, userId, text?.Trim() ?? string.Empty, cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
            return isBotStarted;

        var saved = await FindSubscriberAsync(chatId.Value, cancellationToken);
        await HandleUserInputAsync(settings, chatId.Value, text.Trim(), isBotStarted, saved?.IdSubscriber, cancellationToken);
        return true;
    }

    private async Task<bool> TryProcessCallbackAsync(
        JsonElement update,
        MaxBotSettings settings,
        CancellationToken cancellationToken)
    {
        if (!update.TryGetProperty("callback", out var callback))
            return false;

        var callbackId = TryGetString(callback, "callback_id");
        var payload = MaxBotCallbackSupport.TryGetPayload(callback);
        if (string.IsNullOrWhiteSpace(callbackId) || string.IsNullOrWhiteSpace(payload))
            return false;

        var chatId = MaxBotCallbackSupport.TryResolveChatId(update, callback);
        if (!chatId.HasValue)
            return false;

        string displayName = "Клиент MAX";
        long? userId = null;
        if (update.TryGetProperty("user", out var user))
        {
            displayName = TryGetString(user, "name") ?? TryGetString(user, "username") ?? displayName;
            if (user.TryGetProperty("user_id", out var uidEl) && uidEl.TryGetInt64(out var uid))
                userId = uid;
        }
        else if (callback.TryGetProperty("user", out user))
        {
            displayName = TryGetString(user, "name") ?? TryGetString(user, "username") ?? displayName;
            if (user.TryGetProperty("user_id", out var uidEl) && uidEl.TryGetInt64(out var uid))
                userId = uid;
        }

        await UpsertAndTrackIncomingAsync(chatId.Value, displayName, userId, $"[кнопка] {payload}", cancellationToken);

        var saved = await FindSubscriberAsync(chatId.Value, cancellationToken);
        var response = await BuildCallbackResponseAsync(settings, payload, cancellationToken);
        if (response == null)
            return false;

        try
        {
            await AcknowledgeCallbackAsync(settings.BotToken, callbackId, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        await SendMessageAsync(settings.BotToken, chatId.Value, response.Value.Text, response.Value.Attachments, cancellationToken);
        await SaveOutgoingAsync(chatId.Value, saved?.IdSubscriber, response.Value.Text, cancellationToken);
        return true;
    }

    private async Task HandleUserInputAsync(
        MaxBotSettings settings,
        long chatId,
        string incomingText,
        bool isBotStarted,
        int? idSubscriber,
        CancellationToken cancellationToken)
    {
        var compliance = settings.ToComplianceInfo();
        var command = isBotStarted ? MaxBotCommandHandler.UserCommand.Start : MaxBotCommandHandler.MatchCommand(incomingText);

        if (command == MaxBotCommandHandler.UserCommand.Stop
            || MaxBotCommandHandler.MatchCommand(incomingText) == MaxBotCommandHandler.UserCommand.Stop)
        {
            await _repository.DeleteSubscriberDataByChatIdAsync(chatId, cancellationToken);
            var stopText = MaxBotCommandHandler.BuildStopConfirmationMessage();
            try
            {
                await SendMessageAsync(settings.BotToken, chatId, stopText, MaxBotKeyboardBuilder.MainMenuRow(), cancellationToken);
            }
            catch
            {
                // Пользователь уже удалён — подтверждение может не дойти
            }
            return;
        }

        string? text = null;
        object[]? attachments = null;

        switch (command)
        {
            case MaxBotCommandHandler.UserCommand.Start:
                text = MaxBotCommandHandler.BuildWelcomeMessage(compliance);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            case MaxBotCommandHandler.UserCommand.Help:
                text = MaxBotCommandHandler.BuildHelpMessage(compliance);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            case MaxBotCommandHandler.UserCommand.Schedule:
                text = MaxBotContentService.BuildBranchSelectionPrompt();
                attachments = MaxBotKeyboardBuilder.BranchSelectionRows();
                break;

            case MaxBotCommandHandler.UserCommand.Contacts:
                text = MaxBotCommandHandler.BuildContactsBranchPrompt();
                attachments = MaxBotKeyboardBuilder.ContactsBranchSelectionRows();
                break;

            case MaxBotCommandHandler.UserCommand.Directions:
                (text, attachments) = await BuildDirectionsListResponseAsync(cancellationToken);
                break;

            case MaxBotCommandHandler.UserCommand.Discounts:
                text = MaxBotCommandHandler.BuildDiscountsMessage(compliance, settings.DiscountsText);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            case MaxBotCommandHandler.UserCommand.Privacy:
                text = MaxBotCommandHandler.BuildPrivacyMessage(compliance);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            case MaxBotCommandHandler.UserCommand.Terms:
                text = MaxBotCommandHandler.BuildTermsMessage(compliance);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            case MaxBotCommandHandler.UserCommand.Record:
                text = MaxBotCommandHandler.BuildRecordInfoMessage(compliance);
                attachments = MaxBotKeyboardBuilder.MainMenuRow();
                break;

            default:
                if (MaxBotCommandHandler.TryGetAutoReply(incomingText, compliance, out var autoReply))
                {
                    text = autoReply;
                    attachments = MaxBotKeyboardBuilder.MainMenuRow();
                }
                break;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        await SendMessageAsync(settings.BotToken, chatId, text, attachments, cancellationToken);
        await SaveOutgoingAsync(chatId, idSubscriber, text, cancellationToken);
    }

    private async Task<(string Text, object[] Attachments)?> BuildCallbackResponseAsync(
        MaxBotSettings settings,
        string payload,
        CancellationToken cancellationToken)
    {
        var compliance = settings.ToComplianceInfo();

        if (payload == MaxBotKeyboardBuilder.MenuHelp)
            return (MaxBotCommandHandler.BuildHelpMessage(compliance), MaxBotKeyboardBuilder.MainMenuRow());

        if (payload == MaxBotKeyboardBuilder.MenuDirections)
            return await BuildDirectionsListResponseAsync(cancellationToken);

        if (payload == MaxBotKeyboardBuilder.MenuSchedule)
            return (MaxBotContentService.BuildBranchSelectionPrompt(), MaxBotKeyboardBuilder.BranchSelectionRows());

        if (payload == MaxBotKeyboardBuilder.MenuContacts)
            return (MaxBotCommandHandler.BuildContactsBranchPrompt(), MaxBotKeyboardBuilder.ContactsBranchSelectionRows());

        if (payload.StartsWith("branch:", StringComparison.Ordinal)
            && int.TryParse(payload.AsSpan(7), out var idBranch))
        {
            if (StudyBranch.Find(idBranch) == null)
                return ("Филиал не найден.", MaxBotKeyboardBuilder.BranchSelectionRows());

            var scheduleText = await _contentService.BuildWeekScheduleTextAsync(idBranch, DateTime.Today, cancellationToken);
            return (scheduleText, MaxBotKeyboardBuilder.ScheduleAfterBranchRows());
        }

        if (payload.StartsWith("contacts:", StringComparison.Ordinal)
            && int.TryParse(payload.AsSpan(9), out var idContactsBranch))
        {
            var branchInfo = await GetBranchInfoAsync(idContactsBranch, cancellationToken);
            if (branchInfo == null)
                return ("Филиал не найден.", MaxBotKeyboardBuilder.ContactsBranchSelectionRows());

            var contactsText = MaxBotCommandHandler.BuildBranchContactsMessage(branchInfo, compliance);
            return (contactsText, MaxBotKeyboardBuilder.ContactsAfterBranchRows());
        }

        if (payload.StartsWith("dir:", StringComparison.Ordinal)
            && int.TryParse(payload.AsSpan(4), out var idDirection))
        {
            var directions = await _contentService.GetDirectionsAsync(cancellationToken);
            var direction = directions.FirstOrDefault(d => d.IdDirection == idDirection);
            if (direction == null)
                return ("Направление не найдено.", MaxBotKeyboardBuilder.BackAndMenuRows());

            return (MaxBotCommandHandler.BuildDirectionConsultationMessage(direction.Name), MaxBotKeyboardBuilder.BackAndMenuRows());
        }

        return null;
    }

    private async Task<(string Text, object[] Attachments)> BuildDirectionsListResponseAsync(
        CancellationToken cancellationToken)
    {
        var directions = await _contentService.GetDirectionsAsync(cancellationToken);
        if (directions.Count == 0)
        {
            return ("Пока нет активных направлений. Уточните у администратора центра.",
                MaxBotKeyboardBuilder.MainMenuRow());
        }

        var text = "🎨 Направления\n\nВыберите направление, чтобы узнать подробности и стоимость:";
        var buttons = directions.Select(d => (d.IdDirection, d.Name));
        return (text, MaxBotKeyboardBuilder.DirectionListRows(buttons));
    }

    private async Task<MaxBotBranchInfo?> GetBranchInfoAsync(int idBranch, CancellationToken cancellationToken)
    {
        var fromDb = await _repository.GetBranchAsync(idBranch, cancellationToken);
        if (fromDb != null)
            return fromDb;

        var branch = StudyBranch.Find(idBranch);
        if (branch == null)
            return null;

        return new MaxBotBranchInfo
        {
            IdBranch = branch.IdBranch,
            ShortName = branch.ShortName,
            AddressLine = branch.AddressLine
        };
    }

    private async Task AcknowledgeCallbackAsync(
        string botToken,
        string callbackId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { notification = "✓" });
        var path = $"/answers?callback_id={Uri.EscapeDataString(callbackId)}";

        using var request = CreateRequest(HttpMethod.Post, path, botToken, payload);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(TrimError(body) ?? $"Ошибка callback ({(int)response.StatusCode})");
    }

    private async Task AnswerCallbackAsync(
        string botToken,
        string callbackId,
        string text,
        object[]? attachments,
        CancellationToken cancellationToken)
    {
        var messageBody = attachments is { Length: > 0 }
            ? new { text, attachments }
            : (object)new { text };

        var payload = JsonSerializer.Serialize(new { message = messageBody });
        var path = $"/answers?callback_id={Uri.EscapeDataString(callbackId)}";

        using var request = CreateRequest(HttpMethod.Post, path, botToken, payload);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(TrimError(body) ?? $"Ошибка callback ({(int)response.StatusCode})");
    }

    private async Task UpsertAndTrackIncomingAsync(
        long chatId,
        string displayName,
        long? userId,
        string body,
        CancellationToken cancellationToken)
    {
        await _repository.UpsertSubscriberAsync(new MaxBotSubscriber
        {
            MaxChatId = chatId,
            MaxUserId = userId,
            DisplayName = displayName
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
            return;

        var saved = await FindSubscriberAsync(chatId, cancellationToken);
        await _repository.SaveMessageAsync(new MaxBotMessage
        {
            MaxChatId = chatId,
            IdSubscriber = saved?.IdSubscriber,
            Direction = "in",
            Body = body,
            SentAt = DateTime.UtcNow,
            IsRead = false
        }, cancellationToken);
    }

    private async Task SaveOutgoingAsync(long chatId, int? idSubscriber, string text, CancellationToken cancellationToken)
    {
        await _repository.SaveMessageAsync(new MaxBotMessage
        {
            MaxChatId = chatId,
            IdSubscriber = idSubscriber,
            Direction = "out",
            Body = text,
            SentAt = DateTime.UtcNow,
            IsRead = true
        }, cancellationToken);
    }

    private async Task<MaxBotSubscriber?> FindSubscriberAsync(long chatId, CancellationToken cancellationToken) =>
        await _repository.GetSubscriberByChatIdAsync(chatId, cancellationToken);

    private static long? TryGetChatId(JsonElement element)
    {
        if (element.TryGetProperty("chat_id", out var chatEl) && chatEl.TryGetInt64(out var chatId))
            return chatId;

        if (element.TryGetProperty("recipient", out var recipient)
            && recipient.TryGetProperty("chat_id", out chatEl)
            && chatEl.TryGetInt64(out chatId))
            return chatId;

        return null;
    }
}
