#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class GroupDailyReportDialog : Window
{
    private readonly GroupDailyReportRepository _repository = new();
    private readonly ObservableCollection<GroupDailyReportEditRow> _rows = new();
    private readonly DateTime _reportDate;
    private readonly int _idBranch;

    public GroupDailyReportDialog(DateTime reportDate, int idBranch, string branchTitle)
    {
        _reportDate = reportDate.Date;
        _idBranch = idBranch;
        InitializeComponent();
        Title = $"Отчёт за {_reportDate:dd.MM.yyyy} — {branchTitle}";
        ReportGrid.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _rows.Clear();
            foreach (var row in await _repository.GetEditableRowsAsync(_reportDate, _idBranch))
                _rows.Add(row);

            UpdateSummary();
            if (_rows.Count == 0)
                SummaryText.Text = "В этот день по расписанию филиала нет занятий.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSummary()
    {
        var students = _rows.Sum(r => r.StudentsPresent);
        var trials = _rows.Sum(r => r.TrialPresent);
        SummaryText.Text = _rows.Count == 0
            ? SummaryText.Text
            : $"Итого за день: {students} чел., {trials} проб. ({_rows.Count} групп)";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _repository.SaveReportsAsync(_reportDate, _rows.ToList(), UserSession.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
