#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TeacherScheduleWindow : Window
{
    private readonly GroupsRepository _groupsRepository = new();
    private readonly TeachersRepository _teachersRepository = new();
    private readonly ObservableCollection<TeacherScheduleRow> _rows = new();
    private readonly bool _isTeacherView;
    private readonly int? _teacherFilterId;
    private bool _uiReady;

    public TeacherScheduleWindow()
    {
        InitializeComponent();

        var user = UserSession.CurrentUser;
        _isTeacherView = AuthorizationService.IsTeacher(user);
        _teacherFilterId = null;

        ScheduleGrid.ItemsSource = _rows;
        ConfigureAccess();
        Loaded += async (_, _) =>
        {
            if (_uiReady)
                return;

            _uiReady = true;
            await ReloadAsync();
        };
    }

    private void ConfigureAccess()
    {
        if (!_isTeacherView)
            return;

        HeaderTitleText.Text = "Мой график";
        PageTitleText.Text = "Моя занятость";
        SubtitleText.Text = "Ваши занятия по дням недели — данные из общего расписания центра";
        HintText.Text = "Чтобы изменить расписание, обратитесь к администратору. Здесь отображается актуальная занятость.";
        OnlyActiveTeachersPanel.Visibility = Visibility.Collapsed;
    }

    private async Task<int?> ResolveTeacherFilterAsync()
    {
        if (!_isTeacherView)
            return null;

        var user = UserSession.CurrentUser;
        if (user == null)
            return null;

        return await _teachersRepository.ResolveTeacherIdForUserAsync(user);
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            _rows.Clear();

            int? filterId = _teacherFilterId ?? await ResolveTeacherFilterAsync();
            if (_isTeacherView && !filterId.HasValue)
            {
                ShowError("К вашей учётной записи не привязана карточка преподавателя.");
                EmptyText.Visibility = Visibility.Visible;
                CountText.Text = "преподаватель не найден";
                return;
            }

            var onlyActive = !_isTeacherView && OnlyActiveTeachersCheck?.IsChecked == true;
            var rows = await _groupsRepository.GetTeacherScheduleGridAsync(filterId, onlyActive);
            foreach (var row in rows)
                _rows.Add(row);

            CountText.Text = _isTeacherView
                ? "ваша недельная занятость"
                : $"преподавателей в графике: {_rows.Count}";
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _isTeacherView)
            return;

        await ReloadAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
