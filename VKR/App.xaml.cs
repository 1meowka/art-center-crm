#nullable enable
using System;
using System.Windows;
using Microsoft.Extensions.Configuration;
using VKR.Services;

namespace VKR;

public partial class App : Application
{
    public static IConfiguration Configuration { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var basePath = AppContext.BaseDirectory;

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

#if DEBUG
        configurationBuilder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
#endif

        var configuration = configurationBuilder
            .AddUserSecrets<App>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        Configuration = configuration;

        var connectionString =
            configuration.GetConnectionString("Postgres")
            ?? configuration["VKR_POSTGRES_CONNECTION"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            MessageBox.Show(
                "Не задана строка подключения к PostgreSQL.\n\n" +
                "Для локальной разработки проверьте файл appsettings.Development.json\n" +
                "(Host, Database, Username, Password).\n\n" +
                "Для других сред используйте переменную окружения VKR_POSTGRES_CONNECTION\n" +
                "или User Secrets: ConnectionStrings:Postgres.",
                "Ошибка конфигурации",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Db.Initialize(connectionString);
        AppServices.Initialize();

        try
        {
            NotificationSchema.Ensure();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        try
        {
            AcademicSchema.Ensure();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        try
        {
            AiAnalyticsSchema.Ensure();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        try
        {
            FinanceSchema.Ensure();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        var showingUnhandledException = false;
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (showingUnhandledException)
                return;

            showingUnhandledException = true;
            try
            {
                MessageBox.Show(
                    Utilities.UserFacingErrors.GetMessage(args.Exception),
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Не даём обработчику ошибок уйти в бесконечную рекурсию.
            }
            finally
            {
                showingUnhandledException = false;
            }
        };

        base.OnStartup(e);
    }
}
