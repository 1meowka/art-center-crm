#nullable enable
using Microsoft.Extensions.Hosting;
using VKR.Services;

namespace MaxBotWorker;

public sealed class MaxBotWorkerService : IHostedService
{
    private readonly MaxBotPollingService _poller;
    private readonly MaxBotService _botService;
    private readonly MaxBotRepository _repository;

    public MaxBotWorkerService(
        MaxBotPollingService poller,
        MaxBotService botService,
        MaxBotRepository repository)
    {
        _poller = poller;
        _botService = botService;
        _repository = repository;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            if (settings is { IsConfigured: true })
                await _botService.SyncBotCommandsAsync(settings.BotToken, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Не удалось обновить меню команд MAX: " + ex.Message);
        }

        _poller.Start();
        Console.WriteLine("MAX Bot Worker: опрос MAX запущен. Закройте окно или нажмите Ctrl+C для остановки.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _poller.Stop();
        Console.WriteLine("MAX Bot Worker: остановлен.");
        return Task.CompletedTask;
    }
}
