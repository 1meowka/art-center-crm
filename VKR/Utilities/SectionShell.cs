#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VKR.Utilities;

/// <summary>
/// Показывает раздел внутри главного окна, без второго maximized Window.
/// «Назад» (Close раздела) возвращает на главную.
/// </summary>
public sealed class SectionShell
{
    private readonly Window _main;
    private readonly FrameworkElement _home;
    private readonly ContentControl _host;
    private Window? _hosted;
    private bool _allowHostedClose;

    public SectionShell(Window main, FrameworkElement home, ContentControl host)
    {
        _main = main;
        _home = home;
        _host = host;
    }

    public bool HasSection => _hosted != null;

    public event Action? ReturnedHome;

    public void Show(Window section)
    {
        DismissHosted();

        // WPF запрещает Show(), если ShowActivated=false и WindowState=Maximized
        // (так задано почти у всех разделов в XAML). Сначала Normal.
        section.WindowState = WindowState.Normal;
        section.Owner = _main;
        section.ShowInTaskbar = false;
        section.ShowActivated = false;
        section.WindowStartupLocation = WindowStartupLocation.Manual;
        section.Left = -20000;
        section.Top = -20000;
        section.Width = 1024;
        section.Height = 768;
        section.Opacity = 0;
        section.Closing += Hosted_Closing;

        try
        {
            section.Show();
        }
        catch
        {
            section.Closing -= Hosted_Closing;
            try { section.Close(); } catch { /* ignore */ }
            throw;
        }

        if (section.Content is not FrameworkElement content)
        {
            section.Closing -= Hosted_Closing;
            section.Close();
            return;
        }

        CopyResources(section, content);
        section.Content = null;
        _host.Content = content;
        section.Hide();

        _hosted = section;
        _home.Visibility = Visibility.Collapsed;
        _host.Visibility = Visibility.Visible;
        _main.Activate();
    }

    public void GoHome()
    {
        if (_hosted == null)
            return;

        DismissHosted();
        _host.Content = null;
        _host.Visibility = Visibility.Collapsed;
        _home.Visibility = Visibility.Visible;
        ReturnedHome?.Invoke();
    }

    public void Dispose() => DismissHosted();

    private void Hosted_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowHostedClose)
            return;

        e.Cancel = true;
        GoHome();
    }

    private void DismissHosted()
    {
        if (_hosted == null)
            return;

        _hosted.Closing -= Hosted_Closing;
        _host.Content = null;

        _allowHostedClose = true;
        try
        {
            _hosted.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _allowHostedClose = false;
            _hosted = null;
        }
    }

    private static void CopyResources(Window from, FrameworkElement to)
    {
        foreach (var dict in from.Resources.MergedDictionaries.ToList())
        {
            if (!to.Resources.MergedDictionaries.Contains(dict))
                to.Resources.MergedDictionaries.Add(dict);
        }

        foreach (var key in from.Resources.Keys.Cast<object>().ToList())
        {
            if (key == null || to.Resources.Contains(key))
                continue;
            try
            {
                to.Resources.Add(key, from.Resources[key]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }
}
