#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VKR.Services;

namespace VKR.Views;

public partial class SalaryDialog : Window
{
    private readonly TeachersRepository _repository;
    private readonly int _idTeacher;

    public SalaryDialog(TeachersRepository repository, int idTeacher)
    {
        InitializeComponent();
        _repository = repository;
        _idTeacher = idTeacher;

        var today = DateTime.Today;
        var firstDay = new DateTime(today.Year, today.Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        FromDateBox.Text = firstDay.ToString("dd.MM.yyyy");
        ToDateBox.Text = lastDay.ToString("dd.MM.yyyy");

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var all = await _repository.GetTeachersAsync();
            var current = all.FirstOrDefault(t => t.IdTeacher == _idTeacher);
            TeacherNameText.Text = current?.FullName ?? "";
            await CalculateAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Calculate_Click(object sender, RoutedEventArgs e) => await CalculateAsync();

    private async Task CalculateAsync()
    {
        try
        {
            HideError();
            if (!TryGetPeriod(out var fromDate, out var toDate)) return;

            var report = await _repository.GetSalaryAsync(_idTeacher, fromDate, toDate);
            ResultPeriodText.Text = $"ПЕРИОД: {report.PeriodText}".ToUpperInvariant();
            ResultTotalText.Text = report.TotalText;
            LessonsHeldText.Text = report.LessonsHeld.ToString();
            HoursText.Text = report.TotalHoursText;
            VisitsText.Text = report.VisitsCount.ToString();
            RateText.Text = report.RateText;
            LessonHoursText.Text = report.LessonHoursText;
            ResultBorder.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private bool TryGetPeriod(out DateTime fromDate, out DateTime toDate)
    {
        if (!DateTime.TryParseExact(FromDateBox.Text?.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDate))
        {
            ShowError("Дата начала в формате ДД.ММ.ГГГГ.");
            toDate = default;
            return false;
        }
        if (!DateTime.TryParseExact(ToDateBox.Text?.Trim(), "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out toDate))
        {
            ShowError("Дата окончания в формате ДД.ММ.ГГГГ.");
            return false;
        }
        if (fromDate > toDate)
        {
            ShowError("Дата начала не может быть позже даты окончания.");
            return false;
        }
        return true;
    }

    private async void ThisMonth_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        FromDateBox.Text = first.ToString("dd.MM.yyyy");
        ToDateBox.Text = last.ToString("dd.MM.yyyy");
        await CalculateAsync();
    }

    private async void LastMonth_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        var last = first.AddMonths(1).AddDays(-1);
        FromDateBox.Text = first.ToString("dd.MM.yyyy");
        ToDateBox.Text = last.ToString("dd.MM.yyyy");
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
}
