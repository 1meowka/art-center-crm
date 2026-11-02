#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class SalesWindow : Window
{
    private readonly SaleRepository _repository = new();
    private readonly ObservableCollection<SaleListItem> _items = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));
    private bool _uiReady;
    private bool _suppressFilterChange;

    public SalesWindow()
    {
        InitializeComponent();
        SalesGrid.ItemsSource = _items;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += SalesWindow_Loaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async void SalesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady) return;
        _uiReady = true;
        DeleteSaleButton.Visibility = AuthorizationService.CanDeleteRecords(UserSession.CurrentUser)
            ? Visibility.Visible
            : Visibility.Collapsed;

        await LoadYearFilterAsync();
        await ReloadAsync();
    }

    private async Task LoadYearFilterAsync()
    {
        _suppressFilterChange = true;
        YearCombo.Items.Clear();
        YearCombo.Items.Add(new ComboBoxItem { Content = "все годы", Tag = null });

        var years = await _repository.GetSaleYearsAsync();
        foreach (var year in years)
            YearCombo.Items.Add(new ComboBoxItem { Content = year.ToString(), Tag = year });

        YearCombo.SelectedIndex = 0;
        _suppressFilterChange = false;
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();
            _items.Clear();

            var rows = await _repository.GetSalesAsync(
                SearchBox?.Text,
                DateFromPicker?.SelectedDate,
                DateToPicker?.SelectedDate,
                idStudent: null,
                year: GetSelectedYear(),
                saleTypeFilter: GetSelectedSaleTypeFilter(),
                sortKey: GetSelectedSortKey());

            decimal total = 0;
            foreach (var r in rows)
            {
                _items.Add(r);
                total += r.TotalAmount;
            }

            SalesCountText.Text = $"Всего продаж: {_items.Count}";
            TotalRevenueText.Text = total.ToString("N2") + " ₽";
            TotalCountText.Text = _items.Count.ToString();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить продажи: " + ex.Message);
        }
    }

    private SaleSortKey GetSelectedSortKey()
    {
        if (SortCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "DateAsc" => SaleSortKey.DateAsc,
                "AmountDesc" => SaleSortKey.AmountDesc,
                "AmountAsc" => SaleSortKey.AmountAsc,
                "ItemAsc" => SaleSortKey.ItemAsc,
                "ItemDesc" => SaleSortKey.ItemDesc,
                "StudentAsc" => SaleSortKey.StudentAsc,
                "StudentDesc" => SaleSortKey.StudentDesc,
                _ => SaleSortKey.DateDesc
            };
        }

        return SaleSortKey.DateDesc;
    }

    private int? GetSelectedYear()
    {
        if (YearCombo?.SelectedItem is ComboBoxItem item && item.Tag is int year)
            return year;

        return null;
    }

    private SaleTypeFilter GetSelectedSaleTypeFilter()
    {
        if (SaleTypeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "TrialOnly" => SaleTypeFilter.TrialOnly,
                "OtherOnly" => SaleTypeFilter.OtherOnly,
                _ => SaleTypeFilter.All
            };
        }

        return SaleTypeFilter.All;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        var text = SearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _searchDebouncer.Schedule();
    }

    private async void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void SortFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressFilterChange) return;
        await ReloadAsync();
    }

    private async void AddSale_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaleEditDialog { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void EditSale_Click(object sender, RoutedEventArgs e)
    {
        if (SalesGrid.SelectedItem is not SaleListItem item)
            return;

        var dlg = new SaleEditDialog(item.IdSale) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SalesGrid.SelectedItem is not SaleListItem item) return;
        var dlg = new SaleEditDialog(item.IdSale) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void DeleteSale_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanDeleteRecords(UserSession.CurrentUser))
        {
            ShowError("Удаление доступно только администратору и директору.");
            return;
        }

        if (SalesGrid.SelectedItem is not SaleListItem item)
            return;

        var ans = MessageBox.Show(
            $"Удалить продажу от {item.SaleDateText} на сумму {item.TotalAmountText}?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ans != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteAsync(item.IdSale);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось удалить: " + ex.Message);
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
