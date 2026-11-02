#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class TeacherStatsDialog : Window
{
    private readonly TeachersRepository _repository;
    private readonly int _idTeacher;
    private readonly ObservableCollection<TeacherGroupAttendanceStatsRow> _rows = new();

    public TeacherStatsDialog(TeachersRepository repository, int idTeacher)
    {
        InitializeComponent();
        _repository = repository;
        _idTeacher = idTeacher;
        StatsGrid.ItemsSource = _rows;

        var today = DateTime.Today;
        FromDateBox.Text = new DateTime(today.Year, today.Month, 1).ToString("dd.MM.yyyy");
        ToDateBox.Text = today.ToString("dd.MM.yyyy");

        Loaded += async (_, _) => await CalculateAsync();
    }

    private async void Calculate_Click(object sender, RoutedEventArgs e) => await CalculateAsync();

    private async Task CalculateAsync()
    {
        if (!TryGetPeriod(out var from, out var to)) return;

        try
        {
            var report = await _repository.GetTeacherAttendanceStatsAsync(_idTeacher, from, to);

            TeacherNameText.Text = $"{report.TeacherName} · период: {report.PeriodText}";
            GroupsCountText.Text = report.GroupsCount.ToString();
            StudentsCountText.Text = report.StudentsCount.ToString();
            PlannedLessonsText.Text = report.PlannedLessons.ToString();
            LessonsCountText.Text = report.LessonsHeld.ToString();
            HoursCountText.Text = report.TotalHoursText;
            OverallPercentText.Text = report.OverallPercentText;

            _rows.Clear();
            foreach (var row in report.Rows) _rows.Add(row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryGetPeriod(out DateTime from, out DateTime to)
    {
        if (!DateTime.TryParseExact(FromDateBox.Text?.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
        {
            MessageBox.Show("Дата начала должна быть в формате ДД.ММ.ГГГГ.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            to = default;
            return false;
        }
        if (!DateTime.TryParseExact(ToDateBox.Text?.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
        {
            MessageBox.Show("Дата окончания должна быть в формате ДД.ММ.ГГГГ.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (from > to)
        {
            MessageBox.Show("Дата начала не может быть позже даты окончания.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private async void ThisMonth_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1);
        FromDateBox.Text = first.ToString("dd.MM.yyyy");
        ToDateBox.Text = first.AddMonths(1).AddDays(-1).ToString("dd.MM.yyyy");
        await CalculateAsync();
    }

    private async void LastMonth_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        FromDateBox.Text = first.ToString("dd.MM.yyyy");
        ToDateBox.Text = first.AddMonths(1).AddDays(-1).ToString("dd.MM.yyyy");
        await CalculateAsync();
    }

    private async void ThisYear_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        FromDateBox.Text = new DateTime(today.Year, 1, 1).ToString("dd.MM.yyyy");
        ToDateBox.Text = new DateTime(today.Year, 12, 31).ToString("dd.MM.yyyy");
        await CalculateAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
