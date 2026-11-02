#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class AdminWindow : Window
{
    private readonly UserRepository _repository = AppServices.GetRequiredService<UserRepository>();
    private readonly ObservableCollection<UserListItem> _users = new();
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(400));
    private bool _uiReady;

    public AdminWindow()
    {
        InitializeComponent();

        if (!AuthorizationService.CanManageUsers(UserSession.CurrentUser))
        {
            MessageBox.Show(
                "Раздел администрирования доступен только руководителю (директору).",
                "Доступ запрещён",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Loaded += (_, _) => Close();
            return;
        }

        UsersGrid.ItemsSource = _users;
        _searchDebouncer.Debounced += _ => ReloadAsync();
        Loaded += async (_, _) =>
        {
            if (_uiReady)
                return;

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
            _users.Clear();

            var rows = await _repository.GetUsersAsync(SearchBox?.Text, OnlyActiveCheck?.IsChecked == true);
            foreach (var row in rows)
                _users.Add(row);

            UsersCountText.Text = $"найдено пользователей: {_users.Count}";
            EmptyText.Visibility = _users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TotalUsersText.Text = _users.Count.ToString();
            ActiveUsersText.Text = _users.Count(u => u.IsActive).ToString();
            TeachersUsersText.Text = _users.Count(u => RoleCodes.IsTeacher(u.RoleCode)).ToString();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

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

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        var text = SearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _searchDebouncer.Schedule();
    }

    private async void OnlyActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        await ReloadAsync();
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UserEditDialog(_repository) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void UsersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (UsersGrid.SelectedItem is UserListItem item)
        {
            var dialog = new UserEditDialog(_repository, item.IdUser) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
    }

    private async void EditUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is int idUser)
        {
            var dialog = new UserEditDialog(_repository, idUser) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ReloadAsync();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
}
