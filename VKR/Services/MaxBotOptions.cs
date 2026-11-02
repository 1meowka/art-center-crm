#nullable enable
using Microsoft.Extensions.Configuration;

namespace VKR.Services;

public static class MaxBotOptions
{
    /// <summary>
    /// true — опрос MAX только через MaxBotWorker (на ПК центра).
    /// false — программа сама слушает MAX, пока открыта (удобно для разработки).
    /// </summary>
    public static bool UseExternalWorker(IConfiguration configuration) =>
        bool.TryParse(configuration["MaxBot:UseExternalWorker"], out var enabled) && enabled;

    /// <summary>Базовый URL API MAX (с июня 2026 — platform-api2.max.ru).</summary>
    public static string ApiBaseUrl(IConfiguration configuration)
    {
        var fromConfig = configuration["MaxBot:ApiBaseUrl"]?.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(fromConfig)
            ? "https://platform-api2.max.ru"
            : fromConfig;
    }
}
