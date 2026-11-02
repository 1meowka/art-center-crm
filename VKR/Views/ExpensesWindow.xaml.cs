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

public partial class ExpensesWindow : Window
{
    private readonly ExpenseRepository _repository  = new();
    private readonly PaymentRepository _paymentRepo = new();
    private readonly SaleRepository    _saleRepo    = new();

    private readonly ObservableCollection<ExpenseListItem>   _items     = new();
    private readonly ObservableCollection<ExpenseByCategory> _byCategory = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));

    private bool _uiReady;

    public ExpensesWindow()
    {
        InitializeComponent();
        ExpensesGrid.ItemsSource     = _items;
        CategoryStatsGrid.ItemsSource = _byCategory;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += ExpensesWindow_Loaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async void ExpensesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Заполняем комбобокс категорий с пунктом "Все"
            var cats = await _repository.GetCategoriesAsync();
            var withAll = new List<LookupItem> { new LookupItem { Id = 0, Name = "Все категории" } };
            withAll.AddRange(cats);
            CategoryFilterCombo.ItemsSource = withAll;
            CategoryFilterCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить категории: " + ex.Message);
        }

        DeleteExpenseButton.Visibility = AuthorizationService.CanDeleteRecords(UserSession.CurrentUser)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _uiReady = true;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();

            int? idCategory = null;
            if (CategoryFilterCombo.SelectedValue is int v && v > 0)
                idCategory = v;

            var dateFrom = DateFromPicker?.SelectedDate;
            var dateTo   = DateToPicker?.SelectedDate;

            // список расходов
            _items.Clear();
            var rows = await _repository.GetExpensesAsync(SearchBox?.Text, dateFrom, dateTo, idCategory);
            foreach (var r in rows)
                _items.Add(r);
            ExpensesCountText.Text = $"Всего записей: {_items.Count}";

            // расходы по категориям
            _byCategory.Clear();
            var byCat = await _repository.GetByCategoryAsync(dateFrom, dateTo);
            foreach (var c in byCat)
                _byCategory.Add(c);

            // финансовые итоги
            var totalExpenses = await _repository.GetTotalForPeriodAsync(dateFrom, dateTo);
            var totalPayments = await _paymentRepo.GetTotalForPeriodAsync(dateFrom, dateTo);
            var totalSales    = await _saleRepo.GetTotalForPeriodAsync(dateFrom, dateTo);

            var profit = new ProfitSummary
            {
                IncomePayments = totalPayments,
                IncomeSales    = totalSales,
                Expenses       = totalExpenses
            };

            IncomePaymentsText.Text = profit.IncomePaymentsText;
            IncomeSalesText.Text    = profit.IncomeSalesText;
            ExpensesTotalText.Text  = profit.ExpensesText;
            ProfitText.Text         = profit.ProfitText;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить расходы: " + ex.Message);
        }
    }

    // ---------- handlers ----------

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

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

    private async void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void AddExpense_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ExpenseEditDialog { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void EditExpense_Click(object sender, RoutedEventArgs e)
    {
        if (ExpensesGrid.SelectedItem is not ExpenseListItem item)
            return;

        var dlg = new ExpenseEditDialog(item.IdExpense) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void ExpensesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ExpensesGrid.SelectedItem is not ExpenseListItem item) return;
        var dlg = new ExpenseEditDialog(item.IdExpense) { Owner = this };
        if (dlg.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void DeleteExpense_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanDeleteRecords(UserSession.CurrentUser))
        {
            ShowError("Удаление доступно только администратору и директору.");
            return;
        }

        if (ExpensesGrid.SelectedItem is not ExpenseListItem item)
            return;

        var ans = MessageBox.Show(
            $"Удалить расход от {item.ExpenseDateText} на сумму {item.AmountText}?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ans != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteAsync(item.IdExpense);
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
