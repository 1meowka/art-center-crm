#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class PpoExportDialog : Window
{
    private readonly PpoReportRepository _repository = AppServices.GetRequiredService<PpoReportRepository>();
    private readonly PpoReportExporter _exporter = AppServices.GetRequiredService<PpoReportExporter>();
    private bool _isExporting;

    public PpoExportDialog()
    {
        InitializeComponent();
        var today = DateTime.Today;
        SingleDatePicker.SelectedDate = today;
        DateFromPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        DateToPicker.SelectedDate = today;
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

        if (!TryBuildRequest(out var request, out var error))
        {
            StatusText.Text = error;
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт ППО",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = PpoReportExporter.SuggestFileName(request),
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

            var income = await _repository.GetDailyIncomeAsync(request.DateFrom, request.DateTo);
            var expenses = await _repository.GetExpensesAsync(request.DateFrom, request.DateTo);
            var outputPath = _exporter.Export(request, income, expenses, saveDialog.FileName);

            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeSuccessFgBrush");
            StatusText.Text = "Готово.";

            var open = MessageBox.Show(
                $"Отчёт сохранён:\n{outputPath}\n\nОткрыть файл?",
                "Выгрузка ППО",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes && File.Exists(outputPath))
            {
                Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
            }

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

    private bool TryBuildRequest(out PpoReportRequest request, out string error)
    {
        request = new PpoReportRequest();
        error = "";

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
