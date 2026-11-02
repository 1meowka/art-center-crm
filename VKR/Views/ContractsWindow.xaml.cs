#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class ContractsWindow : Window
{
    private readonly ContractRepository _repository = new();
    private readonly ObservableCollection<ContractListItem> _items = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));
    private bool _uiReady;

    public ContractsWindow()
    {
        InitializeComponent();
        ContractsGrid.ItemsSource = _items;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += ContractsWindow_Loaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async void ContractsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady) return;
        _uiReady = true;
        DeleteContractButton.Visibility = AuthorizationService.CanDeleteRecords(UserSession.CurrentUser)
            ? Visibility.Visible
            : Visibility.Collapsed;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();
            _items.Clear();

            var status = GetSelectedStatus();
            var rows = await _repository.GetContractsAsync(
                SearchBox?.Text,
                status,
                DateFromPicker?.SelectedDate,
                DateToPicker?.SelectedDate);

            foreach (var r in rows)
                _items.Add(r);

            if (CountText != null)
                CountText.Text = $"найдено: {_items.Count}";

            UpdateSelectedPanel();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private string? GetSelectedStatus()
    {
        if (StatusCombo?.SelectedItem is ComboBoxItem cbi)
        {
            var v = cbi.Content?.ToString();
            if (string.IsNullOrWhiteSpace(v) || v == "все") return null;
            return v;
        }
        return null;
    }

    // -------- Filters --------
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        _searchDebouncer.Schedule();
    }

    private async void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    // -------- Selection --------
    private void ContractsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedPanel();
    }

    private void UpdateSelectedPanel()
    {
        if (ContractsGrid.SelectedItem is ContractListItem c)
        {
            SelectedContractTitle.Text = $"№ {c.ContractNumber} — {c.StudentFullNameText}";
            SelectedContractInfo.Text =
                $"Заказчик: {c.RepresentativeFullNameText}   •   Обучающийся: {c.StudentFullNameText}   •   " +
                $"Сумма: {c.TotalAmountText}   •   Оплачено: {c.PaidAmountText}   •   Остаток: {c.OutstandingText}   •   Статус: {c.Status}";
        }
        else
        {
            SelectedContractTitle.Text = "Договор не выбран";
            SelectedContractInfo.Text = "Выберите строку, чтобы увидеть детали и действия.";
        }
    }

    // -------- Actions --------
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async void AddContract_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContractEditDialog { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void EditContract_Click(object sender, RoutedEventArgs e)
    {
        if (ContractsGrid.SelectedItem is not ContractListItem c)
        {
            MessageBox.Show("Сначала выберите договор.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new ContractEditDialog(c.IdContract) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void ContractsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditContract_Click(sender, e);
        await Task.CompletedTask;
    }

    private async void ArchiveContract_Click(object sender, RoutedEventArgs e)
    {
        if (ContractsGrid.SelectedItem is not ContractListItem c) return;

        var ok = MessageBox.Show(
            $"Перевести договор № {c.ContractNumber} в архив?",
            "Архивирование", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            await _repository.ArchiveAsync(c.IdContract);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось архивировать договор."));
        }
    }

    private async void DeleteContract_Click(object sender, RoutedEventArgs e)
    {

        if (ContractsGrid.SelectedItem is not ContractListItem c) return;

        var ok = MessageBox.Show(
            $"Удалить договор № {c.ContractNumber} безвозвратно?\nЭто также удалит все связанные оплаты и записи.",
            "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteAsync(c.IdContract);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось удалить договор."));
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ClearError()
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
