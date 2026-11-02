#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Printing;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class GroupsWindow : Window
{
    private readonly GroupsRepository _repository = new();
    private readonly GroupDailyReportRepository _dailyReportRepository = new();
    private readonly ObservableCollection<StudyGroupListItem> _groups = new();
    private readonly ObservableCollection<ScheduleItem> _schedule = new();
    private readonly ObservableCollection<ScheduleDayColumn> _scheduleDays = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));

    private bool _uiReady;
    private bool _showingGroups = true;
    private bool _canViewGroupHistory;
    private bool _canEditGroupDailyReport;
    private bool _scheduleDatePickerSync;
    private DateTime _scheduleWeekStart = GetWeekStartMonday(DateTime.Today);
    private int? _selectedScheduleBranchId;

    public GroupsWindow()
    {
        InitializeComponent();
        GroupsGrid.ItemsSource = _groups;
        InitializeScheduleDays();
        ScheduleCardsControl.ItemsSource = _scheduleDays;
        ScheduleGrid.ItemsSource = _schedule;
        UpdateScheduleWeekDates();
        UpdateScheduleWeekHeader();
        _searchDebouncer.Debounced += async _ => await SafeReloadAsync();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async Task SafeReloadAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void InitializeScheduleDays()
    {
        _scheduleDays.Clear();
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 1, DayName = "Понедельник", ShortDayName = "ПН" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 2, DayName = "Вторник", ShortDayName = "ВТ" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 3, DayName = "Среда", ShortDayName = "СР" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 4, DayName = "Четверг", ShortDayName = "ЧТ" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 5, DayName = "Пятница", ShortDayName = "ПТ" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 6, DayName = "Суббота", ShortDayName = "СБ" });
        _scheduleDays.Add(new ScheduleDayColumn { DayOfWeek = 7, DayName = "Воскресенье", ShortDayName = "ВС" });
        UpdateScheduleWeekDates();
    }

    private static DateTime GetWeekStartMonday(DateTime date)
    {
        var day = date.Date;
        var diff = (7 + (day.DayOfWeek - DayOfWeek.Monday)) % 7;
        return day.AddDays(-diff);
    }

    private void UpdateScheduleWeekDates()
    {
        for (var i = 0; i < _scheduleDays.Count; i++)
            _scheduleDays[i].Date = _scheduleWeekStart.AddDays(i);
    }

    private void UpdateScheduleWeekHeader()
    {
        var weekEnd = _scheduleWeekStart.AddDays(6);
        ScheduleWeekRangeText.Text = $"{_scheduleWeekStart:dd.MM.yyyy} — {weekEnd:dd.MM.yyyy}";
        ScheduleWeekHintText.Text = _scheduleWeekStart <= DateTime.Today && DateTime.Today <= weekEnd
            ? "Текущая неделя"
            : "Листайте стрелками, выберите дату или вернитесь к текущей неделе";
        SyncScheduleJumpDatePicker();
    }

    private void SyncScheduleJumpDatePicker()
    {
        if (ScheduleJumpDatePicker == null) return;

        var weekEnd = _scheduleWeekStart.AddDays(6);
        var target = _scheduleWeekStart <= DateTime.Today && DateTime.Today <= weekEnd
            ? DateTime.Today
            : _scheduleWeekStart;

        if (ScheduleJumpDatePicker.SelectedDate?.Date == target.Date)
            return;

        _scheduleDatePickerSync = true;
        try
        {
            ScheduleJumpDatePicker.SelectedDate = target;
        }
        finally
        {
            _scheduleDatePickerSync = false;
        }
    }

    private async Task GoToScheduleWeekAsync(DateTime anyDateInWeek)
    {
        var newWeekStart = GetWeekStartMonday(anyDateInWeek);
        if (newWeekStart == _scheduleWeekStart)
            return;

        _scheduleWeekStart = newWeekStart;
        UpdateScheduleWeekDates();
        UpdateScheduleWeekHeader();
        if (!_showingGroups)
            await ReloadScheduleAsync();
    }

    private async Task ShiftScheduleWeekAsync(int days)
    {
        _scheduleWeekStart = _scheduleWeekStart.AddDays(days);
        UpdateScheduleWeekDates();
        UpdateScheduleWeekHeader();
        if (!_showingGroups)
            await ReloadScheduleAsync();
    }

    private async void ScheduleJumpDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _scheduleDatePickerSync) return;
        if (ScheduleJumpDatePicker.SelectedDate is not DateTime selected) return;
        await GoToScheduleWeekAsync(selected);
        await UpdateScheduleReportPanelAsync();
    }

    private async void ScheduleDayDate_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScheduleDayColumn day } || day.Date == default)
            return;

        e.Handled = true;
        await GoToScheduleWeekAsync(day.Date);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady) return;
        _uiReady = true;
        _canViewGroupHistory = AuthorizationService.CanViewGroupHistory(UserSession.CurrentUser);
        _canEditGroupDailyReport = AuthorizationService.CanEditGroupDailyReport(UserSession.CurrentUser);
        HistoryButton.Visibility = _canViewGroupHistory ? Visibility.Visible : Visibility.Collapsed;
        if (ScheduleDailyReportButton != null)
            ScheduleDailyReportButton.Visibility = _canEditGroupDailyReport ? Visibility.Visible : Visibility.Collapsed;
        if (ScheduleBranchList != null)
            ScheduleBranchList.ItemsSource = StudyBranch.All;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            if (_showingGroups)
                await ReloadGroupsAsync();
            else
                await ReloadScheduleAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async Task ReloadGroupsAsync()
    {
        _groups.Clear();
        var rows = await _repository.GetGroupsAsync(SearchBox?.Text, OnlyActiveCheck?.IsChecked == true);
        foreach (var row in rows)
        {
            row.ShowHistoryButton = _canViewGroupHistory;
            _groups.Add(row);
        }
        GroupsCountText.Text = $"найдено групп: {_groups.Count}";
        GroupsEmptyText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ReloadScheduleAsync()
    {
        if (!_selectedScheduleBranchId.HasValue)
            return;

        _schedule.Clear();
        foreach (var day in _scheduleDays)
            day.Lessons.Clear();

        var rows = await _repository.GetScheduleAsync(
            null,
            OnlyActiveCheck?.IsChecked == true,
            _selectedScheduleBranchId);
        // Если есть поиск, фильтруем в памяти по названию группы / преподавателю / кабинету
        var filter = SearchBox?.Text?.Trim();
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var f = filter.ToLowerInvariant();
                if (!row.GroupTitle.ToLowerInvariant().Contains(f)
                    && !row.TeacherName.ToLowerInvariant().Contains(f)
                    && !row.ClassroomNumber.ToLowerInvariant().Contains(f))
                    continue;
            }
            _schedule.Add(row);

            foreach (var day in _scheduleDays)
            {
                if (day.DayOfWeek == row.DayOfWeek)
                {
                    row.LessonDate = day.Date;
                    day.Lessons.Add(row);
                    break;
                }
            }
        }
        ScheduleCountText.Text = $"найдено занятий: {_schedule.Count}";

        var weekEnd = _scheduleWeekStart.AddDays(6);
        var daySummaries = await _dailyReportRepository.GetDaySummariesForRangeAsync(
            _scheduleWeekStart,
            weekEnd,
            _selectedScheduleBranchId);
        foreach (var day in _scheduleDays)
        {
            if (daySummaries.TryGetValue(day.Date.Date, out var summary))
            {
                day.ReportStudentsTotal = summary.StudentsTotal;
                day.ReportTrialsTotal = summary.TrialsTotal;
            }
            else
            {
                day.ReportStudentsTotal = 0;
                day.ReportTrialsTotal = 0;
            }
        }

        await UpdateScheduleReportPanelAsync();
        var isEmpty = _schedule.Count == 0;
        ScheduleEmptyPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (isEmpty)
        {
            ScheduleCardsControl.Visibility = Visibility.Collapsed;
            ScheduleListBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var listViewActive = ViewListButton.Style == (Style)FindResource("TabButtonActiveStyle");
        ScheduleCardsControl.Visibility = listViewActive ? Visibility.Collapsed : Visibility.Visible;
        ScheduleListBorder.Visibility = listViewActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateScheduleBranchUi()
    {
        var hasBranch = _selectedScheduleBranchId.HasValue;
        ScheduleBranchPickerPanel.Visibility = hasBranch ? Visibility.Collapsed : Visibility.Visible;
        ScheduleContentPanel.Visibility = hasBranch ? Visibility.Visible : Visibility.Collapsed;
        ViewModeBorder.Visibility = hasBranch ? Visibility.Visible : Visibility.Collapsed;

        if (!hasBranch)
            return;

        var branch = StudyBranch.Find(_selectedScheduleBranchId!.Value);
        if (branch == null)
            return;

        ScheduleBranchTitleText.Text = branch.ShortName;
        ScheduleBranchAddressText.Text = branch.AddressLine;
        ScheduleEmptyText.Text =
            $"Расписание для филиала «{branch.ShortName}» пока пустое.\nДобавьте занятия — укажите кабинет этого филиала.";
    }

    private async Task SelectScheduleBranchAsync(int idBranch)
    {
        _selectedScheduleBranchId = idBranch;
        UpdateScheduleBranchUi();
        await ReloadScheduleAsync();
    }

    private async void ScheduleBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idBranch)
            await SelectScheduleBranchAsync(idBranch);
    }

    private void ScheduleChangeBranch_Click(object sender, RoutedEventArgs e)
    {
        _selectedScheduleBranchId = null;
        UpdateScheduleBranchUi();
        _schedule.Clear();
        foreach (var day in _scheduleDays)
            day.Lessons.Clear();
        ScheduleCountText.Text = string.Empty;
        ScheduleEmptyPanel.Visibility = Visibility.Collapsed;
    }

    private async void ScheduleCreateFirst_Click(object sender, RoutedEventArgs e) =>
        await OpenScheduleEditDialogAsync(null);

    // ========= Табы =========
    private async void TabGroups_Click(object sender, RoutedEventArgs e)
    {
        _showingGroups = true;
        TabGroupsButton.Style = (Style)FindResource("TabButtonActiveStyle");
        TabScheduleButton.Style = (Style)FindResource("TabButtonStyle");
        GroupsPanel.Visibility = Visibility.Visible;
        SchedulePanel.Visibility = Visibility.Collapsed;
        ViewModeBorder.Visibility = Visibility.Collapsed;
        AddItemButton.Content = "➕  Добавить группу";
        await ReloadAsync();
    }

    private async void TabSchedule_Click(object sender, RoutedEventArgs e)
    {
        _showingGroups = false;
        TabGroupsButton.Style = (Style)FindResource("TabButtonStyle");
        TabScheduleButton.Style = (Style)FindResource("TabButtonActiveStyle");
        GroupsPanel.Visibility = Visibility.Collapsed;
        SchedulePanel.Visibility = Visibility.Visible;
        AddItemButton.Content = "➕  Добавить занятие";
        UpdateScheduleBranchUi();
        if (_selectedScheduleBranchId.HasValue)
            await ReloadScheduleAsync();
    }

    private async void SchedulePrevWeek_Click(object sender, RoutedEventArgs e) =>
        await ShiftScheduleWeekAsync(-7);

    private async void ScheduleNextWeek_Click(object sender, RoutedEventArgs e) =>
        await ShiftScheduleWeekAsync(7);

    private async void ScheduleTodayWeek_Click(object sender, RoutedEventArgs e) =>
        await GoToScheduleWeekAsync(DateTime.Today);

    // ========= Переключатель вида расписания =========
    private void ViewCards_Click(object sender, RoutedEventArgs e)
    {
        ViewCardsButton.Style = (Style)FindResource("TabButtonActiveStyle");
        ViewListButton.Style = (Style)FindResource("TabButtonStyle");
        if (_schedule.Count > 0)
        {
            ScheduleCardsControl.Visibility = Visibility.Visible;
            ScheduleListBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ViewList_Click(object sender, RoutedEventArgs e)
    {
        ViewCardsButton.Style = (Style)FindResource("TabButtonStyle");
        ViewListButton.Style = (Style)FindResource("TabButtonActiveStyle");
        if (_schedule.Count > 0)
        {
            ScheduleCardsControl.Visibility = Visibility.Collapsed;
            ScheduleListBorder.Visibility = Visibility.Visible;
        }
    }

    // ========= Поиск / фильтр =========
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        var text = SearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _searchDebouncer.Schedule();
    }

    private async void OnlyActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private void PrintSchedule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var documentPath = CreateScheduleWordDocument();
            Process.Start(new ProcessStartInfo(documentPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private string CreateScheduleWordDocument()
    {
        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Арт-студия",
            "Расписание");
        Directory.CreateDirectory(outputDirectory);

        var logoPath = Path.Combine(outputDirectory, "logo.png");
        CopyLogoForWord(logoPath);

        var documentPath = Path.Combine(outputDirectory, $"Расписание_{DateTime.Now:yyyyMMdd_HHmmss}.rtf");
        File.WriteAllText(documentPath, BuildScheduleWordRtf(), Encoding.ASCII);
        return documentPath;
    }

    private static void CopyLogoForWord(string logoPath)
    {
        var logoResource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/logo.png"));
        if (logoResource == null)
            return;

        using var input = logoResource.Stream;
        using var output = File.Create(logoPath);
        input.CopyTo(output);
    }

    private string BuildScheduleWordRtf()
    {
        var rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\deff0\deflang1049{\fonttbl{\f0 Segoe UI;}}");
        rtf.Append(@"{\colortbl;");
        rtf.Append(@"\red15\green23\blue42;");     // 1 text
        rtf.Append(@"\red71\green85\blue105;");    // 2 muted
        rtf.Append(@"\red248\green250\blue252;");  // 3 card
        rtf.Append(@"\red203\green213\blue225;");  // 4 border
        rtf.Append(@"\red30\green41\blue59;");     // 5 dark header
        rtf.Append(@"\red255\green255\blue255;");  // 6 white
        rtf.Append(@"\red224\green231\blue255;");  // 7 indigo pale
        rtf.Append(@"\red220\green252\blue231;");  // 8 green pale
        rtf.Append(@"\red254\green249\blue195;");  // 9 yellow pale
        rtf.Append(@"\red252\green231\blue243;");  // 10 pink pale
        rtf.Append(@"\red219\green234\blue254;");  // 11 blue pale
        rtf.Append(@"\red45\green212\blue191;");   // 12 teal accent
        rtf.Append(@"}");
        rtf.Append(@"\paperw16838\paperh11906\landscape\margl560\margr560\margt560\margb560\fs20\cf1 ");

        rtf.Append(@"\trowd\trgaph120\trleft0");
        rtf.Append(@"\clcbpat6\clbrdrt\brdrs\brdrw10\brdrcf4\clbrdrl\brdrs\brdrw10\brdrcf4\clbrdrb\brdrs\brdrw10\brdrcf4\clbrdrr\brdrs\brdrw10\brdrcf4\clpadl180\clpadr180\clpadt120\clpadb120\cellx4800");
        rtf.Append(@"\clcbpat6\clbrdrt\brdrs\brdrw10\brdrcf4\clbrdrl\brdrs\brdrw10\brdrcf4\clbrdrb\brdrs\brdrw10\brdrcf4\clbrdrr\brdrs\brdrw10\brdrcf4\clpadl220\clpadr220\clpadt520\clpadb120\cellx15700");
        rtf.Append(@"\intbl\qc ");
        AppendLogoRtf(rtf);
        rtf.Append(@"\cell\intbl\qc\cf1\b\fs32 ");
        AppendRtfText(rtf, "Расписание занятий");
        rtf.Append(@"\b\fs26\line\cf2 ");
        AppendRtfText(rtf, $"Арт-студия · неделя {_scheduleWeekStart:dd.MM.yyyy} – {_scheduleWeekStart.AddDays(6):dd.MM.yyyy}");
        rtf.Append(@"\b0\cell\row");

        const int tableWidth = 15470;
        const int cellWidth = tableWidth / 7;
        rtf.Append(@"\trowd\trgaph0\trleft0");
        for (var i = 1; i <= 7; i++)
        {
            rtf.Append(@"\clcbpat5");
            rtf.Append(@"\clbrdrt\brdrs\brdrw10\brdrcf4\clbrdrl\brdrs\brdrw10\brdrcf4");
            rtf.Append(@"\clbrdrb\brdrs\brdrw10\brdrcf4\clbrdrr\brdrs\brdrw10\brdrcf4");
            rtf.Append(@"\clpadl120\clpadr120\clpadt100\clpadb100");
            rtf.Append(@"\cellx").Append(i * cellWidth);
        }

        foreach (var day in _scheduleDays)
        {
            rtf.Append(@"\intbl\qc\cf6\b\fs20 ");
            AppendRtfText(rtf, $"{day.ShortDayName} · {day.DateLongText}");
            rtf.Append(@"\b0\fs16\line ");
            AppendRtfText(rtf, $"{day.Lessons.Count} занятий");
            rtf.Append(@"\cell ");
        }

        rtf.Append(@"\row");

        rtf.Append(@"\trowd\trgaph0\trleft0");
        for (var i = 1; i <= 7; i++)
        {
            var fill = 6 + ((i - 1) % 5) + 1;
            rtf.Append(@"\clcbpat").Append(fill);
            rtf.Append(@"\clbrdrt\brdrs\brdrw10\brdrcf4\clbrdrl\brdrs\brdrw10\brdrcf4");
            rtf.Append(@"\clbrdrb\brdrs\brdrw10\brdrcf4\clbrdrr\brdrs\brdrw10\brdrcf4");
            rtf.Append(@"\clpadl120\clpadr120\clpadt130\clpadb130");
            rtf.Append(@"\cellx").Append(i * cellWidth);
        }

        foreach (var day in _scheduleDays)
        {
            rtf.Append(@"\intbl\cf1 ");
            foreach (var lesson in day.Lessons.OrderBy(x => x.TimeStart))
            {
                rtf.Append(@"\b\fs22\cf1 ");
                AppendRtfText(rtf, lesson.TimeRangeText);
                rtf.Append(@"\b0\fs18\line\cf1 ");
                AppendRtfText(rtf, lesson.GroupTitle);
                rtf.Append(@"\line\cf2 ");
                AppendRtfText(rtf, lesson.TeacherName);
                rtf.Append(@"\line ");
                AppendRtfText(rtf, lesson.ClassroomText);
                rtf.Append(@"\cf12 ");
                AppendRtfText(rtf, "━━━━━━━━");
                rtf.Append(@"\cf1\line ");
            }

            rtf.Append(@"\cell ");
        }

        rtf.Append(@"\row}");
        return rtf.ToString();
    }

    private static void AppendLogoRtf(StringBuilder rtf)
    {
        var logoResource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/logo.png"));
        if (logoResource == null)
            return;

        byte[] bytes;
        using (var stream = logoResource.Stream)
        {
            var source = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var memory = new MemoryStream();
            encoder.Save(memory);
            bytes = memory.ToArray();
        }

        rtf.Append(@"{\pict\jpegblip\picwgoal2600\pichgoal2600 ");
        foreach (var b in bytes)
            rtf.Append(b.ToString("x2"));
        rtf.Append(@"}");
    }

    private static void AppendRtfText(StringBuilder rtf, string? text)
    {
        foreach (var ch in text ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                case '{':
                case '}':
                    rtf.Append('\\').Append(ch);
                    break;
                case '\n':
                    rtf.Append(@"\line ");
                    break;
                case '\r':
                    break;
                default:
                    if (ch <= 0x7f)
                    {
                        rtf.Append(ch);
                    }
                    else
                    {
                        var code = (short)ch;
                        rtf.Append(@"\u").Append(code).Append('?');
                    }
                    break;
            }
        }
    }

    private string BuildScheduleWordHtml(string logoPath)
    {
        var html = new StringBuilder();
        html.AppendLine("<html><head><meta charset=\"utf-8\">");
        html.AppendLine("<style>");
        html.AppendLine("@page { size: A4 landscape; margin: 12mm; }");
        html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #0f172a; }");
        html.AppendLine(".header { margin-bottom: 14px; }");
        html.AppendLine(".logo { width: 52px; height: 52px; vertical-align: middle; margin-right: 14px; }");
        html.AppendLine(".brand { display: inline-block; vertical-align: middle; }");
        html.AppendLine("h1 { font-size: 24px; margin: 0; }");
        html.AppendLine(".sub { color: #64748b; font-size: 12px; margin-top: 4px; }");
        html.AppendLine("table.week { width: 100%; border-collapse: separate; border-spacing: 6px; table-layout: fixed; }");
        html.AppendLine("td.day { vertical-align: top; border: 1px solid #cbd5e1; padding: 8px; }");
        html.AppendLine(".day-title { font-weight: 800; font-size: 13px; margin-bottom: 2px; }");
        html.AppendLine(".count { color: #94a3b8; font-size: 10px; font-weight: 700; margin-bottom: 8px; }");
        html.AppendLine(".lesson { background: #f8fafc; padding: 7px; margin-bottom: 7px; }");
        html.AppendLine(".time { font-size: 14px; font-weight: 800; margin-bottom: 4px; }");
        html.AppendLine(".group { font-size: 11px; font-weight: 800; margin-bottom: 3px; }");
        html.AppendLine(".teacher, .room { color: #475569; font-size: 10px; margin-top: 2px; }");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<div class=\"header\">");
        html.AppendLine($"<img class=\"logo\" src=\"{HtmlEscape(logoPath)}\" />");
        html.AppendLine("<div class=\"brand\"><h1>Расписание занятий</h1>");
        html.AppendLine($"<div class=\"sub\">Арт-студия · неделя {_scheduleWeekStart:dd.MM.yyyy} – {_scheduleWeekStart.AddDays(6):dd.MM.yyyy}</div></div>");
        html.AppendLine("</div>");
        html.AppendLine("<table class=\"week\"><tr>");

        foreach (var day in _scheduleDays)
        {
            html.AppendLine("<td class=\"day\">");
            html.AppendLine($"<div class=\"day-title\">{HtmlEscape(day.ShortDayName)} · {HtmlEscape(day.DateLongText)}</div>");
            html.AppendLine($"<div class=\"count\">{HtmlEscape(day.DayName)} · {day.Lessons.Count} занятий</div>");

            foreach (var lesson in day.Lessons.OrderBy(x => x.TimeStart))
            {
                html.AppendLine("<div class=\"lesson\">");
                html.AppendLine($"<div class=\"time\">{HtmlEscape(lesson.TimeRangeText)}</div>");
                html.AppendLine($"<div class=\"group\">{HtmlEscape(lesson.GroupTitle)}</div>");
                html.AppendLine($"<div class=\"teacher\">{HtmlEscape(lesson.TeacherName)}</div>");
                html.AppendLine($"<div class=\"room\">{HtmlEscape(lesson.ClassroomText)}</div>");
                html.AppendLine("</div>");
            }

            html.AppendLine("</td>");
        }

        html.AppendLine("</tr></table>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string HtmlEscape(string? value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private FrameworkElement BuildSchedulePrintVisual(double width, double height)
    {
        var page = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Margin = new Thickness(0)
        };

        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(28, 20, 28, 16) };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Resources/logo.png")),
            Width = 48,
            Height = 48,
            Margin = new Thickness(0, 0, 14, 0)
        };
        DockPanel.SetDock(logo, Dock.Left);
        header.Children.Add(logo);

        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = "Арт-студия",
            FontSize = 22,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black
        });
        headerText.Children.Add(new TextBlock
        {
            Text = $"Расписание занятий · {_scheduleWeekStart:dd.MM.yyyy} – {_scheduleWeekStart.AddDays(6):dd.MM.yyyy}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        header.Children.Add(headerText);
        page.Children.Add(header);

        var daysGrid = new UniformGrid
        {
            Columns = 7,
            Margin = new Thickness(24, 0, 24, 24)
        };
        Grid.SetRow(daysGrid, 1);

        foreach (var day in _scheduleDays)
            daysGrid.Children.Add(BuildPrintDayCard(day));

        page.Children.Add(daysGrid);
        page.Measure(new Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();
        return page;
    }

    private static Border BuildPrintDayCard(ScheduleDayColumn day)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Margin = new Thickness(4)
        };

        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(224, 231, 255)),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = day.ShortDayName,
                FontSize = 10,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 48, 163)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = day.DateLongText,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Brushes.Black
        });
        title.Children.Add(new TextBlock
        {
            Text = day.DayName,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
        });
        title.Children.Add(new TextBlock
        {
            Text = $"{day.Lessons.Count} занятий",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        });
        header.Children.Add(title);
        stack.Children.Add(header);

        foreach (var lesson in day.Lessons)
        {
            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = lesson.TimeRangeText, FontSize = 13, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Black },
                        new TextBlock { Text = lesson.GroupTitle, FontSize = 10, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) },
                        new TextBlock { Text = lesson.TeacherName, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) },
                        new TextBlock { Text = lesson.ClassroomText, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 3, 0, 0) }
                    }
                }
            });
        }

        card.Child = stack;
        return card;
    }

    // ========= CRUD =========
    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_showingGroups)
        {
            var dialog = new GroupEditDialog(_repository) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
        else
        {
            await OpenScheduleEditDialogAsync(null);
        }
    }

    private async Task OpenScheduleEditDialogAsync(int? idSchedule)
    {
        var dialog = new ScheduleEditDialog(_repository, idSchedule, _selectedScheduleBranchId) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void GroupsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupsGrid.SelectedItem is StudyGroupListItem item)
        {
            var dialog = new GroupEditDialog(_repository, item.IdGroup) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
    }

    private async void GroupCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idGroup)
        {
            var dialog = new GroupEditDialog(_repository, idGroup) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
    }

    private async void ScheduleCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idSchedule)
            await OpenScheduleEditDialogAsync(idSchedule);
    }

    private async void ScheduleGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScheduleGrid.SelectedItem is ScheduleItem item)
            await OpenScheduleEditDialogAsync(item.IdSchedule);
    }

    private void TeacherSchedule_Click(object sender, RoutedEventArgs e)
    {
        var window = new TeacherScheduleWindow { Owner = this };
        window.ShowDialog();
    }

    private async Task UpdateScheduleReportPanelAsync()
    {
        if (ScheduleReportPeriodText == null)
            return;

        ScheduleReportPeriodText.Text = ReportPeriodHelper.GetPeriodCaption(DateTime.Today);

        var (periodStart, periodEnd) = ReportPeriodHelper.GetAnalysisPeriod(DateTime.Today);
        var periodSummary = _selectedScheduleBranchId.HasValue
            ? await _dailyReportRepository.GetPeriodSummaryAsync(
                periodStart, periodEnd, _selectedScheduleBranchId)
            : new GroupDailyReportDaySummary();
        ScheduleReportPeriodSummaryText.Text = periodSummary.GroupsReported == 0
            ? "За отчётный период данные ещё не внесены"
            : $"За период: {periodSummary.StudentsTotal} чел., {periodSummary.TrialsTotal} проб.";

        var selectedDate = GetSelectedScheduleReportDate();
        if (!_selectedScheduleBranchId.HasValue)
        {
            ScheduleDaySummaryText.Text = string.Empty;
            return;
        }

        var daySummary = await _dailyReportRepository.GetDaySummaryAsync(
            selectedDate, _selectedScheduleBranchId);
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        ScheduleDaySummaryText.Text =
            $"{selectedDate.ToString("dddd, dd.MM.yyyy", culture)}: {daySummary.SummaryText}";
    }

    private DateTime GetSelectedScheduleReportDate()
    {
        if (ScheduleJumpDatePicker?.SelectedDate is DateTime picked)
            return picked.Date;

        var weekEnd = _scheduleWeekStart.AddDays(6);
        return _scheduleWeekStart <= DateTime.Today && DateTime.Today <= weekEnd
            ? DateTime.Today
            : _scheduleWeekStart;
    }

    private async void ScheduleDailyReport_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditGroupDailyReport || !_selectedScheduleBranchId.HasValue)
            return;

        var reportDate = GetSelectedScheduleReportDate();
        var branchTitle = ScheduleBranchTitleText?.Text ?? "филиал";
        var dialog = new GroupDailyReportDialog(reportDate, _selectedScheduleBranchId.Value, branchTitle)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
            await ReloadScheduleAsync();
    }

    private void OpenGroupHistory(int? idGroup, string? groupTitle)
    {
        if (!_canViewGroupHistory)
            return;

        var window = new GroupHistoryWindow(idGroup, groupTitle) { Owner = this };
        window.ShowDialog();
    }

    private void GroupHistoryCard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: int idGroup })
            return;

        var group = _groups.FirstOrDefault(g => g.IdGroup == idGroup);
        OpenGroupHistory(idGroup, group?.Title);
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenGroupHistory(null, null);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

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
