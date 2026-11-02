#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VKR.Services;

public sealed class MaxBotPollEventArgs : EventArgs
{
    public MaxBotPollEventArgs(int processedUpdates, int totalUnread)
    {
        ProcessedUpdates = processedUpdates;
        TotalUnread = totalUnread;
    }

    public int ProcessedUpdates { get; }
    public int TotalUnread { get; }
}

/// <summary>
/// Фоновый long polling MAX — один цикл на всё приложение (без наложений).
/// </summary>
public sealed class MaxBotPollingService : IDisposable
{
    private readonly MaxBotRepository _repository;
    private readonly MaxBotService _botService;
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private bool _started;

    public MaxBotPollingService(MaxBotRepository repository, MaxBotService botService)
    {
        _repository = repository;
        _botService = botService;
    }

    public event EventHandler<MaxBotPollEventArgs>? PollCompleted;
    public event EventHandler<int>? UnreadCountChanged;

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public async Task<int> RefreshUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var count = await _repository.GetTotalUnreadCountAsync(cancellationToken);
        UnreadCountChanged?.Invoke(this, count);
        return count;
    }

    public async Task<int> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _pollGate.WaitAsync(0, cancellationToken))
            return -1;

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            if (settings is not { IsConfigured: true })
            {
                await RefreshUnreadCountAsync(cancellationToken);
                return 0;
            }

            var processed = await _botService.PollAndProcessUpdatesAsync(cancellationToken);
            var unread = await RefreshUnreadCountAsync(cancellationToken);
            PollCompleted?.Invoke(this, new MaxBotPollEventArgs(processed, unread));
            return processed;
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public void Dispose() => Stop();
}
