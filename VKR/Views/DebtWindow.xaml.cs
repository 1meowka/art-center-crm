#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class DebtWindow : Window
{
    private readonly ContractRepository _contracts = new();
    private readonly DebtExporter _exporter = new();
    private readonly ObservableCollection<DebtorItem> _debtors = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));

    private bool _uiReady;

    public DebtWindow()
    {
        InitializeComponent();
        DebtorsGrid.ItemsSource = _debtors;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += DebtWindow_Loaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async void DebtWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady) return;
        _uiReady = true;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();

            var summary = await _contracts.GetFinancialSummaryAsync();
            TotalDebtText.Text = summary.TotalDebtText;
            DebtorsCountText.Text = summary.DebtorsCount.ToString();

            _debtors.Clear();
            var filter = GetSelectedFilter();
            var rows = await _contracts.GetDebtorsAsync(SearchBox?.Text, GetSelectedSort(), filter);
            foreach (var row in rows)
                _debtors.Add(row);

            var filterHint = filter switch
            {
                DebtorDisplayFilter.AwaitingPayment => " (ожидают оплаты)",
                DebtorDisplayFilter.Overdue => " (просрочка)",
                DebtorDisplayFilter.Deferred => " (отсрочка)",
                _ => string.Empty
            };
            ListCountText.Text = $"Открытых задолженностей: {_debtors.Count}{filterHint}";
            EmptyText.Visibility = _debtors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private DebtorSortKey GetSelectedSort()
    {
        if (SortCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<DebtorSortKey>(tag, out var sortKey))
            return sortKey;

        return DebtorSortKey.OverdueDesc;
    }

    private DebtorDisplayFilter GetSelectedFilter()
    {
        if (FilterCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<DebtorDisplayFilter>(tag, out var filter))
            return filter;

        return DebtorDisplayFilter.All;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        var text = SearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _searchDebouncer.Schedule();
    }

    private async void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private DebtorItem? GetSelectedDebtor()
    {
        if (DebtorsGrid.SelectedItem is DebtorItem debtor)
            return debtor;

        ShowError("Выберите клиента из списка задолженностей.");
        return null;
    }

    private async void RepayDebt_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDebtor() is not { } debtor)
            return;

        var dlg = new PaymentEditDialog(debtor) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void RepayDebtFull_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDebtor() is not { } debtor)
            return;

        var dlg = new PaymentEditDialog(debtor) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void DebtorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DebtorsGrid.SelectedItem is not DebtorItem debtor)
            return;

        var dlg = new PaymentEditDialog(debtor) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void DeferDebt_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDebtor() is not { } debtor)
            return;

        var dlg = new DebtDeferDialog(debtor) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private void AddClientNote_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedDebtor() is not { } debtor)
            return;

        ClientNotesWindow.OpenForStudent(this, debtor.IdStudent, debtor.StudentFullName);
    }

    private async void ExportDebt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearError();
            var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = DebtExporter.SuggestFileName()
            };
            if (dialog.ShowDialog() != true)
                return;

            var rows = await _contracts.GetDebtorsAsync(SearchBox?.Text, GetSelectedSort(), GetSelectedFilter());
            _exporter.Export(rows, dialog.FileName);
            MessageBox.Show($"Выгрузка сохранена:\n{dialog.FileName}", "Готово",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось выгрузить задолженности."));
        }
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
