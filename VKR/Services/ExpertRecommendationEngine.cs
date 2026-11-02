#nullable enable
using System;
using System.Collections.Generic;

namespace VKR.Services;

/// <summary>
/// Экспертная система: признаки клиента → сработавшее правило → рекомендация администратору.
/// </summary>
public static class ExpertRecommendationEngine
{
    public const string ChurnStandardAction =
        "Продолжить стандартное сопровождение клиента";

    public const string LeadLowPriorityAction =
        "Отправить информационное сообщение или перенести заявку в отложенную обработку";

    public static bool IsActionable(string? recommendation) =>
        !string.IsNullOrWhiteSpace(recommendation)
        && recommendation != ChurnStandardAction
        && recommendation != LeadLowPriorityAction;

    public static ExpertRecommendation EvaluateChurn(
        string riskLevel,
        StudentChurnFeatures features)
    {
        var signs = new List<string>();

        if (features.HasNoAttendanceData)
            signs.Add("Нет записей посещаемости за 30 дней");
        if (features.AttendanceRate < 0.70m && features.AttendanceRecordsCount > 0)
            signs.Add($"Посещаемость снижена: {features.AttendanceRate:P0}");
        if (features.DebtAmount > 0)
            signs.Add($"Задолженность: {features.DebtAmount:N0} ₽");
        if (features.MaxOverdueDays >= 7)
            signs.Add($"Просрочка оплаты: {features.MaxOverdueDays} дн.");
        if (features.ContractDaysLeft.HasValue && features.ContractDaysLeft.Value <= 14)
            signs.Add(features.ContractDaysLeft.Value <= 0
                ? "Срок договора истёк"
                : $"Договор заканчивается через {features.ContractDaysLeft.Value} дн.");
        if (features.HasWarningEvent)
            signs.Add("Событие приостановки, ухода или снижения активности");
        if (features.DaysSinceLastContact.HasValue && features.DaysSinceLastContact.Value > 14)
            signs.Add($"Последний контакт {features.DaysSinceLastContact.Value} дн. назад");
        else if (!features.DaysSinceLastContact.HasValue && features.DaysSinceArrival > 14)
            signs.Add("Контакт с клиентом не зафиксирован");

        if (riskLevel == "высокий" && features.DebtAmount > 0)
            return Rule("CHURN-H-DEBT", signs,
                "Напомнить об оплате и предложить удобный способ погашения задолженности", 1);

        if (riskLevel == "высокий" && features.AttendanceRate < 0.70m && features.AttendanceRecordsCount > 0)
            return Rule("CHURN-H-ATT", signs,
                "Связаться с представителем или обучающимся и уточнить причину пропусков", 1);

        if (riskLevel == "высокий" && (!features.DaysSinceLastContact.HasValue || features.DaysSinceLastContact > 14))
            return Rule("CHURN-H-CONTACT", signs,
                "Срочно связаться с клиентом — давно не было контакта", 1);

        if (riskLevel == "высокий")
            return Rule("CHURN-H-GENERAL", signs,
                "Связаться с клиентом в первую очередь и зафиксировать причину риска", 1);

        if (riskLevel == "средний" && features.ContractDaysLeft.HasValue && features.ContractDaysLeft.Value <= 14)
            return Rule("CHURN-M-CONTRACT", signs,
                "Предложить продление договора или подобрать новый абонемент", 3);

        if (riskLevel == "средний" && features.MissedCount >= 3)
            return Rule("CHURN-M-SCHEDULE", signs,
                "Предложить другое расписание, группу или преподавателя", 3);

        if (riskLevel == "средний" && (!features.DaysSinceLastContact.HasValue || features.DaysSinceLastContact > 21))
            return Rule("CHURN-M-CONTACT", signs,
                "Связаться с клиентом и уточнить удовлетворённость занятиями", 3);

        if (riskLevel == "средний")
            return Rule("CHURN-M-MONITOR", signs,
                "Поставить клиента на контроль и уточнить причины снижения активности", 3);

        return Rule("CHURN-L-STANDARD", signs, ChurnStandardAction, 5, actionable: false);
    }

    public static ExpertRecommendation EvaluateLead(
        string level,
        LeadConversionFeatures features)
    {
        var signs = new List<string>();

        if (features.HasSource)
            signs.Add($"Источник: конверсия {features.SourceConversionRate:P0}");
        else
            signs.Add("Источник заявки не указан");

        signs.Add(features.DirectionActive
            ? $"Направление активно: конверсия {features.DirectionConversionRate:P0}"
            : "Направление неактивно");

        if (features.HasTrial)
            signs.Add($"Пробное занятие: {features.TrialResultText}");
        else
            signs.Add("Пробное занятие не назначено");

        if (features.DaysToFirstContact.HasValue)
            signs.Add($"Первый контакт через {features.DaysToFirstContact.Value} дн.");
        else
            signs.Add("Контакт не зафиксирован");

        if (!string.IsNullOrWhiteSpace(features.LastContactResult))
            signs.Add($"Последний контакт: {features.LastContactResult}");

        if (features.TrialDoneNoContract)
            return Rule("LEAD-TRIAL-FOLLOWUP", signs,
                "Уточнить причину отказа и предложить альтернативное направление", 1);

        if (features.DaysSinceCreated > 7 && (features.Status == "новая" || features.Status == "подтверждена"))
            return Rule("LEAD-STALE", signs,
                "Назначить повторный контакт и проверить актуальность обращения", 2);

        return level switch
        {
            "высокий" => Rule("LEAD-H-PRIORITY", signs,
                "Приоритетно связаться с клиентом и подготовить договор", 1),
            "средний" => Rule("LEAD-M-FOLLOWUP", signs,
                "Повторно обработать заявку и уточнить заинтересованность клиента", 3),
            _ => Rule("LEAD-L-INFO", signs, LeadLowPriorityAction, 5, actionable: false)
        };
    }

    private static ExpertRecommendation Rule(
        string code,
        List<string> signs,
        string action,
        int priority,
        bool? actionable = null) =>
        new(code, action, signs, priority, actionable ?? IsActionable(action));
}

public sealed class ExpertRecommendation
{
    public ExpertRecommendation(string ruleCode, string action, IReadOnlyList<string> signs, int priority, bool isActionable)
    {
        RuleCode = ruleCode;
        Action = action;
        Signs = signs;
        Priority = priority;
        IsActionable = isActionable;
    }

    public string RuleCode { get; }
    public string Action { get; }
    public IReadOnlyList<string> Signs { get; }
    public int Priority { get; }
    public bool IsActionable { get; }
}

public sealed class StudentChurnFeatures
{
    public decimal AttendanceRate { get; init; }
    public int AttendanceRecordsCount { get; init; }
    public bool HasNoAttendanceData { get; init; }
    public int MissedCount { get; init; }
    public decimal DebtAmount { get; init; }
    public int MaxOverdueDays { get; init; }
    public int? ContractDaysLeft { get; init; }
    public bool HasWarningEvent { get; init; }
    public int? DaysSinceLastContact { get; init; }
    public int DaysSinceArrival { get; init; }
}

public sealed class LeadConversionFeatures
{
    public bool HasSource { get; init; }
    public decimal SourceConversionRate { get; init; }
    public bool DirectionActive { get; init; }
    public decimal DirectionConversionRate { get; init; }
    public bool HasTrial { get; init; }
    public string TrialResultText { get; init; } = "";
    public int? DaysToFirstContact { get; init; }
    public string? LastContactResult { get; init; }
    public bool TrialDoneNoContract { get; init; }
    public int DaysSinceCreated { get; init; }
    public string Status { get; init; } = "";
}
