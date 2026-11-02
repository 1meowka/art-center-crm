#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class ScheduleEditDialog : Window
{
    private readonly GroupsRepository _repository;
    private readonly int? _idSchedule;
    private readonly int? _idBranch;

    public ScheduleEditDialog(GroupsRepository repository, int? idSchedule = null, int? idBranch = null)
    {
        InitializeComponent();
        _repository = repository;
        _idSchedule = idSchedule;
        _idBranch = idBranch;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideError();

            var groups = await _repository.GetGroupsLookupAsync();
            var classrooms = await _repository.GetClassroomsAsync(_idBranch);
            GroupCombo.ItemsSource = groups;
            ClassroomCombo.ItemsSource = classrooms;
            DayCombo.SelectedIndex = 0;
            LessonTypeCombo.ItemsSource = LessonTypeInfo.Options;
            LessonTypeCombo.SelectedIndex = 0;
            TimeStartBox.Text = "15:00";
            TimeEndBox.Text = "16:30";

            if (!_idSchedule.HasValue)
            {
                MaskedInputParser.SelectFirstIfNone(GroupCombo);
                MaskedInputParser.SelectFirstIfNone(ClassroomCombo);
                ShowPrerequisitesWarning(groups, classrooms);
            }

            if (_idSchedule.HasValue)
            {
                DialogTitleText.Text = "Редактирование занятия";
                DeleteButton.Visibility = Visibility.Visible;

                var allSchedule = await _repository.GetScheduleAsync();
                ScheduleItem? current = null;
                foreach (var s in allSchedule)
                {
                    if (s.IdSchedule == _idSchedule.Value) { current = s; break; }
                }
                if (current != null)
                {
                    GroupCombo.SelectedValue = current.IdGroup;
                    ClassroomCombo.SelectedValue = current.IdClassroom;
                    SetDayCombo(current.DayOfWeek);
                    TimeStartBox.Text = current.TimeStart.ToString(@"hh\:mm");
                    TimeEndBox.Text = current.TimeEnd.ToString(@"hh\:mm");
                    IsActiveCheck.IsChecked = current.IsActive;
                    LessonTypeCombo.SelectedItem = LessonTypeInfo.FindByCode(current.LessonType);
                    DialogTitleText.Text = $"Редактирование: {current.GroupTitle}";
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SetDayCombo(short day)
    {
        foreach (var item in DayCombo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag is string tagStr
                && short.TryParse(tagStr, out var tagVal) && tagVal == day)
            {
                DayCombo.SelectedItem = cbi;
                return;
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            MaskedInputParser.TryGetSelectedId(GroupCombo, out var idGroup);
            if (idGroup <= 0) idGroup = 1;
            MaskedInputParser.TryGetSelectedId(ClassroomCombo, out var idClassroom);
            if (idClassroom <= 0) idClassroom = 1;

            short dayOfWeek = 1;
            if (DayCombo.SelectedItem is ComboBoxItem dayItem && dayItem.Tag is string tag
                && short.TryParse(tag, out var d))
                dayOfWeek = d;

            if (!MaskedInputParser.TryParseTime(TimeStartBox.Text, out var timeStart))
                timeStart = new TimeSpan(10, 0, 0);
            if (!MaskedInputParser.TryParseTime(TimeEndBox.Text, out var timeEnd))
                timeEnd = timeStart.Add(TimeSpan.FromHours(1));
            if (timeStart >= timeEnd)
                timeEnd = timeStart.Add(TimeSpan.FromHours(1));

            var lessonType = LessonTypeCombo.SelectedItem is CodeOption lt ? lt.Code : "regular";

            var model = new ScheduleEditModel
            {
                IdSchedule = _idSchedule ?? 0,
                IdGroup = idGroup,
                IdClassroom = idClassroom,
                DayOfWeek = dayOfWeek,
                TimeStart = timeStart,
                TimeEnd = timeEnd,
                IsActive = IsActiveCheck.IsChecked == true,
                LessonType = lessonType
            };

            await _repository.SaveScheduleAsync(model);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_idSchedule.HasValue) return;

        var result = MessageBox.Show(
            "Удалить занятие из расписания?",
            "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteScheduleAsync(_idSchedule.Value);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowPrerequisitesWarning(
        IReadOnlyList<LookupItem> groups,
        IReadOnlyList<LookupItem> classrooms)
    {
        if (groups.Count > 0 && classrooms.Count > 0)
            return;

        var parts = new List<string>();
        if (groups.Count == 0)
            parts.Add("нет активных групп — создайте группу на вкладке «Группы»");
        if (classrooms.Count == 0)
            parts.Add(_idBranch.HasValue
                ? "нет кабинетов для выбранного филиала — добавьте их в справочнике «Кабинеты»"
                : "нет кабинетов — добавьте их в справочнике «Кабинеты»");

        ShowError("Нельзя сохранить занятие: " + string.Join("; ", parts) + ".");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
