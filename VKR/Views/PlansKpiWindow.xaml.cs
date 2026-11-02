#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class PlansKpiWindow : Window
{
    private readonly KpiService _kpiService = new();
    private readonly ObservableCollection<KpiPlan> _plans = new();
    private readonly ObservableCollection<KpiResult> _results = new();

    private DateTime _currentStartDate;
    private DateTime _currentEndDate;
    private KpiPlan? _selectedPlan;

    public PlansKpiWindow()
    {
        InitializeComponent();
        PlansListBox.ItemsSource = _plans;
        KpiResultsGrid.ItemsSource = _results;

        _currentStartDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        _currentEndDate = DateTime.Now;

        StartDatePicker.SelectedDate = _currentStartDate;
        EndDatePicker.SelectedDate = _currentEndDate;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdatePeriodSummary();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            Mouse.OverrideCursor = Cursors.Wait;

            var plans = await _kpiService.GetAllPlansAsync();
            _plans.Clear();
            foreach (var plan in plans)
                _plans.Add(plan);

            PlansCountText.Text = $"найдено: {_plans.Count}";

            if (_selectedPlan != null)
            {
                var stillExists = false;
                foreach (var plan in _plans)
                {
                    if (plan.IdKpiPlan == _selectedPlan.IdKpiPlan)
                    {
                        stillExists = true;
                        PlansListBox.SelectedItem = plan;
                        _selectedPlan = plan;
                        break;
                    }
                }

                if (!stillExists)
                    _selectedPlan = null;
            }

            if (_selectedPlan != null)
            {
                var results = await _kpiService.CalculateKpiWithPlanAsync(
                    _selectedPlan.IdKpiPlan,
                    _currentStartDate,
                    _currentEndDate);
                DisplayResults(results);
            }
            else
            {
                var results = await _kpiService.CalculateKpiAsync(_currentStartDate, _currentEndDate);
                DisplayResults(results);
            }

            UpdatePeriodSummary();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void DisplayResults(IReadOnlyList<KpiResult> results)
    {
        _results.Clear();
        foreach (var result in results)
            _results.Add(result);

        UpdateMetricCard(LeadsValueText, LeadsStatusText, LeadsProgressBar, FindResult(results, "Карточки гостей"));
        UpdateMetricCard(StudentsValueText, StudentsStatusText, StudentsProgressBar, FindResult(results, "Новые обучающиеся"));
        UpdateMetricCard(ContractsValueText, ContractsStatusText, ContractsProgressBar, FindResult(results, "Договоры"));
        UpdateMetricCard(RevenueValueText, RevenueStatusText, RevenueProgressBar, FindResult(results, "Выручка"));
        UpdateMetricCard(AttendanceValueText, AttendanceStatusText, AttendanceProgressBar, FindResult(results, "Посещаемость"));
        UpdateMetricCard(ChurnValueText, ChurnStatusText, ChurnProgressBar, FindResult(results, "Отток обучающихся"));
    }

    private static KpiResult? FindResult(IReadOnlyList<KpiResult> results, string indicatorName)
    {
        foreach (var result in results)
        {
            if (result.IndicatorName == indicatorName)
                return result;
        }

        return null;
    }

    private static void UpdateMetricCard(TextBlock valueText, TextBlock statusText, ProgressBar progressBar, KpiResult? result)
    {
        if (result == null)
        {
            valueText.Text = "—";
            statusText.Text = "нет данных";
            progressBar.Value = 0;
            progressBar.Foreground = GetStatusBrush("информационный");
            return;
        }

        valueText.Text = result.FactValue;

        if (result.PlanValue == "-" || string.IsNullOrWhiteSpace(result.PlanValue))
            statusText.Text = result.Status;
        else if (result.IsSpecial)
            statusText.Text = $"лимит {result.PlanValue} · факт {result.FactValue} · {result.Status}";
        else
            statusText.Text = $"{result.PlanValue} / {result.FactValue} · {result.CompletionPercent:F0}% · {result.Status}";

        progressBar.Value = result.IsSpecial
            ? result.Status == "в норме" ? 100 : 35
            : (double)Math.Min(100m, Math.Max(0m, result.CompletionPercent));

        progressBar.Foreground = GetStatusBrush(result.Status);
    }

    private static Brush GetStatusBrush(string status) =>
        status switch
        {
            "выполнено" or "в норме" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            "частично" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            "не выполнено" or "превышен" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };

    private void UpdatePeriodSummary()
    {
        var period = $"{_currentStartDate:dd.MM.yyyy} — {_currentEndDate:dd.MM.yyyy}";
        PeriodSummaryText.Text = _selectedPlan == null
            ? $"{period} · фактические показатели без сравнения с планом"
            : $"{period} · план «{_selectedPlan.PlanName}»";
    }

    private async void ApplyDateFilter_Click(object sender, RoutedEventArgs e)
    {

        _currentStartDate = StartDatePicker.SelectedDate.Value;
        _currentEndDate = EndDatePicker.SelectedDate.Value;


        await ReloadAsync();
    }

    private void PlansListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPlan = PlansListBox.SelectedItem as KpiPlan;
        UpdatePeriodSummary();
    }

    private async void AddPlanButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PlanEditDialog();
        if (dialog.ShowDialog() == true && dialog.Plan != null)
        {
            try
            {
                await _kpiService.SavePlanAsync(dialog.Plan);
                _selectedPlan = dialog.Plan;
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при сохранении плана: {ex.Message}");
            }
        }
    }

    private async void EditPlan_Click(object sender, RoutedEventArgs e)
    {
        var plan = GetPlanFromSender(sender);

        var dialog = new PlanEditDialog(plan);
        if (dialog.ShowDialog() == true && dialog.Plan != null)
        {
            try
            {
                await _kpiService.SavePlanAsync(dialog.Plan);
                _selectedPlan = dialog.Plan;
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка при сохранении плана: {ex.Message}");
            }
        }
    }

    private async void DeletePlan_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanDeleteRecords(UserSession.CurrentUser))
        {
            ShowError("Удаление доступно только администратору и директору.");
            return;
        }

        var plan = GetPlanFromSender(sender);
        if (plan == null)
            return;

        var result = MessageBox.Show(
            $"Удалить план \"{plan.PlanName}\"?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _kpiService.DeletePlanAsync(plan.IdKpiPlan);
            if (_selectedPlan?.IdKpiPlan == plan.IdKpiPlan)
                _selectedPlan = null;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка при удалении плана: {ex.Message}");
        }
    }

    private async void CalculateButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlansListBox.SelectedItem is not KpiPlan plan)
            return;

        try
        {
            HideError();
            Mouse.OverrideCursor = Cursors.Wait;
            _selectedPlan = plan;

            var results = await _kpiService.CalculateKpiWithPlanAsync(
                plan.IdKpiPlan,
                _currentStartDate,
                _currentEndDate);
            DisplayResults(results);
            UpdatePeriodSummary();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка при расчёте KPI: {ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static KpiPlan? GetPlanFromSender(object sender)
    {
        if (sender is FrameworkElement { Tag: KpiPlan plan })
            return plan;

        return null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowError(string message)
    {
        ErrorMessageText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorMessageText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
