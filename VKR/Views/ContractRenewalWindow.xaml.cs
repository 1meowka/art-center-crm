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

public partial class ContractRenewalWindow : Window
{
    private readonly ContractRenewalRepository _repository = AppServices.GetRequiredService<ContractRenewalRepository>();
    private readonly ObservableCollection<ContractRenewalDueItem> _items = new();

    public ContractRenewalWindow()
    {
        InitializeComponent();
        DueGrid.ItemsSource = _items;
        DueGrid.SelectionChanged += (_, _) => UpdateButtons();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private void UpdateButtons()
    {
        var hasSelection = DueGrid.SelectedItem is ContractRenewalDueItem;
        SnoozeButton.IsEnabled = hasSelection;
        CompleteButton.IsEnabled = hasSelection;
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();
            NotificationSchema.Ensure();
            var items = await _repository.GetDueRemindersAsync();
            _items.Clear();
            foreach (var item in items)
                _items.Add(item);
            CountText.Text = $"Ожидают обработки: {_items.Count}";
            UpdateButtons();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить задачи: " + ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void StatsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new NotificationStatsWindow(NotificationStatsSection.Renewal) { Owner = this };
        win.ShowDialog();
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e) => OpenCompleteDialog();

    private void DueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenCompleteDialog();

    private void DueGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || StudentCardOpener.ShouldIgnoreGridClick(e.OriginalSource as DependencyObject))
            return;

        if (DueGrid.SelectedItem is ContractRenewalDueItem item && item.IdStudent > 0)
            StudentCardOpener.Open(this, item.IdStudent);
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DueGrid.SelectedItem is not ContractRenewalDueItem item) return;

        var dialog = new NotificationSnoozeDialog(
            item.TaskKey,
            $"{item.StudentName} — договор {item.ContractNumber}, окончание {item.EndDateText}")
        { Owner = this };

        if (dialog.ShowDialog() == true)
            _ = ReloadAsync();
    }

    private void OpenCompleteDialog()
    {
        if (DueGrid.SelectedItem is not ContractRenewalDueItem item) return;

        var dialog = new ContractRenewalDialog(item) { Owner = this };
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
