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

public partial class StudentSatisfactionWindow : Window
{
    private readonly StudentSatisfactionRepository _repository = AppServices.GetRequiredService<StudentSatisfactionRepository>();
    private readonly ObservableCollection<SatisfactionDueItem> _items = new();

    public StudentSatisfactionWindow()
    {
        InitializeComponent();
        DueGrid.ItemsSource = _items;
        DueGrid.SelectionChanged += (_, _) =>
        {
            var hasSelection = DueGrid.SelectedItem is SatisfactionDueItem;
            SurveyButton.IsEnabled = hasSelection;
            SnoozeButton.IsEnabled = hasSelection;
        };
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();
            NotificationSchema.Ensure();
            var items = await _repository.GetDueSurveysAsync();
            _items.Clear();
            foreach (var item in items)
                _items.Add(item);
            CountText.Text = $"Ожидают опроса: {_items.Count}";
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить задачи: " + ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void StatsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new NotificationStatsWindow(NotificationStatsSection.Satisfaction) { Owner = this };
        win.ShowDialog();
    }

    private void SurveyButton_Click(object sender, RoutedEventArgs e) => OpenSurveyDialog();

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DueGrid.SelectedItem is not SatisfactionDueItem item) return;

        var dialog = new NotificationSnoozeDialog(
            item.TaskKey,
            $"{item.StudentName} — опрос за {item.PeriodText.ToLower()}, договор {item.ContractNumber}")
        { Owner = this };

        if (dialog.ShowDialog() == true)
            _ = ReloadAsync();
    }

    private void DueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSurveyDialog();

    private void DueGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || StudentCardOpener.ShouldIgnoreGridClick(e.OriginalSource as DependencyObject))
            return;

        if (DueGrid.SelectedItem is SatisfactionDueItem item && item.IdStudent > 0)
            StudentCardOpener.Open(this, item.IdStudent);
    }

    private void OpenSurveyDialog()
    {
        if (DueGrid.SelectedItem is not SatisfactionDueItem item) return;

        var dialog = new SatisfactionSurveyDialog(item) { Owner = this };
        if (dialog.ShowDialog() == true)
            _ = ReloadAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

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
