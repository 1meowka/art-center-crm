#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VKR.Models;

namespace VKR.Services;

public sealed class KpiService
{
    private readonly KpiRepository _repository = new();

    public async Task<IReadOnlyList<KpiPlan>> GetAllPlansAsync()
    {
        return await _repository.GetAllPlansAsync();
    }

    public async Task<KpiPlan?> GetPlanByIdAsync(int idKpiPlan)
    {
        return await _repository.GetPlanByIdAsync(idKpiPlan);
    }

    public async Task<int> SavePlanAsync(KpiPlan plan)
    {
        return await _repository.SavePlanAsync(plan);
    }

    public async Task DeletePlanAsync(int idKpiPlan)
    {
        await _repository.DeletePlanAsync(idKpiPlan);
    }

    /// <summary>
    /// Рассчитать KPI (факт и результат) за период
    /// </summary>
    public async Task<List<KpiResult>> CalculateKpiAsync(DateTime periodStart, DateTime periodEnd)
    {
        var fact = await _repository.CalculateFactAsync(periodStart, periodEnd);
        var results = new List<KpiResult>();

        // Заявки
        results.Add(new KpiResult
        {
            IndicatorName = "Карточки гостей",
            FactValue = fact.FactLeads.ToString(),
            PlanValue = "-",
            CompletionPercent = 0,
            Status = "информационный"
        });

        // Новые студенты
        results.Add(new KpiResult
        {
            IndicatorName = "Новые обучающиеся",
            FactValue = fact.FactNewStudents.ToString(),
            PlanValue = "-",
            CompletionPercent = 0,
            Status = "информационный"
        });

        // Договоры
        results.Add(new KpiResult
        {
            IndicatorName = "Договоры",
            FactValue = fact.FactContracts.ToString(),
            PlanValue = "-",
            CompletionPercent = 0,
            Status = "информационный"
        });

        // Выручка
        results.Add(new KpiResult
        {
            IndicatorName = "Выручка",
            FactValue = $"{fact.FactRevenue:N2}",
            PlanValue = "-",
            CompletionPercent = 0,
            Status = "информационный"
        });

        // Посещаемость
        results.Add(new KpiResult
        {
            IndicatorName = "Посещаемость",
            FactValue = $"{fact.FactAttendancePercent:F1}%",
            PlanValue = "-",
            CompletionPercent = fact.FactAttendancePercent,
            Status = "информационный"
        });

        // Отток
        results.Add(new KpiResult
        {
            IndicatorName = "Отток обучающихся",
            FactValue = fact.FactChurn.ToString(),
            PlanValue = "-",
            CompletionPercent = 0,
            Status = "информационный"
        });

        return results;
    }

    /// <summary>
    /// Сравнить план и факт, получить результаты
    /// </summary>
    public async Task<List<KpiResult>> CalculateKpiWithPlanAsync(int idKpiPlan, DateTime periodStart, DateTime periodEnd)
    {
        var plan = await _repository.GetPlanByIdAsync(idKpiPlan);
        if (plan == null)
            throw new InvalidOperationException("План не найден.");

        var fact = await _repository.CalculateFactAsync(periodStart, periodEnd);
        var results = new List<KpiResult>();

        // Заявки: факт / план
        decimal leadsCompletion = plan.PlannedLeads > 0 ? (fact.FactLeads * 100m / plan.PlannedLeads) : 0;
        results.Add(new KpiResult
        {
            IndicatorName = "Карточки гостей",
            PlanValue = plan.PlannedLeads.ToString(),
            FactValue = fact.FactLeads.ToString(),
            CompletionPercent = leadsCompletion,
            Status = GetStatus(leadsCompletion)
        });

        // Новые студенты
        decimal studentsCompletion = plan.PlannedNewStudents > 0 ? (fact.FactNewStudents * 100m / plan.PlannedNewStudents) : 0;
        results.Add(new KpiResult
        {
            IndicatorName = "Новые обучающиеся",
            PlanValue = plan.PlannedNewStudents.ToString(),
            FactValue = fact.FactNewStudents.ToString(),
            CompletionPercent = studentsCompletion,
            Status = GetStatus(studentsCompletion)
        });

        // Договоры
        decimal contractsCompletion = plan.PlannedContracts > 0 ? (fact.FactContracts * 100m / plan.PlannedContracts) : 0;
        results.Add(new KpiResult
        {
            IndicatorName = "Договоры",
            PlanValue = plan.PlannedContracts.ToString(),
            FactValue = fact.FactContracts.ToString(),
            CompletionPercent = contractsCompletion,
            Status = GetStatus(contractsCompletion)
        });

        // Выручка
        decimal revenueCompletion = plan.PlannedRevenue > 0 ? (fact.FactRevenue * 100m / plan.PlannedRevenue) : 0;
        results.Add(new KpiResult
        {
            IndicatorName = "Выручка",
            PlanValue = $"{plan.PlannedRevenue:N2}",
            FactValue = $"{fact.FactRevenue:N2}",
            CompletionPercent = revenueCompletion,
            Status = GetStatus(revenueCompletion)
        });

        // Посещаемость
        results.Add(new KpiResult
        {
            IndicatorName = "Посещаемость",
            PlanValue = $"{plan.PlannedAttendancePercent:F1}%",
            FactValue = $"{fact.FactAttendancePercent:F1}%",
            CompletionPercent = fact.FactAttendancePercent >= plan.PlannedAttendancePercent ? 100 : (fact.FactAttendancePercent / plan.PlannedAttendancePercent * 100),
            Status = fact.FactAttendancePercent >= plan.PlannedAttendancePercent ? "выполнено" : "не выполнено"
        });

        // Отток (статус наоборот!)
        results.Add(new KpiResult
        {
            IndicatorName = "Отток обучающихся",
            PlanValue = plan.PlannedMaxChurn.ToString(),
            FactValue = fact.FactChurn.ToString(),
            CompletionPercent = 0,
            Status = fact.FactChurn <= plan.PlannedMaxChurn ? "в норме" : "превышен",
            IsSpecial = true
        });

        // Конверсия заявок
        results.Add(new KpiResult
        {
            IndicatorName = "Конверсия заявок",
            PlanValue = "100%",
            FactValue = $"{fact.FactConversionPercent:F1}%",
            CompletionPercent = fact.FactConversionPercent,
            Status = GetStatus(fact.FactConversionPercent)
        });

        return results;
    }

    private static string GetStatus(decimal completionPercent)
    {
        return completionPercent switch
        {
            >= 100 => "выполнено",
            >= 70 => "частично",
            _ => "не выполнено"
        };
    }
}
