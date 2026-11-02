#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace VKR.Services;

/// <summary>
/// Автовыход при неактивности (защита рабочего места).
/// </summary>
public sealed class SessionIdleMonitor : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly Window _window;
    private readonly Action _onIdleTimeout;
    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _timeout;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public SessionIdleMonitor(Window window, Action onIdleTimeout, TimeSpan? timeout = null)
    {
        _window = window;
        _onIdleTimeout = onIdleTimeout;
        _timeout = timeout ?? DefaultTimeout;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += OnTick;

        _window.PreviewMouseMove += OnActivity;
        _window.PreviewKeyDown += OnActivity;
        _window.PreviewMouseDown += OnActivity;
        _window.Activated += (_, _) => RegisterActivity();

        _timer.Start();
    }

    private void OnActivity(object sender, InputEventArgs e) => RegisterActivity();

    private void RegisterActivity() => _lastActivityUtc = DateTime.UtcNow;

    private void OnTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastActivityUtc < _timeout)
            return;

        _timer.Stop();
        _onIdleTimeout();
    }

    public void Dispose()
    {
        _timer.Stop();
        _window.PreviewMouseMove -= OnActivity;
        _window.PreviewKeyDown -= OnActivity;
        _window.PreviewMouseDown -= OnActivity;
    }
}
