#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class VpExportDialog : Window
{
    private readonly VpReportRepository _repository = AppServices.GetRequiredService<VpReportRepository>();
    private readonly VpReportExporter _exporter = AppServices.GetRequiredService<VpReportExporter>();
    private readonly KpiRepository _kpiRepository = new();
    private bool _isExporting;

    public VpExportDialog()
    {
        InitializeComponent();
        var today = DateTime.Today;
        SingleDatePicker.SelectedDate = today;
        DateFromPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        DateToPicker.SelectedDate = today;

        var defaultYear = AdminReportRepository.ResolveDefaultAcademicYearStart();
        YearComboBox.ItemsSource = Enumerable.Range(defaultYear - 5, 8).Reverse().ToList();
        YearComboBox.SelectedItem = defaultYear;
        YearComboBox.SelectionChanged += (_, _) => UpdateYearEndLabel();
        UpdateYearEndLabel();
    }

    private void UpdateYearEndLabel()
    {
        if (YearComboBox.SelectedItem is int year)
            YearEndText.Text = $"— {year + 1} (лист {year % 100:D2}-{(year + 1) % 100:D2})";
    }

    private void PeriodMode_Changed(object sender, RoutedEventArgs e)
    {
        if (SingleDayPanel == null || PeriodPanel == null)
            return;

        var isSingleDay = SingleDayRadio.IsChecked == true;
        SingleDayPanel.Visibility = isSingleDay ? Visibility.Visible : Visibility.Collapsed;
        PeriodPanel.Visibility = isSingleDay ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = "";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExporting)
            return;

        if (!TryBuildRequest(out var request, out var academicYearStart, out var error))
        {
            StatusText.Text = error;
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Сохранить операционный отчёт",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = VpReportExporter.SuggestFileName(request),
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            _isExporting = true;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeTextMutedBrush");
            StatusText.Text = "Формируем отчёт…";
            Mouse.OverrideCursor = Cursors.Wait;

            var sales = await _repository.GetSalesRegisterLinesAsync(request.DateFrom, request.DateTo);
            var contracts = await _repository.GetContractsAsync(request.DateFrom, request.DateTo);
            var academicYearContracts = await _repository.GetAcademicYearContractsAsync(academicYearStart);
            var trials = await _repository.GetTrialLessonsAsync(request.DateFrom, request.DateTo);
            var newcomers = await _repository.GetNewcomersAsync(request.DateFrom, request.DateTo);
            var debtors = await _repository.GetDebtorsAsync(request.DateFrom, request.DateTo);
            var churned = await _repository.GetChurnedAsync(request.DateFrom, request.DateTo);
            var monthMetrics = await _repository.GetMonthMetricsAsync(request.DateFrom, request.DateTo);

            var kpiPlan = await _kpiRepository.FindPlanForPeriodAsync(request.DateFrom, request.DateTo);
            var planTargets = VpKpiPlanTargets.FromPlan(kpiPlan);

            var outputPath = _exporter.Export(
                request,
                sales,
                contracts,
                academicYearContracts,
                trials,
                newcomers,
                debtors,
                churned,
                monthMetrics,
                planTargets,
                academicYearStart,
                saveDialog.FileName);

            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeSuccessFgBrush");
            var summary =
                $"Из БД загружено за {request.DateFrom:dd.MM.yyyy}–{request.DateTo:dd.MM.yyyy}:\n" +
                $"• оплаты: {sales.Count}\n" +
                $"• договоры за период: {contracts.Count}\n" +
                $"• ученики уч. года ({academicYearStart % 100:D2}-{(academicYearStart + 1) % 100:D2}): {academicYearContracts.Count}\n" +
                $"• пробные: {trials.Count}\n" +
                $"• новички: {newcomers.Count}\n" +
                $"• дебиторка: {debtors.Count}\n" +
                $"• ушедшие: {churned.Count}";

            StatusText.Text = summary.Replace('\n', ' ');

            var totalRows = sales.Count + contracts.Count + academicYearContracts.Count
                + trials.Count + newcomers.Count + debtors.Count + churned.Count;
            var message = totalRows == 0
                ? $"Отчёт сохранён, но за выбранный период в базе нет записей.\n\n{summary}\n\nПуть:\n{outputPath}\n\nОткрыть файл?"
                : $"Отчёт сохранён:\n{outputPath}\n\n{summary}\n\nОткрыть файл?";

            var open = MessageBox.Show(
                message,
                "Операционный отчёт",
                MessageBoxButton.YesNo,
                totalRows == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes && File.Exists(outputPath))
                Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeDangerFgBrush");
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Ошибка выгрузки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isExporting = false;
            Mouse.OverrideCursor = null;
        }
    }

    private bool TryBuildRequest(out PpoReportRequest request, out int academicYearStart, out string error)
    {
        request = new PpoReportRequest();
        error = "";
        academicYearStart = YearComboBox.SelectedItem is int year
            ? year
            : AdminReportRepository.ResolveDefaultAcademicYearStart();

        if (SingleDayRadio.IsChecked == true)
        {
            if (SingleDatePicker.SelectedDate is not DateTime day)
            {
                error = "Укажите дату.";
                return false;
            }

            request.DateFrom = day.Date;
            request.DateTo = day.Date;
            return true;
        }

        if (DateFromPicker.SelectedDate is not DateTime from || DateToPicker.SelectedDate is not DateTime to)
        {
            error = "Укажите начало и конец периода.";
            return false;
        }

        if (from.Date > to.Date)
        {
            error = "Дата начала не может быть позже даты окончания.";
            return false;
        }

        request.DateFrom = from.Date;
        request.DateTo = to.Date;
        return true;
    }
}
