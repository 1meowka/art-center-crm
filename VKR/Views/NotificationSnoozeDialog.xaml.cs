#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Services;

namespace VKR.Views;

public partial class NotificationSnoozeDialog : Window
{
    private readonly NotificationSnoozeRepository _snoozeRepository = AppServices.GetRequiredService<NotificationSnoozeRepository>();
    private readonly string _taskKey;

    public NotificationSnoozeDialog(string taskKey, string taskTitle)
    {
        InitializeComponent();
        _taskKey = taskKey;
        TaskTitleText.Text = taskTitle;
        UntilDatePicker.SelectedDate = DateTime.Today.AddDays(1);
    }

    private void Tomorrow_Click(object sender, RoutedEventArgs e) =>
        _ = SnoozeToAsync(DateTime.Today.AddDays(1));

    private void ThreeDays_Click(object sender, RoutedEventArgs e) =>
        _ = SnoozeToAsync(DateTime.Today.AddDays(3));

    private void Week_Click(object sender, RoutedEventArgs e) =>
        _ = SnoozeToAsync(DateTime.Today.AddDays(7));

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var date = UntilDatePicker.SelectedDate ?? DateTime.Today.AddDays(1);
        await SnoozeToAsync(date);
    }

    private async Task SnoozeToAsync(DateTime date)
    {
        try
        {
            ClearError();
            await _snoozeRepository.SnoozeAsync(_taskKey, date, UserSession.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
