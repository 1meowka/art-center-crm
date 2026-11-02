#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class SaleEditDialog : Window
{
    private readonly SaleRepository     _repo         = new();
    private readonly PaymentRepository  _paymentRepo  = new();
    private readonly ContractRepository _contractRepo = new();

    private readonly int? _idSale;
    private Sale? _existing;

    public SaleEditDialog() : this(null) { }

    public SaleEditDialog(int? idSale)
    {
        InitializeComponent();
        _idSale = idSale;

        TitleText.Text = _idSale.HasValue ? "Редактирование продажи" : "Новая продажа";

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            ClearError();

            var students = await _contractRepo.GetStudentsLookupAsync();
            var products = await _repo.GetProductsLookupAsync();
            var methods = await _paymentRepo.GetPaymentMethodsAsync();

            if (_idSale.HasValue)
            {
                _existing = await _repo.GetByIdAsync(_idSale.Value);

                SaleDatePicker.SelectedDate = _existing.SaleDate;
                LookupComboHelper.SetItems(StudentCombo, students, _existing.IdStudent);
                LookupComboHelper.SetItems(ProductCombo, products, _existing.IdProduct);
                LookupComboHelper.SetItems(PaymentMethodCombo, methods, _existing.IdPaymentMethod);
                ItemNameBox.Text = _existing.ItemName;
                QuantityBox.Text = _existing.Quantity.ToString(CultureInfo.InvariantCulture);
                UnitPriceBox.Text = _existing.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture);
                TotalAmountBox.Text = _existing.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture);
                CommentBox.Text = _existing.Comment ?? string.Empty;
            }
            else
            {
                LookupComboHelper.SetItems(StudentCombo, students);
                LookupComboHelper.SetItems(ProductCombo, products);
                LookupComboHelper.SetItems(PaymentMethodCombo, methods);
                SaleDatePicker.SelectedDate = DateTime.Today;
                QuantityBox.Text = "1";
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private void ProductCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductCombo.SelectedItem is LookupItem li && string.IsNullOrWhiteSpace(ItemNameBox.Text))
            ItemNameBox.Text = li.Name;
    }

    private void Recalc_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TotalAmountBox == null) return;

        var qOk = int.TryParse(QuantityBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty);
        var pOk = decimal.TryParse(UnitPriceBox?.Text?.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var price);

        if (qOk && pOk)
            TotalAmountBox.Text = (qty * price).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearError();



            if (!int.TryParse(QuantityBox.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var qty) || qty <= 0)
                qty = 1;

            if (!decimal.TryParse(UnitPriceBox.Text?.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 0)
                price = 0;

            var sale = _existing ?? new Sale();
            sale.SaleDate        = SaleDatePicker.SelectedDate ?? DateTime.Today;
            sale.IdStudent       = LookupComboHelper.GetSelectedId(StudentCombo);
            sale.IdProduct       = LookupComboHelper.GetSelectedId(ProductCombo);
            sale.ItemName        = ItemNameBox.Text.Trim();
            sale.Quantity        = qty;
            sale.UnitPrice       = price;
            sale.TotalAmount     = qty * price;
            sale.IdPaymentMethod = LookupComboHelper.GetSelectedId(PaymentMethodCombo);
            sale.Comment         = string.IsNullOrWhiteSpace(CommentBox.Text)
                                       ? null
                                       : CommentBox.Text.Trim();

            if (_idSale.HasValue)
                await _repo.UpdateAsync(sale);
            else
                await _repo.CreateAsync(sale);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.SaveFailed));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
