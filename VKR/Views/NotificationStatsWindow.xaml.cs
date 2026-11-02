#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class NotificationStatsWindow : Window
{
    private readonly StudentSatisfactionRepository _satisfactionRepository =
        AppServices.GetRequiredService<StudentSatisfactionRepository>();
    private readonly ContractRenewalRepository _renewalRepository =
        AppServices.GetRequiredService<ContractRenewalRepository>();

    private readonly ObservableCollection<SatisfactionHistoryItem> _satisfactionHistory = new();
    private readonly ObservableCollection<ContractRenewalHistoryItem> _renewalHistory = new();
    private readonly CultureInfo _ruCulture = CultureInfo.GetCultureInfo("ru-RU");

    private bool _isPeriodInitializing = true;
    private NotificationStatsSection _initialSection;

    public NotificationStatsWindow(NotificationStatsSection initialSection = NotificationStatsSection.Satisfaction)
    {
        _initialSection = initialSection;
        InitializeComponent();
        SatisfactionHistoryGrid.ItemsSource = _satisfactionHistory;
        RenewalHistoryGrid.ItemsSource = _renewalHistory;
        SectionHelp.AttachTopic(this, SectionHelp.NotificationStatsTitle, SectionHelp.NotificationStatsBody);
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        StatsTabs.SelectedIndex = _initialSection == NotificationStatsSection.Renewal ? 1 : 0;
        ApplyPeriodPreset("CurrentMonth", reload: false);
        PeriodPresetCombo.SelectedIndex = 0;
        _isPeriodInitializing = false;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            ClearError();

            if (DateFromPicker is null || DateToPicker is null)
                return;

            NotificationSchema.Ensure();

            if (DateFromPicker.SelectedDate is not DateTime fromDate
                || DateToPicker.SelectedDate is not DateTime toDate)
            {
                ShowError("Укажите корректный период.");
                return;
            }


            PeriodHintText.Text =
                $"Показаны записи, отмеченные с {fromDate:dd.MM.yyyy} по {toDate:dd.MM.yyyy}";

            var satisfactionSummary = await _satisfactionRepository.GetStatsSummaryAsync(fromDate, toDate);
            var satisfactionHistory = await _satisfactionRepository.GetHistoryAsync(fromDate, toDate);
            _satisfactionHistory.Clear();
            foreach (var item in satisfactionHistory)
                _satisfactionHistory.Add(item);

            SatisfactionTotalText.Text = satisfactionSummary.Total.ToString("N0", _ruCulture);
            SatisfactionPositiveText.Text = satisfactionSummary.PositiveCount.ToString("N0", _ruCulture);
            SatisfactionPositiveShareText.Text = satisfactionSummary.PositivePercentText;
            SatisfactionNegativeText.Text = satisfactionSummary.NegativeCount.ToString("N0", _ruCulture);
            SatisfactionNegativeShareText.Text = satisfactionSummary.NegativePercentText;
            SatisfactionNeutralText.Text = satisfactionSummary.NeutralCount.ToString("N0", _ruCulture);
            SatisfactionNeutralShareText.Text = satisfactionSummary.NeutralPercentText;

            var renewalSummary = await _renewalRepository.GetStatsSummaryAsync(fromDate, toDate);
            var renewalHistory = await _renewalRepository.GetHistoryAsync(fromDate, toDate);
            _renewalHistory.Clear();
            foreach (var item in renewalHistory)
                _renewalHistory.Add(item);

            RenewalTotalText.Text = renewalSummary.Total.ToString("N0", _ruCulture);
            RenewalYesText.Text = renewalSummary.YesCount.ToString("N0", _ruCulture);
            RenewalYesShareText.Text = renewalSummary.YesShareText;
            RenewalNoText.Text = renewalSummary.NoCount.ToString("N0", _ruCulture);
            RenewalNoShareText.Text = renewalSummary.NoShareText;
            RenewalUnknownText.Text = renewalSummary.UnknownCount.ToString("N0", _ruCulture);
            RenewalUnknownShareText.Text = renewalSummary.UnknownShareText;
            RenewalRateText.Text = renewalSummary.RenewalRateText;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить статистику: " + ex.Message);
        }
    }

    private void PeriodPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPeriodInitializing || !IsLoaded || PeriodPresetCombo.SelectedItem is not ComboBoxItem item)
            return;

        if (item.Tag is string tag && tag != "Custom")
            ApplyPeriodPreset(tag);
    }

    private async void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPeriodInitializing || !IsLoaded || PeriodPresetCombo is null)
            return;

        _isPeriodInitializing = true;
        PeriodPresetCombo.SelectedIndex = 3;
        _isPeriodInitializing = false;
        await ReloadAsync();
    }

    private void ApplyPeriodPreset(string tag, bool reload = true)
    {
        if (DateFromPicker is null || DateToPicker is null)
            return;

        var today = DateTime.Today;
        DateTime from;
        DateTime to;

        switch (tag)
        {
            case "LastMonth":
                var firstThisMonth = new DateTime(today.Year, today.Month, 1);
                from = firstThisMonth.AddMonths(-1);
                to = firstThisMonth.AddDays(-1);
                break;
            case "CurrentYear":
                from = new DateTime(today.Year, 1, 1);
                to = today;
                break;
            default:
                from = new DateTime(today.Year, today.Month, 1);
                to = today;
                break;
        }

        _isPeriodInitializing = true;
        DateFromPicker.SelectedDate = from;
        DateToPicker.SelectedDate = to;
        _isPeriodInitializing = false;

        if (reload && IsLoaded)
            _ = ReloadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

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
