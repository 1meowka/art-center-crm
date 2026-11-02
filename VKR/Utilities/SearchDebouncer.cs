#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace VKR.Utilities;

public sealed class SearchDebouncer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;
    private int _generation;

    public SearchDebouncer(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += OnTimerTick;
    }

    public event Func<CancellationToken, Task>? Debounced;

    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _cts?.Cancel();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var generation = Interlocked.Increment(ref _generation);

        if (Debounced == null)
            return;

        try
        {
            await Debounced(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                Debug.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
