#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TeachersWindow : Window
{
    public bool CanViewTeacherSalary { get; }

    private readonly TeachersRepository _repository = new();
    private readonly ObservableCollection<TeacherListItem> _teachers = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));
    private bool _uiReady;

    public TeachersWindow()
    {
        InitializeComponent();
        CanViewTeacherSalary = AuthorizationService.CanViewTeacherSalary(UserSession.CurrentUser);
        TeachersGrid.ItemsSource = _teachers;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += async (_, _) =>
        {
            if (_uiReady) return;
            _uiReady = true;
            await ReloadAsync();
        };
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            _teachers.Clear();
            var rows = await _repository.GetTeachersAsync(SearchBox?.Text, OnlyActiveCheck?.IsChecked == true);
            foreach (var row in rows) _teachers.Add(row);
            TeachersCountText.Text = $"найдено преподавателей: {_teachers.Count}";
            EmptyText.Visibility = _teachers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        var t = SearchBox?.Text ?? string.Empty;
        if (t.Length == 0 || t.Length >= 2)
            _searchDebouncer.Schedule();
    }

    private async void OnlyActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        await ReloadAsync();
    }

    private async void AddTeacher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TeacherEditDialog(_repository) { Owner = this };
        if (dialog.ShowDialog() == true) await ReloadAsync();
    }

    private async void TeachersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TeachersGrid.SelectedItem is TeacherListItem item)
        {
            var dialog = new TeacherEditDialog(_repository, item.IdTeacher) { Owner = this };
            if (dialog.ShowDialog() == true) await ReloadAsync();
        }
    }

    private async void EditTeacher_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idTeacher)
        {
            var dialog = new TeacherEditDialog(_repository, idTeacher) { Owner = this };
            if (dialog.ShowDialog() == true) await ReloadAsync();
        }
    }

    private void Salary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idTeacher)
        {
            var dialog = new SalaryDialog(_repository, idTeacher) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idTeacher)
        {
            var dialog = new TeacherStatsDialog(_repository, idTeacher) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void Groups_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idTeacher)
        {
            var dialog = new TeacherGroupsDialog(_repository, idTeacher) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

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
