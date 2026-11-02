#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class BirthdaysNotificationsWindow : Window
{
    private readonly NotificationsRepository _repository = AppServices.GetRequiredService<NotificationsRepository>();
    private readonly ObservableCollection<BirthdayItem> _items = new();
    private bool _uiReady;

    public BirthdaysNotificationsWindow()
    {
        InitializeComponent();
        BirthdaysGrid.ItemsSource = _items;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        DatePicker.SelectedDate = DateTime.Today;
        _uiReady = true;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();
            var date = DatePicker.SelectedDate ?? DateTime.Today;
            var items = await _repository.GetBirthdaysOnDateAsync(date);

            _items.Clear();
            foreach (var i in items)
                _items.Add(i);

            CountText.Text = $"Именинников: {_items.Count}";
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить именинников: " + ex.Message);
        }
    }

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        DatePicker.SelectedDate = DateTime.Today;
        await ReloadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void GreetedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (sender is not CheckBox cb) return;
        if (cb.Tag is not BirthdayItem item) return;
        if (!cb.IsKeyboardFocusWithin && !cb.IsMouseOver) return;

        var newValue = cb.IsChecked == true;
        if (newValue == item.IsGreeted) return;

        var date = DatePicker.SelectedDate ?? DateTime.Today;

        try
        {
            ClearError();
            await _repository.SetGreetedAsync(
                item.IdStudent,
                date.Year,
                newValue,
                newValue ? DateTime.Today : null);

            item.IsGreeted = newValue;
            item.GreetingDate = newValue ? DateTime.Today : null;
        }
        catch (Exception ex)
        {
            cb.IsChecked = item.IsGreeted;
            ShowError("Не удалось сохранить отметку: " + ex.Message);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BirthdaysGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (StudentCardOpener.ShouldIgnoreGridClick(e.OriginalSource as DependencyObject))
            return;

        if (BirthdaysGrid.SelectedItem is BirthdayItem item && item.IdStudent > 0)
            StudentCardOpener.Open(this, item.IdStudent);
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
