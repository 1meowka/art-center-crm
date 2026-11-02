#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class AttendanceWindow : Window
{
    private readonly TeachersRepository _repository;
    private readonly int _idTeacher;
    private readonly List<TeacherLessonItem> _allLessons = new();
    private readonly ObservableCollection<TeacherLessonItem> _lessons = new();
    private readonly ObservableCollection<AttendanceRow> _rows = new();
    private TeacherLessonItem? _selectedLesson;
    private bool _uiReady;
    private bool _datePickerSync;
    private bool _isApplyingDateFilter;
    private DateTime? _lastAppliedDate;

    public AttendanceWindow(TeachersRepository repository, int idTeacher)
    {
        InitializeComponent();
        _repository = repository;
        _idTeacher = idTeacher;
        LessonsItemsControl.ItemsSource = _lessons;
        StudentsGrid.ItemsSource = _rows;
        SetLessonDate(DateTime.Today, reload: false);
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        try
        {
            await LoadLessonsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _uiReady = true;
        }
    }

    private async Task LoadLessonsAsync()
    {
        HideError();
        _allLessons.Clear();
        _allLessons.AddRange(await _repository.GetTeacherLessonsAsync(_idTeacher));
        await ApplyDateFilterAsync(selectFirstLesson: true);
    }

    private async Task ApplyDateFilterAsync(bool selectFirstLesson)
    {
        if (_isApplyingDateFilter) return;
        if (!TryGetLessonDate(out var lessonDate, showValidationError: false)) return;

        if (_lastAppliedDate == lessonDate && !selectFirstLesson)
            return;

        _isApplyingDateFilter = true;
        try
        {
            _lastAppliedDate = lessonDate;

            var isoDay = ToIsoDayOfWeek(lessonDate);
            var dayLessons = _allLessons
                .Where(x => x.DayOfWeek == isoDay)
                .OrderBy(x => x.TimeStart)
                .ThenBy(x => x.GroupTitle)
                .ToList();

            _lessons.Clear();
            foreach (var lesson in dayLessons) _lessons.Add(lesson);

            _rows.Clear();
            _selectedLesson = null;
            RowsSummaryText.Text = string.Empty;
            DayInfoText.Text = $"{GetRussianDayName(lessonDate)} · {lessonDate:dd.MM.yyyy} · найдено занятий: {_lessons.Count}";
            HeaderText.Text = $"Занятия на {lessonDate:dd.MM.yyyy}";

            if (_allLessons.Count == 0)
            {
                LessonTitleText.Text = "У преподавателя нет активного расписания";
                LessonSubtitleText.Text = "Добавьте группу и занятия в разделе «Группы».";
                return;
            }

            if (_lessons.Count == 0)
            {
                LessonTitleText.Text = "На эту дату занятий нет";
                LessonSubtitleText.Text = BuildNoLessonsHint(lessonDate, isoDay);
                return;
            }

            if (selectFirstLesson)
            {
                _selectedLesson = _lessons[0];
                await LoadStudentsAsync();
            }
            else
            {
                LessonTitleText.Text = "Выберите занятие слева";
                LessonSubtitleText.Text = $"Дата: {lessonDate:dd.MM.yyyy}";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _isApplyingDateFilter = false;
        }
    }

    private string BuildNoLessonsHint(DateTime lessonDate, short isoDay)
    {
        var scheduledDays = _allLessons
            .Select(x => x.DayOfWeek)
            .Distinct()
            .OrderBy(x => x)
            .Select(GetRussianDayNameFromIso)
            .ToList();

        var daysText = scheduledDays.Count == 0
            ? "—"
            : string.Join(", ", scheduledDays);

        return $"{GetRussianDayName(lessonDate)} ({lessonDate:dd.MM.yyyy}) не входит в расписание. " +
               $"Занятия преподавателя: {daysText}. Выберите нужный день стрелками или в календаре.";
    }

    private async void LessonCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int idSchedule) return;

        try
        {
            _selectedLesson = _lessons.FirstOrDefault(l => l.IdSchedule == idSchedule);
            await LoadStudentsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task LoadStudentsAsync()
    {
        if (_selectedLesson == null) return;

        HideError();

        if (!TryGetLessonDate(out var lessonDate)) return;

        LessonTitleText.Text = _selectedLesson.GroupTitle;
        LessonSubtitleText.Text =
            $"{GetRussianDayName(lessonDate)}, {lessonDate:dd.MM.yyyy} · {_selectedLesson.TimeRangeText}, " +
            $"{_selectedLesson.ClassroomText} · {_selectedLesson.LessonHoursText}";

        _rows.Clear();
        var rows = await _repository.GetAttendanceRowsAsync(
            _selectedLesson.IdSchedule, _selectedLesson.IdGroup, lessonDate);
        foreach (var row in rows) _rows.Add(row);

        UpdateRowsSummary();

        if (_rows.Count == 0)
            ShowError("В этой группе на выбранную дату нет учеников. Проверьте состав группы и даты зачисления.");
    }

    private async void SaveAttendance_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();
            if (!TryGetLessonDate(out var lessonDate)) return;


            StudentsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            StudentsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            await _repository.SaveAttendanceAsync(_selectedLesson.IdSchedule, lessonDate, _rows);
            MessageBox.Show("Посещаемость сохранена.", "Готово",
                MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadStudentsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void LessonDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _datePickerSync) return;
        if (LessonDatePicker.SelectedDate is not DateTime) return;

        _lastAppliedDate = null;
        try
        {
            await ApplyDateFilterAsync(selectFirstLesson: true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Today_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetLessonDate(DateTime.Today, reload: false);
            _lastAppliedDate = null;
            await ApplyDateFilterAsync(selectFirstLesson: true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void PreviousDay_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLessonDate(out var date)) return;
        try
        {
            SetLessonDate(date.AddDays(-1), reload: false);
            _lastAppliedDate = null;
            await ApplyDateFilterAsync(selectFirstLesson: true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void NextDay_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLessonDate(out var date)) return;
        try
        {
            SetLessonDate(date.AddDays(1), reload: false);
            _lastAppliedDate = null;
            await ApplyDateFilterAsync(selectFirstLesson: true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SetLessonDate(DateTime date, bool reload)
    {
        var normalized = date.Date;
        if (!reload && LessonDatePicker.SelectedDate?.Date == normalized)
            return;

        _datePickerSync = true;
        try
        {
            LessonDatePicker.SelectedDate = normalized;
        }
        finally
        {
            _datePickerSync = false;
        }
    }

    private void MarkAllPresent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.Status = "был";
        UpdateRowsSummary();
    }

    private void MarkAllAbsent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.Status = "не был";
        UpdateRowsSummary();
    }

    private bool TryGetLessonDate(out DateTime lessonDate, bool showValidationError = true)
    {
        lessonDate = default;
        if (LessonDatePicker.SelectedDate is not DateTime selected)
        {
            if (showValidationError)
                ShowError("Выберите дату занятия в календаре.");
            return false;
        }

        lessonDate = selected.Date;
        return true;
    }

    private static short ToIsoDayOfWeek(DateTime date) => date.DayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)date.DayOfWeek;

    private static string GetRussianDayName(DateTime date) => GetRussianDayNameFromIso(ToIsoDayOfWeek(date));

    private static string GetRussianDayNameFromIso(short isoDay) => isoDay switch
    {
        1 => "Понедельник", 2 => "Вторник", 3 => "Среда", 4 => "Четверг",
        5 => "Пятница", 6 => "Суббота", 7 => "Воскресенье", _ => "—"
    };

    private void UpdateRowsSummary()
    {
        var marked = _rows.Count(r => r.Status != "не отмечено");
        RowsSummaryText.Text = _rows.Count == 0 ? string.Empty : $"отмечено: {marked}/{_rows.Count}";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = string.IsNullOrWhiteSpace(message)
            ? "Не удалось выполнить операцию."
            : message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
