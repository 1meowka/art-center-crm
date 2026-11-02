#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class CalendarEventsWindow : Window
{
    private readonly CalendarEventRepository _repository = new();
    private readonly ObservableCollection<CalendarEventListItem> _events = new();

    public CalendarEventsWindow()
    {
        InitializeComponent();
        EventsGrid.ItemsSource = _events;

        FromDatePicker.SelectedDate = DateTime.Today.AddMonths(-1);
        ToDatePicker.SelectedDate = DateTime.Today.AddMonths(3);

        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _events.Clear();
            var from = FromDatePicker.SelectedDate ?? DateTime.Today.AddMonths(-1);
            var to = ToDatePicker.SelectedDate ?? DateTime.Today.AddMonths(3);
            var rows = await _repository.GetEventsAsync(from, to, OnlyActiveCheck.IsChecked == true);
            foreach (var row in rows)
                _events.Add(row);

            CountText.Text = $"всего: {_events.Count}";
            EmptyText.Visibility = _events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CalendarEventEditDialog(_repository, null) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void EventsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EventsGrid.SelectedItem is CalendarEventListItem item)
        {
            var dialog = new CalendarEventEditDialog(_repository, item.IdEvent) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
    }

    private async void Filter_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
}
