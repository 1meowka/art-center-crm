#nullable enable

using System;
using System.Text;
using VKR.Models;

namespace VKR.Services;

/// <summary>
/// Автоответы бота по командам пользователя (требования MAX: подсказки, юр. информация, поддержка).
/// </summary>
public static class MaxBotCommandHandler
{
    public enum UserCommand
    {
        None,
        Start,
        Help,
        Schedule,
        Contacts,
        Directions,
        Privacy,
        Terms,
        Stop,
        Record,
        Discounts
    }

    public static UserCommand MatchCommand(string? incomingText)
    {
        if (string.IsNullOrWhiteSpace(incomingText))
            return UserCommand.None;

        var lower = incomingText.Trim().ToLowerInvariant();

        if (IsCommand(lower, "/start", "/старт") || lower == "start")
            return UserCommand.Start;

        if (IsCommand(lower, "/help", "/помощь", "/menu", "/меню") || lower is "help" or "menu" or "помощь" or "меню")
            return UserCommand.Help;

        if (IsCommand(lower, "/расписание", "/schedule") || lower == "расписание")
            return UserCommand.Schedule;

        if (IsCommand(lower, "/контакты", "/contacts") || lower == "контакты")
            return UserCommand.Contacts;

        if (IsCommand(lower, "/направления", "/directions") || lower == "направления")
            return UserCommand.Directions;

        if (IsCommand(lower, "/скидки", "/льготы", "/discounts")
            || lower is "скидки" or "льготы" or "скидки и льготы")
            return UserCommand.Discounts;

        if (IsCommand(lower, "/privacy", "/конфиденциальность", "/политика"))
            return UserCommand.Privacy;

        if (IsCommand(lower, "/terms", "/соглашение", "/agreement"))
            return UserCommand.Terms;

        if (IsCommand(lower, "/stop", "/delete", "/удалить", "/отписаться"))
            return UserCommand.Stop;

        if (lower == "запись")
            return UserCommand.Record;

        return UserCommand.None;
    }

    public static bool TryGetAutoReply(string? incomingText, MaxBotComplianceInfo compliance, out string reply)
    {
        reply = string.Empty;
        return MatchCommand(incomingText) switch
        {
            UserCommand.Start => SetReply(out reply, BuildWelcomeMessage(compliance)),
            UserCommand.Help => SetReply(out reply, BuildHelpMessage(compliance)),
            UserCommand.Contacts => SetReply(out reply, BuildContactsMessage(compliance)),
            UserCommand.Privacy => SetReply(out reply, BuildPrivacyMessage(compliance)),
            UserCommand.Terms => SetReply(out reply, BuildTermsMessage(compliance)),
            UserCommand.Stop => SetReply(out reply, "__DELETE_USER_DATA__"),
            _ => false
        };
    }

    private static bool SetReply(out string reply, string value)
    {
        reply = value;
        return true;
    }

    public static string BuildWelcomeMessage(MaxBotComplianceInfo c) =>
        string.IsNullOrWhiteSpace(c.WelcomeText)
            ? DefaultWelcome(c)
            : c.WelcomeText.Trim() + "\n\n" + BuildHelpMessage(c);

    public static string BuildHelpMessage(MaxBotComplianceInfo c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Доступные команды:");
        sb.AppendLine("• /расписание — занятия на текущую неделю");
        sb.AppendLine("• /контакты — адрес, телефон и e-mail по филиалам");
        sb.AppendLine("• /направления — направления и стоимость");
        sb.AppendLine("• /скидки — скидки и льготы");
        sb.AppendLine("• /start — приветствие");
        sb.AppendLine("• /privacy — политика конфиденциальности");
        sb.AppendLine("• /stop — удалить ваши данные из бота");
        sb.AppendLine();
        sb.AppendLine("Также можно написать «Запись» — информация о пробном занятии.");
        sb.AppendLine();
        sb.AppendLine(BuildOperatorBlock(c));
        return sb.ToString().TrimEnd();
    }

    public static string BuildPrivacyMessage(MaxBotComplianceInfo c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Политика конфиденциальности");
        sb.AppendLine();
        sb.AppendLine($"Оператор: {c.OrgDisplayName}");
        if (!string.IsNullOrWhiteSpace(c.OrgAddress))
            sb.AppendLine($"Адрес: {c.OrgAddress.Trim()}");
        if (!string.IsNullOrWhiteSpace(c.OrgInn))
            sb.AppendLine($"ИНН: {c.OrgInn.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Мы обрабатываем имя и идентификатор чата MAX для связи с вами по вопросам занятий и сервисных уведомлений. Данные не передаются третьим лицам, кроме случаев, предусмотренных законом.");
        sb.AppendLine();
        sb.AppendLine("Вы можете запросить удаление данных командой /stop.");
        if (!string.IsNullOrWhiteSpace(c.PrivacyUrl))
            sb.AppendLine($"Полный текст: {c.PrivacyUrl.Trim()}");
        return sb.ToString().TrimEnd();
    }

    public static string BuildTermsMessage(MaxBotComplianceInfo c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Пользовательское соглашение");
        sb.AppendLine();
        sb.AppendLine($"Используя бота {c.OrgDisplayName}, вы соглашаетесь получать сервисные сообщения о занятиях, оплате и записи.");
        sb.AppendLine("Бот не предназначен для рекламных рассылок без вашего согласия.");
        if (!string.IsNullOrWhiteSpace(c.TermsUrl))
            sb.AppendLine($"Полный текст: {c.TermsUrl.Trim()}");
        else
            sb.AppendLine("Полный текст соглашения размещён на сайте организации.");
        return sb.ToString().TrimEnd();
    }

