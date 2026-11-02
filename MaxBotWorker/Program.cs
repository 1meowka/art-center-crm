#nullable enable
using MaxBotWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VKR.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddUserSecrets<MaxBotWorkerService>(optional: true)
    .AddEnvironmentVariables();

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["VKR_POSTGRES_CONNECTION"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Не задана строка подключения к PostgreSQL.\n" +
        "Укажите ConnectionStrings:Postgres в appsettings.json рядом с MaxBotWorker.exe\n" +
        "или переменную окружения VKR_POSTGRES_CONNECTION.");
    return 1;
}

Db.Initialize(connectionString);
AppServices.Initialize();

builder.Services.AddSingleton(_ => AppServices.GetRequiredService<MaxBotPollingService>());
builder.Services.AddSingleton(_ => AppServices.GetRequiredService<MaxBotService>());
builder.Services.AddSingleton(_ => AppServices.GetRequiredService<MaxBotRepository>());
builder.Services.AddHostedService<MaxBotWorkerService>();

var host = builder.Build();
await host.RunAsync();
return 0;
