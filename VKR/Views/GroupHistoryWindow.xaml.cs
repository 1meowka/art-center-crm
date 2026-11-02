#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class GroupHistoryWindow : Window
{
    private readonly AcademicHistoryRepository _history = new();
    private readonly GroupsRepository _groups = new();
    private readonly TeachersRepository _teachers = new();

    private readonly ObservableCollection<GroupFilterOption> _groupOptions = new();
    private readonly ObservableCollection<TeacherFilterOption> _teacherOptions = new();
    private readonly List<ComboBoxItem> _eventTypeOptions = new();

    private int? _fixedGroupId;
    private string? _fixedGroupTitle;
    private bool _uiReady;

    public GroupHistoryWindow(int? idGroup = null, string? groupTitle = null)
    {
        _fixedGroupId = idGroup;
        _fixedGroupTitle = groupTitle;
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_uiReady) return;
        _uiReady = true;

        if (!string.IsNullOrWhiteSpace(_fixedGroupTitle))
        {
            HeaderTitleText.Text = $"История — {_fixedGroupTitle}";
            SubtitleText.Text = "Все изменения по выбранной группе: зачисления, переводы, отчисления, смена преподавателя";
        }

        _eventTypeOptions.Add(new ComboBoxItem { Content = "Все события", Tag = null });
        _eventTypeOptions.Add(new ComboBoxItem { Content = "Смена преподавателя", Tag = "teacher_change" });
        _eventTypeOptions.Add(new ComboBoxItem { Content = "Зачисление", Tag = "enroll" });
        _eventTypeOptions.Add(new ComboBoxItem { Content = "Перевод", Tag = "transfer" });
        _eventTypeOptions.Add(new ComboBoxItem { Content = "Отчисление", Tag = "expel" });
        EventTypeFilterCombo.ItemsSource = _eventTypeOptions;
        EventTypeFilterCombo.SelectedIndex = 0;

        _groupOptions.Add(new GroupFilterOption { Title = "Все группы" });
        foreach (var group in await _groups.GetGroupsAsync(onlyActive: false))
            _groupOptions.Add(new GroupFilterOption { IdGroup = group.IdGroup, Title = group.Title });
        GroupFilterCombo.ItemsSource = _groupOptions;
        GroupFilterCombo.SelectedIndex = 0;
        if (_fixedGroupId.HasValue)
        {
            var selected = _groupOptions.FirstOrDefault(g => g.IdGroup == _fixedGroupId.Value);
            if (selected != null)
                GroupFilterCombo.SelectedItem = selected;
            GroupFilterCombo.IsEnabled = false;
        }

        _teacherOptions.Add(new TeacherFilterOption { Title = "Все преподаватели" });
        foreach (var teacher in await _teachers.GetTeachersAsync(onlyActive: false))
            _teacherOptions.Add(new TeacherFilterOption { IdTeacher = teacher.IdTeacher, Title = teacher.FullName });
        TeacherFilterCombo.ItemsSource = _teacherOptions;
        TeacherFilterCombo.SelectedIndex = 0;

        DateToPicker.SelectedDate = DateTime.Today;
        DateFromPicker.SelectedDate = DateTime.Today.AddMonths(-3);

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var filter = BuildFilter();
            var rows = await _history.GetGroupHistoryTimelineAsync(filter);
            HistoryGrid.ItemsSource = rows;
            CountText.Text = $"Событий: {rows.Count}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), "История изменений",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private GroupHistoryFilter BuildFilter()
    {
        int? idGroup = _fixedGroupId;
        if (!_fixedGroupId.HasValue
            && GroupFilterCombo.SelectedItem is GroupFilterOption groupItem
            && groupItem.IdGroup.HasValue)
            idGroup = groupItem.IdGroup;

        int? idTeacher = null;
        if (TeacherFilterCombo.SelectedItem is TeacherFilterOption teacherItem && teacherItem.IdTeacher.HasValue)
            idTeacher = teacherItem.IdTeacher;

        string? eventCategory = null;
        if (EventTypeFilterCombo.SelectedItem is ComboBoxItem eventItem)
            eventCategory = eventItem.Tag as string;

        return new GroupHistoryFilter
        {
            IdGroup = idGroup,
            IdTeacher = idTeacher,
            EventCategory = eventCategory,
            DateFrom = DateFromPicker.SelectedDate,
            DateTo = DateToPicker.SelectedDate,
            Limit = 1000
        };
    }

    private async void Filter_Changed(object sender, EventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        if (!_fixedGroupId.HasValue)
            GroupFilterCombo.SelectedIndex = 0;
        TeacherFilterCombo.SelectedIndex = 0;
        EventTypeFilterCombo.SelectedIndex = 0;
        DateToPicker.SelectedDate = DateTime.Today;
        DateFromPicker.SelectedDate = DateTime.Today.AddMonths(-3);
        await ReloadAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class GroupFilterOption
    {
        public int? IdGroup { get; init; }
        public string Title { get; init; } = string.Empty;
    }

    private sealed class TeacherFilterOption
    {
        public int? IdTeacher { get; init; }
        public string Title { get; init; } = string.Empty;
    }
}
