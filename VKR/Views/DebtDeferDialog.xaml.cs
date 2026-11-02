#nullable enable
using System;
using System.Windows;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class DebtDeferDialog : Window
{
    private readonly ContractRepository _repository = new();
    private readonly DebtorItem _debtor;

    public DebtDeferDialog(DebtorItem debtor)
    {
        InitializeComponent();
        _debtor = debtor;
        ClientHintText.Text = $"{debtor.StudentFullName} · остаток {debtor.OutstandingText} · срок {debtor.DueDateText}";
        DeferUntilPicker.SelectedDate = debtor.DeferUntil?.Date ?? DateTime.Today.AddDays(10);
        CommentBox.Text = debtor.DeferComment ?? string.Empty;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorBorder.Visibility = Visibility.Collapsed;


            await _repository.SaveDebtDeferAsync(
                _debtor.IdDebt,
                DeferUntilPicker.SelectedDate.Value,
                CommentBox.Text);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
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
}
