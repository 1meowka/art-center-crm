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

public partial class AdminReportExportDialog : Window
{
    private readonly AdminReportRepository _repository = AppServices.GetRequiredService<AdminReportRepository>();
    private readonly DirectionsYearReportRepository _directionsRepository =
        AppServices.GetRequiredService<DirectionsYearReportRepository>();
    private readonly YearConsolidatedReportExporter _exporter =
        AppServices.GetRequiredService<YearConsolidatedReportExporter>();
    private bool _isExporting;

    public AdminReportExportDialog()
    {
        InitializeComponent();
        var today = DateTime.Today;
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
            YearEndText.Text = $"— {year + 1} (авг—май)";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExporting)
            return;

        if (DateFromPicker.SelectedDate is null || DateToPicker.SelectedDate is null)
        {
            StatusText.Text = "Укажите период.";
            return;
        }

        if (YearComboBox.SelectedItem is not int academicYearStart)
        {
            StatusText.Text = "Выберите учебный год.";
            return;
        }

        var request = new AdminReportRequest
        {
            PeriodStart = DateFromPicker.SelectedDate.Value.Date,
            PeriodEnd = DateToPicker.SelectedDate.Value.Date,
            AcademicYearStart = academicYearStart
        };

        if (request.PeriodEnd < request.PeriodStart)
        {
            StatusText.Text = "Дата окончания не может быть раньше начала.";
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт учебного года",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = YearConsolidatedReportExporter.SuggestFileName(request),
            AddExtension = true,
            DefaultExt = ".xlsx"
        };

        if (saveDialog.ShowDialog() != true)
            return;

        try
        {
            _isExporting = true;
            StatusText.Text = "Формируем отчёт…";
            Mouse.OverrideCursor = Cursors.Wait;

            var adminData = await _repository.GetReportDataAsync(request);
            var directionsData = await _directionsRepository.GetReportDataAsync(academicYearStart);

            var outputPath = _exporter.Export(adminData, directionsData, saveDialog.FileName);

            StatusText.Text = "Готово.";
            var open = MessageBox.Show(
                $"Отчёт сохранён:\n{outputPath}\n\nОткрыть файл?",
                "Учебный год",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes && File.Exists(outputPath))
                Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Ошибка выгрузки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isExporting = false;
            Mouse.OverrideCursor = null;
        }
    }
}
