#nullable enable
using System.Windows;

namespace VKR.Utilities;

/// <summary>
/// Сворачивание окон без системной рамки (WindowStyle=None).
/// Перед Minimize временно включается стандартная рамка, иначе окно
/// может исчезнуть с панели задач и не восстановиться.
/// </summary>
public static class WindowMinimize
{
    public static void Attach(Window window)
    {
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized)
                window.WindowStyle = WindowStyle.None;
        };
    }

    public static void Minimize(Window window)
    {
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.WindowState = WindowState.Minimized;
    }
}