    public static string BuildContactsBranchPrompt() =>
        "📞 Контакты\n\nВыберите филиал:";

    public static string BuildBranchContactsMessage(MaxBotBranchInfo branch, MaxBotComplianceInfo org)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📞 Контакты");
        sb.AppendLine();
        sb.AppendLine(org.OrgDisplayName);
        sb.AppendLine($"📍 {branch.ShortName}");
        sb.AppendLine(branch.AddressLine);

        if (!string.IsNullOrWhiteSpace(branch.SupportPhone))
            sb.AppendLine($"📞 {branch.SupportPhone.Trim()}");
        else if (!string.IsNullOrWhiteSpace(org.SupportPhone))
            sb.AppendLine($"📞 {org.SupportPhone.Trim()}");

        if (!string.IsNullOrWhiteSpace(branch.SupportEmail))
            sb.AppendLine($"✉ {branch.SupportEmail.Trim()}");
        else if (!string.IsNullOrWhiteSpace(org.SupportEmail))
            sb.AppendLine($"✉ {org.SupportEmail.Trim()}");

        if (!string.IsNullOrWhiteSpace(branch.ContactsNote)
            && !branch.ContactsNote.Contains("Шаблон контактов", StringComparison.Ordinal))
            sb.AppendLine().AppendLine(branch.ContactsNote.Trim());

        sb.AppendLine();
        sb.AppendLine("Напишите «Запись» — менеджер ответит в рабочее время.");
        return sb.ToString().TrimEnd();
    }

    public static string BuildDiscountsMessage(MaxBotComplianceInfo c, string? discountsText)
    {
        if (!string.IsNullOrWhiteSpace(discountsText))
            return discountsText.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("🎁 Скидки и льготы");
        sb.AppendLine();
        sb.AppendLine($"Информация о скидках и льготах в {c.OrgDisplayName} будет опубликована здесь в ближайшее время.");
        sb.AppendLine("По вопросам обратитесь к администратору центра.");
        sb.AppendLine();
        sb.AppendLine(BuildOperatorBlock(c));
        return sb.ToString().TrimEnd();
    }

    public static string BuildDirectionConsultationMessage(string directionName) =>
        $"Вы выбрали направление «{directionName.Trim()}».\n\n" +
        "Спасибо за интерес! В ближайшее время администратор центра свяжется с вами и проконсультирует по занятиям и записи.";

    public static string BuildContactsMessage(MaxBotComplianceInfo c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📞 Контакты");
        sb.AppendLine();
        sb.AppendLine(c.OrgDisplayName);
        if (!string.IsNullOrWhiteSpace(c.OrgAddress))
            sb.AppendLine($"📍 {c.OrgAddress.Trim()}");
        if (!string.IsNullOrWhiteSpace(c.SupportPhone))
            sb.AppendLine($"📞 {c.SupportPhone.Trim()}");
        if (!string.IsNullOrWhiteSpace(c.SupportEmail))
            sb.AppendLine($"✉ {c.SupportEmail.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Напишите «Запись» — менеджер ответит в рабочее время.");
        return sb.ToString().TrimEnd();
    }

    public static string BuildStopConfirmationMessage() =>
        "Ваши данные удалены из нашей системы. Вы больше не будете получать сообщения от этого бота.\n\n" +
        "Чтобы снова связаться с центром, напишите /start.";

    public static string BuildRecordInfoMessage(MaxBotComplianceInfo c) =>
        $"Запись на пробное занятие\n\n" +
        $"Напишите имя и телефон ребёнка — менеджер {c.OrgDisplayName} свяжется с вами.\n\n" +
        BuildOperatorBlock(c);

    private static string DefaultWelcome(MaxBotComplianceInfo c)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Здравствуйте! Это официальный бот {c.OrgDisplayName}.");
        sb.AppendLine();
        sb.AppendLine("Здесь вы можете:");
        sb.AppendLine("• смотреть расписание на неделю;");
        sb.AppendLine("• узнать направления и стоимость;");
        sb.AppendLine("• получать напоминания о занятиях;");
        sb.AppendLine("• задать вопрос менеджеру.");
        sb.AppendLine();
        sb.AppendLine("Продолжая диалог, вы соглашаетесь на обработку данных для связи с вами. Подробнее: /privacy");
        sb.AppendLine();
        sb.AppendLine("Команды: /help");
        return sb.ToString().TrimEnd();
    }

    private static string BuildOperatorBlock(MaxBotComplianceInfo c)
    {
        var parts = new StringBuilder("Поддержка: ");
        var has = false;
        if (!string.IsNullOrWhiteSpace(c.SupportPhone))
        {
            parts.Append(c.SupportPhone.Trim());
            has = true;
        }
        if (!string.IsNullOrWhiteSpace(c.SupportEmail))
        {
            if (has) parts.Append(", ");
            parts.Append(c.SupportEmail.Trim());
            has = true;
        }
        return has ? parts.ToString() : "обратитесь к администратору центра.";
    }

    private static bool IsCommand(string lower, params string[] commands)
    {
        foreach (var cmd in commands)
        {
            if (lower == cmd || lower.StartsWith(cmd + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
