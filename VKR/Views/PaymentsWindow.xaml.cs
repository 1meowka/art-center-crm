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

public partial class PaymentsWindow : Window
{
    private readonly PaymentRepository _payments = new();
    private readonly ContractRepository _contracts = new();

    private readonly ObservableCollection<PaymentListItem> _items = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));

    private bool _uiReady;

    public PaymentsWindow()
    {
        InitializeComponent();
        PaymentsGrid.ItemsSource = _items;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += PaymentsWindow_Loaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async void PaymentsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady) return;
        _uiReady = true;
        DeletePaymentButton.Visibility = AuthorizationService.CanDeleteRecords(UserSession.CurrentUser)
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
            var rows = await _payments.GetPaymentsAsync(
                SearchBox?.Text,
                DateFromPicker?.SelectedDate,
                DateToPicker?.SelectedDate);

            foreach (var r in rows)
                _items.Add(r);

            PaymentsCountText.Text = $"Всего оплат: {_items.Count}";
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

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

    private async void AddPayment_Click(object sender, RoutedEventArgs e)
    {
        PaymentEditDialog.OpenFloating(this, () => { _ = ReloadAsync(); });
    }

    private void EditPayment_Click(object sender, RoutedEventArgs e)
    {
        if (PaymentsGrid.SelectedItem is not PaymentListItem item)
            return;
        PaymentEditDialog.OpenFloating(this, () => { _ = ReloadAsync(); }, item.IdPayment);
    }

    private void PaymentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PaymentsGrid.SelectedItem is not PaymentListItem item) return;
        PaymentEditDialog.OpenFloating(this, () => { _ = ReloadAsync(); }, item.IdPayment);
    }

    private async void DeletePayment_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanDeleteRecords(UserSession.CurrentUser))
        {
            ShowError("Удаление доступно только администратору и директору.");
            return;
        }

        if (PaymentsGrid.SelectedItem is not PaymentListItem item)
            return;

        var ans = MessageBox.Show(
            $"Удалить оплату от {item.PaymentDateText} на сумму {item.AmountText}?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (ans != MessageBoxResult.Yes) return;

        try
        {
            await _payments.DeleteAsync(item.IdPayment);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось удалить оплату."));
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
