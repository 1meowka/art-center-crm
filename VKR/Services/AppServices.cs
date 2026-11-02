#nullable enable
using Microsoft.Extensions.DependencyInjection;

namespace VKR.Services;

public static class AppServices
{
    private static ServiceProvider? _provider;

    public static void Initialize()
    {
        if (_provider != null)
            return;

        var services = new ServiceCollection();
        services.AddSingleton<AuthService>();
        services.AddSingleton<LeadRepository>();
        services.AddSingleton<TrialLessonRepository>();
        services.AddSingleton<StudentRepository>();
        services.AddSingleton<DashboardRepository>();
        services.AddSingleton<NotificationsRepository>();
        services.AddSingleton<StudentSatisfactionRepository>();
        services.AddSingleton<NotificationSnoozeRepository>();
        services.AddSingleton<ContractRenewalRepository>();
        services.AddSingleton<ContractRepository>();
        services.AddSingleton<PaymentRepository>();
        services.AddSingleton<ExpenseRepository>();
        services.AddSingleton<TeachersRepository>();
        services.AddSingleton<UserRepository>();
        services.AddSingleton<MaxBotRepository>();
        services.AddSingleton<MaxBotContentService>();
        services.AddSingleton<MaxBotService>();
        services.AddSingleton<MaxBotPollingService>();
        services.AddSingleton<PpoReportRepository>();
        services.AddSingleton<PpoReportExporter>();
        services.AddSingleton<VpReportRepository>();
        services.AddSingleton<VpReportExporter>();
        services.AddSingleton<DirectionsYearReportRepository>();
        services.AddSingleton<DirectionsYearReportExporter>();
        services.AddSingleton<AdminReportRepository>();
        services.AddSingleton<AdminReportExporter>();
        services.AddSingleton<YearConsolidatedReportExporter>();
        services.AddSingleton<AiAnalyticsService>();

        _provider = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_provider == null)
            Initialize();

        return _provider!.GetRequiredService<T>();
    }
}
