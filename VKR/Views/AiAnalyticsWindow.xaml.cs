#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class AiAnalyticsWindow : Window
{
    private readonly AiAnalyticsService _aiService = AppServices.GetRequiredService<AiAnalyticsService>();
    private readonly StudentRepository _studentRepository = AppServices.GetRequiredService<StudentRepository>();
    private readonly LeadRepository _leadRepository = AppServices.GetRequiredService<LeadRepository>();
    private readonly ObservableCollection<ChurnPrediction> _churnPredictions = new();
    private readonly ObservableCollection<LeadConversionPrediction> _conversionPredictions = new();
    private readonly ObservableCollection<AiRecommendationItem> _recommendations = new();
    private readonly ObservableCollection<DirectionAnalyticsItem> _directions = new();
    private readonly ObservableCollection<AiRecommendationHistoryItem> _history = new();

    public AiAnalyticsWindow()
    {
        InitializeComponent();

        ChurnGrid.ItemsSource = _churnPredictions;
        ConversionGrid.ItemsSource = _conversionPredictions;
        RecommendationsListBox.ItemsSource = _recommendations;
        DirectionGrid.ItemsSource = _directions;
        RecommendationHistoryListBox.ItemsSource = _history;

        ChurnGrid.MouseDoubleClick += ChurnGrid_MouseDoubleClick;
        ConversionGrid.MouseDoubleClick += ConversionGrid_MouseDoubleClick;
        RecommendationsListBox.MouseDoubleClick += RecommendationsListBox_MouseDoubleClick;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowTab("summary");
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            Mouse.OverrideCursor = Cursors.Wait;

            var churnPredictions = await _aiService.GetChurnPredictionsAsync();
            var conversionPredictions = await _aiService.GetLeadConversionPredictionsAsync();
            var recommendations = await _aiService.GetRecommendationsAsync(churnPredictions, conversionPredictions);
            var summary = await _aiService.GetSummaryAsync();
            var directions = await _aiService.GetDirectionAnalyticsAsync();
            var history = await _aiService.GetRecommendationHistoryAsync();

            _churnPredictions.Clear();
            foreach (var prediction in churnPredictions)
                _churnPredictions.Add(prediction);

            _conversionPredictions.Clear();
            foreach (var prediction in conversionPredictions)
                _conversionPredictions.Add(prediction);

            _recommendations.Clear();
            foreach (var recommendation in recommendations)
                _recommendations.Add(recommendation);

            _directions.Clear();
            foreach (var direction in directions)
                _directions.Add(direction);

            _history.Clear();
            foreach (var item in history)
                _history.Add(item);

            UpdateSummary(summary);
            UpdateHistoryVisibility();
            ChurnPanel.DataContext = summary;
            ConversionPanel.DataContext = summary;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void UpdateHistoryVisibility()
    {
        var hasHistory = _history.Count > 0;
        RecommendationHistoryPanel.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        RecommendationHistoryEmptyText.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSummary(AiAnalyticsSummary summary)
    {
        ActiveStudentsText.Text = summary.ActiveStudentsCount.ToString("N0");
        ArchivedStudentsText.Text = "архивных: " + summary.ArchivedStudentsCount.ToString("N0");

        NewStudentsText.Text = summary.NewStudentsInPeriod.ToString("N0");
        LeftStudentsText.Text = "ушли: " + summary.LeftStudentsInPeriod.ToString("N0");

        ChurnRateText.Text = FormatPercent(summary.ChurnRate);
        GrowthRateText.Text = "прирост: " + FormatPercent(summary.ClientGrowthRate);
        AttendanceRateText.Text = FormatPercent(summary.AttendanceRate);

        HighRiskText.Text = summary.HighRiskStudentsCount.ToString("N0");
        MediumRiskText.Text = "средний риск: " + summary.MediumRiskStudentsCount.ToString("N0")
                              + " · низкий: " + summary.LowRiskStudentsCount.ToString("N0");

        ActiveLeadsText.Text = summary.ActiveLeadsCount.ToString("N0");
        HighConversionText.Text = summary.HighConversionLeadsCount.ToString("N0");
        ExpectedClientsText.Text = "ожидаемо: " + summary.ExpectedNewClients.ToString("N1") + " клиента";
        ConversionLevelsText.Text = "средние: " + summary.MediumConversionLeadsCount.ToString("N0")
                                    + " · низкие: " + summary.LowConversionLeadsCount.ToString("N0");

        TotalDebtText.Text = FormatMoney(summary.TotalDebt);
        DebtorsText.Text = "должников: " + summary.DebtorsCount.ToString("N0")
                           + " · Kзад ср.: " + FormatPercent(summary.AverageDebtRatio);

        LastCalcDateText.Text = summary.LastAnalyticsCalculatedAt?.ToString("dd.MM.yyyy HH:mm:ss") ?? "не рассчитано";
        RecommendationsCountText.Text = "Активных рекомендаций: " + summary.ActiveRecommendationsCount.ToString("N0");
    }

    private async void RecalculateButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Пересчитать аналитику по данным базы? Будут обновлены прогнозы оттока и конверсии заявок.",
            "Пересчет ИИ-аналитики",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            HideError();
            Mouse.OverrideCursor = Cursors.Wait;
            await _aiService.RecalculateAnalyticsAsync();
            await ReloadAsync();
            MessageBox.Show("Аналитика пересчитана и сохранена в базе данных.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось пересчитать аналитику."));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void RecommendationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not AiRecommendationItem item)
            return;

        try
        {
            await _aiService.SetRecommendationCompletedAsync(item, checkBox.IsChecked == true);
            var summary = await _aiService.GetSummaryAsync();
            RecommendationsCountText.Text = "Активных рекомендаций: " + summary.ActiveRecommendationsCount.ToString("N0");

            if (item.IsCompleted)
                _recommendations.Remove(item);

            var history = await _aiService.GetRecommendationHistoryAsync();
            _history.Clear();
            foreach (var historyItem in history)
                _history.Add(historyItem);
            UpdateHistoryVisibility();
        }
        catch (Exception ex)
        {
            checkBox.IsChecked = !checkBox.IsChecked;
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось сохранить статус рекомендации."));
        }
    }

    private void ChurnGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChurnGrid.SelectedItem is ChurnPrediction prediction)
            OpenStudentCard(prediction.IdStudent);
    }

    private void ConversionGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ConversionGrid.SelectedItem is LeadConversionPrediction prediction)
            _ = OpenLeadCardAsync(prediction.IdLead);
    }

    private void RecommendationsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecommendationsListBox.SelectedItem is not AiRecommendationItem item)
            return;

        if (item.EntityType == "student")
            OpenStudentCard(item.EntityId);
        else if (item.EntityType == "lead")
            _ = OpenLeadCardAsync(item.EntityId);
    }

    private void OpenStudentCard(int idStudent)
    {
        try
        {
            var dialog = new StudentCardDialog(_studentRepository, idStudent) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось открыть карточку обучающегося."));
        }
    }

    private async Task OpenLeadCardAsync(int idLead)
    {
        try
        {
            var lead = await _leadRepository.GetByIdAsync(idLead);
            if (lead == null)
            {
                MessageBox.Show("Карточка гостя не найдена или уже закрыта.", "ИИ-аналитика", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new LeadCardDialog(lead) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось открыть карточку гостя."));
        }
    }

    private void TabSummary_Click(object sender, RoutedEventArgs e) => ShowTab("summary");
    private void TabChurn_Click(object sender, RoutedEventArgs e) => ShowTab("churn");
    private void TabConversion_Click(object sender, RoutedEventArgs e) => ShowTab("conversion");
    private void TabRecommendations_Click(object sender, RoutedEventArgs e) => ShowTab("recommendations");
    private void TabFormula_Click(object sender, RoutedEventArgs e) => ShowTab("formula");

    private void ShowTab(string tab)
    {
        SummaryPanel.Visibility = Visibility.Collapsed;
        ChurnPanel.Visibility = Visibility.Collapsed;
        ConversionPanel.Visibility = Visibility.Collapsed;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        FormulaPanel.Visibility = Visibility.Collapsed;

        TabSummaryButton.Style = (Style)FindResource("TabButtonStyle");
        TabChurnButton.Style = (Style)FindResource("TabButtonStyle");
        TabConversionButton.Style = (Style)FindResource("TabButtonStyle");
        TabRecommendationsButton.Style = (Style)FindResource("TabButtonStyle");
        TabFormulaButton.Style = (Style)FindResource("TabButtonStyle");

        switch (tab)
        {
            case "churn":
                ChurnPanel.Visibility = Visibility.Visible;
                TabChurnButton.Style = (Style)FindResource("TabButtonActiveStyle");
                break;
            case "conversion":
                ConversionPanel.Visibility = Visibility.Visible;
                TabConversionButton.Style = (Style)FindResource("TabButtonActiveStyle");
                break;
            case "recommendations":
                RecommendationsPanel.Visibility = Visibility.Visible;
                TabRecommendationsButton.Style = (Style)FindResource("TabButtonActiveStyle");
                break;
            case "formula":
                FormulaPanel.Visibility = Visibility.Visible;
                TabFormulaButton.Style = (Style)FindResource("TabButtonActiveStyle");
                break;
            default:
                SummaryPanel.Visibility = Visibility.Visible;
                TabSummaryButton.Style = (Style)FindResource("TabButtonActiveStyle");
                break;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = "❌ " + message;
        ErrorMessageText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorMessageText.Visibility = Visibility.Collapsed;
    }

    private static string FormatPercent(decimal value) =>
        value.ToString("P0", CultureInfo.CurrentCulture);

    private static string FormatMoney(decimal value) =>
        value.ToString("N2", CultureInfo.CurrentCulture) + " ₽";
}
