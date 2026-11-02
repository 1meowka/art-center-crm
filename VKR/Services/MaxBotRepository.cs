#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class MaxBotRepository
{
    public MaxBotRepository()
    {
        MaxBotSchema.EnsureIfPossible();
    }

    public async Task<MaxBotSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT bot_token, bot_name, bot_username,
       org_name, org_address, org_inn, support_phone, support_email,
       privacy_url, terms_url, welcome_text, discounts_text
FROM public.max_bot_settings
WHERE id_settings = 1;", connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new MaxBotSettings();

        var rawToken = reader.GetString(reader.GetOrdinal("bot_token"));
        return new MaxBotSettings
        {
            BotToken = SecretProtector.Unprotect(rawToken),
            TokenNeedsReentry = SecretProtector.NeedsReentry(rawToken),
            BotName = reader.IsDBNull(reader.GetOrdinal("bot_name"))
                ? null
                : reader.GetString(reader.GetOrdinal("bot_name")),
            BotUsername = reader.IsDBNull(reader.GetOrdinal("bot_username"))
                ? null
                : reader.GetString(reader.GetOrdinal("bot_username")),
            OrgName = ReadOptionalString(reader, "org_name") ?? "Творческий центр «Арт-студия»",
            OrgAddress = ReadOptionalString(reader, "org_address"),
            OrgInn = ReadOptionalString(reader, "org_inn"),
            SupportPhone = ReadOptionalString(reader, "support_phone"),
            SupportEmail = ReadOptionalString(reader, "support_email"),
            PrivacyUrl = ReadOptionalString(reader, "privacy_url"),
            TermsUrl = ReadOptionalString(reader, "terms_url"),
            WelcomeText = ReadOptionalString(reader, "welcome_text"),
            DiscountsText = ReadOptionalString(reader, "discounts_text")
        };
    }

    public async Task<MaxBotBranchInfo?> GetBranchAsync(int idBranch, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT id_branch, short_name, address_line, support_phone, support_email, contacts_note
FROM public.max_bot_branch
WHERE id_branch = @id_branch;", connection);
        command.Parameters.Add("id_branch", NpgsqlDbType.Smallint).Value = (short)idBranch;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new MaxBotBranchInfo
        {
            IdBranch = reader.GetInt16(reader.GetOrdinal("id_branch")),
            ShortName = reader.GetString(reader.GetOrdinal("short_name")),
            AddressLine = reader.GetString(reader.GetOrdinal("address_line")),
            SupportPhone = ReadOptionalString(reader, "support_phone"),
            SupportEmail = ReadOptionalString(reader, "support_email"),
            ContactsNote = ReadOptionalString(reader, "contacts_note")
        };
    }

    public async Task SaveSettingsAsync(MaxBotSettings settings, int? updatedByUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_settings
    (id_settings, bot_token, bot_name, bot_username,
     org_name, org_address, org_inn, support_phone, support_email,
     privacy_url, terms_url, welcome_text, discounts_text, updated_at, updated_by)
VALUES
    (1, @bot_token, @bot_name, @bot_username,
     @org_name, @org_address, @org_inn, @support_phone, @support_email,
     @privacy_url, @terms_url, @welcome_text, @discounts_text, NOW(), @updated_by)
ON CONFLICT (id_settings) DO UPDATE SET
    bot_token = EXCLUDED.bot_token,
    bot_name = EXCLUDED.bot_name,
    bot_username = EXCLUDED.bot_username,
    org_name = EXCLUDED.org_name,
    org_address = EXCLUDED.org_address,
    org_inn = EXCLUDED.org_inn,
    support_phone = EXCLUDED.support_phone,
    support_email = EXCLUDED.support_email,
    privacy_url = EXCLUDED.privacy_url,
    terms_url = EXCLUDED.terms_url,
    welcome_text = EXCLUDED.welcome_text,
    discounts_text = EXCLUDED.discounts_text,
    updated_at = NOW(),
    updated_by = EXCLUDED.updated_by;", connection);

        command.Parameters.Add("bot_token", NpgsqlDbType.Text).Value =
            SecretProtector.Protect(settings.BotToken.Trim());
        command.Parameters.Add("bot_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.BotName) ? DBNull.Value : settings.BotName.Trim();
        command.Parameters.Add("bot_username", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.BotUsername) ? DBNull.Value : settings.BotUsername.Trim();
        command.Parameters.Add("org_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.OrgName) ? DBNull.Value : settings.OrgName.Trim();
        command.Parameters.Add("org_address", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(settings.OrgAddress) ? DBNull.Value : settings.OrgAddress.Trim();
        command.Parameters.Add("org_inn", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.OrgInn) ? DBNull.Value : settings.OrgInn.Trim();
        command.Parameters.Add("support_phone", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.SupportPhone) ? DBNull.Value : settings.SupportPhone.Trim();
        command.Parameters.Add("support_email", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(settings.SupportEmail) ? DBNull.Value : settings.SupportEmail.Trim();
        command.Parameters.Add("privacy_url", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(settings.PrivacyUrl) ? DBNull.Value : settings.PrivacyUrl.Trim();
        command.Parameters.Add("terms_url", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(settings.TermsUrl) ? DBNull.Value : settings.TermsUrl.Trim();
        command.Parameters.Add("welcome_text", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(settings.WelcomeText) ? DBNull.Value : settings.WelcomeText.Trim();
        command.Parameters.Add("discounts_text", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(settings.DiscountsText) ? DBNull.Value : settings.DiscountsText.Trim();
        command.Parameters.Add("updated_by", NpgsqlDbType.Integer).Value =
            updatedByUserId.HasValue ? updatedByUserId.Value : DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaxBotTemplateFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<MaxBotTemplateFolder>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT f.id_folder, f.name, f.sort_order,
       COUNT(t.id_template)::int AS template_count
FROM public.max_bot_template_folder f
LEFT JOIN public.max_bot_template t ON t.id_folder = f.id_folder
GROUP BY f.id_folder, f.name, f.sort_order
ORDER BY f.sort_order, f.name;", connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MaxBotTemplateFolder
            {
                IdFolder = reader.GetInt32(reader.GetOrdinal("id_folder")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
                TemplateCount = reader.GetInt32(reader.GetOrdinal("template_count"))
            });
        }

        return list;
    }

    public async Task<int> InsertFolderAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var sortCmd = new NpgsqlCommand(
            "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM public.max_bot_template_folder;", connection);
        var sortOrder = Convert.ToInt32(await sortCmd.ExecuteScalarAsync(cancellationToken));

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_template_folder (name, sort_order)
VALUES (@name, @sort_order)
RETURNING id_folder;", connection);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("sort_order", sortOrder);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task RenameFolderAsync(int idFolder, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
UPDATE public.max_bot_template_folder
SET name = @name
WHERE id_folder = @id_folder;", connection);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("id_folder", idFolder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteFolderAsync(int idFolder, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.max_bot_template_folder WHERE id_folder = @id_folder;", connection);
        command.Parameters.AddWithValue("id_folder", idFolder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaxBotTemplate>> GetTemplatesAsync(int idFolder, CancellationToken cancellationToken = default)
    {
        var list = new List<MaxBotTemplate>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT id_template, id_folder, title, body, updated_at
FROM public.max_bot_template
WHERE id_folder = @id_folder
ORDER BY title;", connection);
        command.Parameters.AddWithValue("id_folder", idFolder);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MaxBotTemplate
            {
                IdTemplate = reader.GetInt32(reader.GetOrdinal("id_template")),
                IdFolder = reader.GetInt32(reader.GetOrdinal("id_folder")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Body = reader.GetString(reader.GetOrdinal("body")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            });
        }

        return list;
    }

    public async Task<int> InsertTemplateAsync(MaxBotTemplate template, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_template (id_folder, title, body)
VALUES (@id_folder, @title, @body)
RETURNING id_template;", connection);
        command.Parameters.AddWithValue("id_folder", template.IdFolder);
        command.Parameters.AddWithValue("title", template.Title.Trim());
        command.Parameters.AddWithValue("body", template.Body.Trim());
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task UpdateTemplateAsync(MaxBotTemplate template, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
UPDATE public.max_bot_template
SET title = @title, body = @body, updated_at = NOW()
WHERE id_template = @id_template;", connection);
        command.Parameters.AddWithValue("id_template", template.IdTemplate);
        command.Parameters.AddWithValue("title", template.Title.Trim());
        command.Parameters.AddWithValue("body", template.Body.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTemplateAsync(int idTemplate, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.max_bot_template WHERE id_template = @id_template;", connection);
        command.Parameters.AddWithValue("id_template", idTemplate);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MaxBotSubscriber?> GetSubscriberByChatIdAsync(long maxChatId, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT id_subscriber, max_chat_id, max_user_id, display_name,
       id_representative, id_student, subscribed_at, last_message_at
FROM public.max_bot_subscriber
WHERE max_chat_id = @max_chat_id
LIMIT 1;", connection);
        command.Parameters.AddWithValue("max_chat_id", maxChatId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubscriber(reader) : null;
    }

    public async Task ClearBotCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_settings (id_settings, bot_token, bot_name, bot_username, updates_marker)
VALUES (1, '', NULL, NULL, NULL)
ON CONFLICT (id_settings) DO UPDATE SET
    bot_token = '',
    bot_name = NULL,
    bot_username = NULL,
    updates_marker = NULL,
    updated_at = NOW();", connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaxBotSubscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<MaxBotSubscriber>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT id_subscriber, max_chat_id, max_user_id, display_name,
       id_representative, id_student, subscribed_at, last_message_at
FROM public.max_bot_subscriber
ORDER BY display_name, subscribed_at;", connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadSubscriber(reader));
        }

        return list;
    }

    public async Task UpsertSubscriberAsync(MaxBotSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_subscriber
    (max_chat_id, max_user_id, display_name, id_representative, id_student, subscribed_at, last_message_at)
VALUES
    (@max_chat_id, @max_user_id, @display_name, @id_representative, @id_student, NOW(), NOW())
ON CONFLICT (max_chat_id) DO UPDATE SET
    max_user_id = EXCLUDED.max_user_id,
    display_name = EXCLUDED.display_name,
    id_representative = EXCLUDED.id_representative,
    id_student = EXCLUDED.id_student,
    last_message_at = NOW();", connection);

        command.Parameters.AddWithValue("max_chat_id", subscriber.MaxChatId);
        command.Parameters.Add("max_user_id", NpgsqlDbType.Bigint).Value =
            subscriber.MaxUserId.HasValue ? subscriber.MaxUserId.Value : DBNull.Value;
        command.Parameters.AddWithValue("display_name", subscriber.DisplayName.Trim());
        command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value =
            subscriber.IdRepresentative.HasValue ? subscriber.IdRepresentative.Value : DBNull.Value;
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value =
            subscriber.IdStudent.HasValue ? subscriber.IdStudent.Value : DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSubscriberAsync(int idSubscriber, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.max_bot_subscriber WHERE id_subscriber = @id_subscriber;", connection);
        command.Parameters.AddWithValue("id_subscriber", idSubscriber);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSubscriberDataByChatIdAsync(long maxChatId, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var msgCmd = new NpgsqlCommand(
            "DELETE FROM public.max_bot_message WHERE max_chat_id = @max_chat_id;", connection, tx))
        {
            msgCmd.Parameters.AddWithValue("max_chat_id", maxChatId);
            await msgCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var subCmd = new NpgsqlCommand(
            "DELETE FROM public.max_bot_subscriber WHERE max_chat_id = @max_chat_id;", connection, tx))
        {
            subCmd.Parameters.AddWithValue("max_chat_id", maxChatId);
            await subCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<long?> GetUpdatesMarkerAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT updates_marker FROM public.max_bot_settings WHERE id_settings = 1;", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public async Task SaveUpdatesMarkerAsync(long? marker, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_settings (id_settings, bot_token, updates_marker)
VALUES (1, '', @marker)
ON CONFLICT (id_settings) DO UPDATE SET updates_marker = EXCLUDED.updates_marker;", connection);
        command.Parameters.Add("marker", NpgsqlDbType.Bigint).Value =
            marker.HasValue ? marker.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetTotalUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT COUNT(*)::int
FROM public.max_bot_message
WHERE direction = 'in' AND is_read = FALSE;", connection);

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<MaxBotConversation>> GetConversationsAsync(CancellationToken cancellationToken = default) =>
        await GetConversationsAsync(newestFirst: true, cancellationToken);

    public async Task<IReadOnlyList<MaxBotConversation>> GetConversationsAsync(
        bool newestFirst,
        CancellationToken cancellationToken = default)
    {
        var list = new List<MaxBotConversation>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var order = newestFirst ? "DESC" : "ASC";
        await using var command = new NpgsqlCommand($@"
SELECT s.id_subscriber, s.max_chat_id, s.max_user_id, s.display_name,
       COALESCE(lm.body, '') AS last_preview,
       lm.sent_at AS last_at,
       COALESCE(unread.cnt, 0)::int AS unread_count
FROM public.max_bot_subscriber s
LEFT JOIN LATERAL (
    SELECT body, sent_at
    FROM public.max_bot_message m
    WHERE m.max_chat_id = s.max_chat_id
    ORDER BY sent_at DESC
    LIMIT 1
) lm ON TRUE
LEFT JOIN LATERAL (
    SELECT COUNT(*) AS cnt
    FROM public.max_bot_message m
    WHERE m.max_chat_id = s.max_chat_id
      AND m.direction = 'in'
      AND m.is_read = FALSE
) unread ON TRUE
ORDER BY COALESCE(lm.sent_at, s.subscribed_at) {order};", connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MaxBotConversation
            {
                IdSubscriber = reader.GetInt32(reader.GetOrdinal("id_subscriber")),
                MaxChatId = reader.GetInt64(reader.GetOrdinal("max_chat_id")),
                MaxUserId = reader.IsDBNull(reader.GetOrdinal("max_user_id"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("max_user_id")),
                DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
                LastMessagePreview = TruncatePreview(reader.GetString(reader.GetOrdinal("last_preview"))),
                LastMessageAt = reader.IsDBNull(reader.GetOrdinal("last_at"))
                    ? null
                    : reader.GetDateTime(reader.GetOrdinal("last_at")),
                UnreadCount = reader.GetInt32(reader.GetOrdinal("unread_count"))
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<MaxBotMessage>> GetMessagesAsync(long maxChatId, CancellationToken cancellationToken = default) =>
        await GetMessagesAsync(maxChatId, oldestFirst: true, cancellationToken);

    public async Task<IReadOnlyList<MaxBotMessage>> GetMessagesAsync(
        long maxChatId,
        bool oldestFirst,
        CancellationToken cancellationToken = default)
    {
        var list = new List<MaxBotMessage>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand($@"
SELECT id_message, max_chat_id, id_subscriber, direction, body, sent_at, is_read
FROM public.max_bot_message
WHERE max_chat_id = @max_chat_id
ORDER BY sent_at {(oldestFirst ? "ASC" : "DESC")};", connection);
        command.Parameters.AddWithValue("max_chat_id", maxChatId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(ReadMessage(reader));

        return list;
    }

    public async Task SaveMessageAsync(MaxBotMessage message, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
INSERT INTO public.max_bot_message
    (max_chat_id, id_subscriber, direction, body, sent_at, is_read, max_update_id)
VALUES
    (@max_chat_id, @id_subscriber, @direction, @body, COALESCE(@sent_at, NOW()), @is_read, @max_update_id);", connection);

        command.Parameters.AddWithValue("max_chat_id", message.MaxChatId);
        command.Parameters.Add("id_subscriber", NpgsqlDbType.Integer).Value =
            message.IdSubscriber.HasValue ? message.IdSubscriber.Value : DBNull.Value;
        command.Parameters.AddWithValue("direction", message.Direction);
        command.Parameters.AddWithValue("body", message.Body.Trim());
        command.Parameters.Add("sent_at", NpgsqlDbType.TimestampTz).Value = message.SentAt;
        command.Parameters.AddWithValue("is_read", message.IsRead);
        command.Parameters.Add("max_update_id", NpgsqlDbType.Bigint).Value =
            DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var touchCmd = new NpgsqlCommand(@"
UPDATE public.max_bot_subscriber
SET last_message_at = NOW()
WHERE max_chat_id = @max_chat_id;", connection);
        touchCmd.Parameters.AddWithValue("max_chat_id", message.MaxChatId);
        await touchCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkChatUnreadAsync(long maxChatId, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
UPDATE public.max_bot_message
SET is_read = FALSE
WHERE max_chat_id = @max_chat_id AND direction = 'in';", connection);
        command.Parameters.AddWithValue("max_chat_id", maxChatId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkChatReadAsync(long maxChatId, CancellationToken cancellationToken = default)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
UPDATE public.max_bot_message
SET is_read = TRUE
WHERE max_chat_id = @max_chat_id AND direction = 'in' AND is_read = FALSE;", connection);
        command.Parameters.AddWithValue("max_chat_id", maxChatId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MaxBotMessage ReadMessage(NpgsqlDataReader reader) => new()
    {
        IdMessage = reader.GetInt32(reader.GetOrdinal("id_message")),
        MaxChatId = reader.GetInt64(reader.GetOrdinal("max_chat_id")),
        IdSubscriber = reader.IsDBNull(reader.GetOrdinal("id_subscriber"))
            ? null
            : reader.GetInt32(reader.GetOrdinal("id_subscriber")),
        Direction = reader.GetString(reader.GetOrdinal("direction")),
        Body = reader.GetString(reader.GetOrdinal("body")),
        SentAt = reader.GetDateTime(reader.GetOrdinal("sent_at")),
        IsRead = reader.GetBoolean(reader.GetOrdinal("is_read"))
    };

    private static string TruncatePreview(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine[..57] + "…";
    }

    private static string? ReadOptionalString(NpgsqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

    private static MaxBotSubscriber ReadSubscriber(NpgsqlDataReader reader) => new()
    {
        IdSubscriber = reader.GetInt32(reader.GetOrdinal("id_subscriber")),
        MaxChatId = reader.GetInt64(reader.GetOrdinal("max_chat_id")),
        MaxUserId = reader.IsDBNull(reader.GetOrdinal("max_user_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("max_user_id")),
        DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
        IdRepresentative = reader.IsDBNull(reader.GetOrdinal("id_representative"))
            ? null
            : reader.GetInt32(reader.GetOrdinal("id_representative")),
        IdStudent = reader.IsDBNull(reader.GetOrdinal("id_student"))
            ? null
            : reader.GetInt32(reader.GetOrdinal("id_student")),
        SubscribedAt = reader.GetDateTime(reader.GetOrdinal("subscribed_at")),
        LastMessageAt = reader.IsDBNull(reader.GetOrdinal("last_message_at"))
            ? null
            : reader.GetDateTime(reader.GetOrdinal("last_message_at"))
    };
}
