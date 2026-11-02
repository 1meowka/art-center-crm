#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VKR.Models;

namespace VKR.Services;

public sealed class AiAnalyticsService
{
    private readonly AiAnalyticsRepository _repository = new();

    public async Task<IReadOnlyList<ChurnPrediction>> GetChurnPredictionsAsync() =>
        await _repository.GetChurnPredictionsAsync();

    public async Task<IReadOnlyList<LeadConversionPrediction>> GetLeadConversionPredictionsAsync() =>
        await _repository.GetLeadConversionPredictionsAsync();

    public async Task<List<AiRecommendationItem>> GetRecommendationsAsync(
        IReadOnlyList<ChurnPrediction>? churnPredictions = null,
        IReadOnlyList<LeadConversionPrediction>? conversionPredictions = null)
    {
        churnPredictions ??= await GetChurnPredictionsAsync();
        conversionPredictions ??= await GetLeadConversionPredictionsAsync();
        var completionMap = await _repository.GetRecommendationCompletionMapAsync();

        var recommendations = new List<AiRecommendationItem>();

        foreach (var pred in churnPredictions.Where(p => !string.IsNullOrWhiteSpace(p.RecommendedAction)))
        {
            if (!ExpertRecommendationEngine.IsActionable(pred.RecommendedAction))
                continue;

            var item = new AiRecommendationItem
            {
                IdRecommendation = pred.IdChurnPrediction,
                EntityId = pred.IdStudent,
                Title = pred.RecommendedAction,
                Description = $"{pred.StudentFullName} ({pred.StudentPhone}) — вероятность оттока {pred.ChurnProbability:P0}",
                TriggeredSigns = ExtractSigns(pred.ChurnFactors),
                RuleCode = ExtractRuleCode(pred.ChurnFactors),
                Type = "отток",
                Priority = MapChurnPriority(pred.Priority),
                EntityName = pred.StudentFullName,
                EntityType = "student",
                CreatedAt = pred.DateCalculated,
                IsActionable = true
            };
            item.IsCompleted = IsCompleted(completionMap, item);
            recommendations.Add(item);
        }

        foreach (var pred in conversionPredictions.Where(p => !string.IsNullOrWhiteSpace(p.RecommendedAction)))
        {
            if (!ExpertRecommendationEngine.IsActionable(pred.RecommendedAction))
                continue;

            var item = new AiRecommendationItem
            {
                IdRecommendation = pred.IdPrediction,
                EntityId = pred.IdLead,
                Title = pred.RecommendedAction,
                Description = $"{pred.LeadFullName} ({pred.LeadPhone}) — вероятность конверсии {pred.ConversionProbability:P0}",
                TriggeredSigns = ExtractSigns(pred.ConversionFactors),
                RuleCode = ExtractRuleCode(pred.ConversionFactors),
                Type = "конверсия",
                Priority = pred.ProbabilityLevel,
                EntityName = pred.LeadFullName,
                EntityType = "lead",
                CreatedAt = pred.DateCalculated,
                IsActionable = true
            };
            item.IsCompleted = IsCompleted(completionMap, item);
            recommendations.Add(item);
        }

        var priorityOrder = new Dictionary<string, int> { { "высокий", 1 }, { "средний", 2 }, { "низкий", 3 } };
        return recommendations
            .Where(r => !r.IsCompleted)
            .OrderBy(r => priorityOrder.GetValueOrDefault(r.Priority, 3))
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<AiRecommendationHistoryItem>> GetRecommendationHistoryAsync() =>
        await _repository.GetRecommendationHistoryAsync();

    public async Task SetRecommendationCompletedAsync(AiRecommendationItem item, bool isCompleted)
    {
        if (string.IsNullOrWhiteSpace(item.EntityType) || string.IsNullOrWhiteSpace(item.Title))
            return;

        var user = UserSession.CurrentUser;
        await _repository.SetRecommendationCompletedAsync(
            item.EntityType,
            item.EntityId,
            item.Title,
            isCompleted,
            item.RuleCode,
            item.Type,
            item.EntityName,
            item.TriggeredSigns,
            isCompleted ? user?.IdUser : null,
            isCompleted ? user?.FullName : null);

        item.IsCompleted = isCompleted;
    }

    public async Task<IReadOnlyList<DirectionAnalyticsItem>> GetDirectionAnalyticsAsync() =>
        await _repository.GetDirectionAnalyticsAsync();

    public async Task<AiAnalyticsSummary> GetSummaryAsync() =>
        await _repository.GetAnalyticsSummaryAsync();

    public async Task RecalculateAnalyticsAsync()
    {
        await _repository.EnsureRecommendationStatusTableAsync();
        await _repository.CalculateAndSaveChurnPredictionsAsync();
        await _repository.CalculateAndSaveLeadConversionPredictionsAsync();
    }

    private static string MapChurnPriority(int priority) => priority switch
    {
        1 => "высокий",
        2 => "высокий",
        3 => "средний",
        _ => "низкий"
    };

    private static bool IsCompleted(
        Dictionary<(string EntityType, int EntityId, string Text), bool> map,
        AiRecommendationItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            return false;

        return map.TryGetValue((item.EntityType ?? "", item.EntityId, item.Title), out var done) && done;
    }

    private static string? ExtractRuleCode(string? factors)
    {
        if (string.IsNullOrWhiteSpace(factors))
            return null;

        const string prefix = "Правило: ";
        var idx = factors.LastIndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        return factors[(idx + prefix.Length)..].Trim();
    }

    private static string? ExtractSigns(string? factors)
    {
        if (string.IsNullOrWhiteSpace(factors))
            return null;

        var parts = factors.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("Правило:", StringComparison.Ordinal))
            .ToList();

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
