#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TrialLessonPaymentDialog : Window
{
    private readonly TrialLessonBookModel _booking;
    private readonly string _bookingSummary;
    private readonly TimeSpan? _lessonEnd;
    private readonly Lead _lead;
    private readonly TrialLessonRepository _trialRepository;
    private readonly PaymentRepository _paymentRepository = new();

    public int SavedTrialId { get; private set; }

    public TrialLessonPaymentDialog(
        TrialLessonBookModel booking,
        string bookingSummary,
        TimeSpan? lessonEnd,
        Lead lead,
        TrialLessonRepository trialRepository)
    {
        InitializeComponent();
        _booking = booking;
        _bookingSummary = bookingSummary;
        _lessonEnd = lessonEnd;
        _lead = lead;
        _trialRepository = trialRepository;

        LeadSummaryText.Text = $"Карточка гостя: {_lead.ChildFullName}, тел. {(string.IsNullOrWhiteSpace(_lead.DisplayPhone) ? "—" : _lead.DisplayPhone)}";
        SlotSummaryText.Text = _bookingSummary;
        AmountBox.Text = TrialLessonDefaults.Price.ToString("0", CultureInfo.InvariantCulture);
        UpdatePaymentUi();

        Loaded += async (_, _) => await LoadPaymentMethodsAsync();
    }

    private async Task LoadPaymentMethodsAsync()
    {
        try
        {
            HideError();
            PaymentMethodCombo.ItemsSource = await _paymentRepository.GetPaymentMethodsAsync();
            if (PaymentMethodCombo.Items.Count > 0)
                PaymentMethodCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private void FreeCheckBox_Changed(object sender, RoutedEventArgs e) => UpdatePaymentUi();

    private void AmountBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePaymentUi();

    private void UpdatePaymentUi()
    {
        if (AmountPanel == null || PaymentMethodPanel == null || SaveButton == null)
            return;

        var isFree = FreeCheckBox.IsChecked == true;
        AmountPanel.IsEnabled = !isFree;
        AmountPanel.Opacity = isFree ? 0.45 : 1;
        PaymentMethodPanel.IsEnabled = !isFree;
        PaymentMethodPanel.Opacity = isFree ? 0.45 : 1;
        SaveButton.Content = isFree ? "Записать бесплатно" : "Оплатить и записать";
    }

    private bool TryGetAmount(out decimal amount)
    {
        amount = 0;
        if (FreeCheckBox.IsChecked == true)
            return true;

        var raw = AmountBox.Text?.Trim().Replace(',', '.') ?? "";
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount < 0)
            return false;
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            if (!TryGetAmount(out var amount))
                amount = 0;

            int? paymentMethodId = null;
            if (amount > 0)
            {
                if (PaymentMethodCombo.SelectedItem is LookupItem method && method.Id > 0)
                    paymentMethodId = method.Id;
                else
                    paymentMethodId = 1;
            }

            SavedTrialId = await _trialRepository.BookTrialWithPaymentAsync(
                _booking,
                _lead.ChildFullName,
                _lead.ClientDisplayName,
                _lead.ContactPhone,
                _lead.ChildPhone,
                paymentMethodId,
                _bookingSummary,
                _lessonEnd,
                amount);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
