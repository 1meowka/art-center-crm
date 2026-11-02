#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class ExpenseEditDialog : Window
{
    private readonly ExpenseRepository _repo        = new();
    private readonly PaymentRepository _paymentRepo = new();

    private readonly int? _idExpense;
    private Expense? _existing;

    public ExpenseEditDialog() : this(null) { }

    public ExpenseEditDialog(int? idExpense)
    {
        InitializeComponent();
        _idExpense = idExpense;

        TitleText.Text = _idExpense.HasValue ? "Редактирование расхода" : "Новый расход";

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            ClearError();

            var categories = await _repo.GetCategoriesAsync();
            var methods = await _paymentRepo.GetPaymentMethodsAsync();

            if (_idExpense.HasValue)
            {
                _existing = await _repo.GetByIdAsync(_idExpense.Value);

                ExpenseDatePicker.SelectedDate = _existing.ExpenseDate;
                AmountBox.Text = _existing.Amount.ToString("0.##", CultureInfo.InvariantCulture);
                LookupComboHelper.SetItems(CategoryCombo, categories, _existing.IdExpenseType);
                LookupComboHelper.SetItems(PaymentMethodCombo, methods, _existing.IdPaymentMethod);
                DescriptionBox.Text = _existing.Description ?? string.Empty;
                CommentBox.Text = _existing.Comment ?? string.Empty;
            }
            else
            {
                LookupComboHelper.SetItems(CategoryCombo, categories);
                LookupComboHelper.SetItems(PaymentMethodCombo, methods);
                ExpenseDatePicker.SelectedDate = DateTime.Today;
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearError();

            ClearError();

            var expenseDate = ExpenseDatePicker.SelectedDate ?? DateTime.Today;
            if (!decimal.TryParse(AmountBox.Text?.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount < 0)
                amount = 0;
            var categoryId = CategoryCombo.SelectedItem is LookupItem category && category.Id > 0
                ? category.Id
                : 1;

            var expense = _existing ?? new Expense();
            expense.ExpenseDate     = expenseDate;
            expense.Amount          = amount;
            expense.IdExpenseType   = categoryId;
            expense.IdPaymentMethod = LookupComboHelper.GetSelectedId(PaymentMethodCombo);
            expense.Description     = (DescriptionBox.Text ?? string.Empty).Trim();
            expense.Comment         = string.IsNullOrWhiteSpace(CommentBox.Text)
                                          ? null
                                          : CommentBox.Text.Trim();

            if (_idExpense.HasValue)
                await _repo.UpdateAsync(expense);
            else
            {
                expense.IdCreatedBy = UserSession.CurrentUserId;
                await _repo.CreateAsync(expense);
            }

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
