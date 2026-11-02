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

public partial class DirectionsYearExportDialog : Window
{
    private readonly DirectionsYearReportRepository _repository = AppServices.GetRequiredService<DirectionsYearReportRepository>();
    private readonly DirectionsYearReportExporter _exporter = AppServices.GetRequiredService<DirectionsYearReportExporter>();
    private bool _isExporting;

    public DirectionsYearExportDialog()
    {
        InitializeComponent();
        InitializeYearComboBox();
    }

    private void InitializeYearComboBox()
    {
        var defaultYear = DirectionsYearReportExporter.ResolveDefaultAcademicYearStart();
        var years = Enumerable.Range(defaultYear - 5, 8).Reverse().ToList();
        YearComboBox.ItemsSource = years;
        YearComboBox.SelectedItem = defaultYear;
        YearComboBox.SelectionChanged += (_, _) => UpdateYearEndLabel();
        UpdateYearEndLabel();
    }

    private void UpdateYearEndLabel()
    {
        if (YearComboBox.SelectedItem is int year)
            YearEndText.Text = (year + 1).ToString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isExporting)
            return;

        if (YearComboBox.SelectedItem is not int academicYearStart)
        {
            StatusText.Text = "Выберите учебный год.";
            return;
        }

        var request = new DirectionsYearReportRequest { AcademicYearStart = academicYearStart };

        var saveDialog = new SaveFileDialog
        {
            Title = "Сохранить свод по направлениям",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = DirectionsYearReportExporter.SuggestFileName(request),
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

            var data = await _repository.GetReportDataAsync(academicYearStart);
            var outputPath = _exporter.Export(data, saveDialog.FileName);

            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeSuccessFgBrush");
            StatusText.Text = "Готово.";

            var open = MessageBox.Show(
                $"Отчёт сохранён:\n{outputPath}\n\nОткрыть файл?",
                "Выгрузка по направлениям",
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
}
