#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class UserEditDialog : Window
{
    private readonly UserRepository _repository;
    private readonly int? _idUser;
    private IReadOnlyList<RoleItem> _roles = Array.Empty<RoleItem>();
    private bool _passwordsVisible;

    public UserEditDialog(UserRepository repository, int? idUser = null)
    {
        InitializeComponent();
        _repository = repository;
        _idUser = idUser;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideError();

            _roles = await _repository.GetRolesAsync();
            RoleCombo.ItemsSource = _roles;

            if (_idUser.HasValue)
            {
                DialogTitleText.Text = "Редактирование пользователя";
                DialogSubtitleText.Text = "Изменение данных и роли. Новый пароль — только при необходимости.";
                PasswordLabelText.Text = "НОВЫЙ ПАРОЛЬ";
                ConfirmPasswordLabelText.Text = "ПОВТОР ПАРОЛЯ";
                PasswordHintText.Text = $"Новый пароль — по желанию. {PasswordPolicy.Hint}";

                var user = await _repository.GetUserAsync(_idUser.Value);

                LoginBox.Text = user.Login;
                LastNameBox.Text = user.LastName;
                FirstNameBox.Text = user.FirstName;
                MiddleNameBox.Text = user.MiddleName ?? string.Empty;
                EmailBox.Text = user.Email ?? string.Empty;
                IsActiveCheck.IsChecked = user.IsActive;
                RoleCombo.SelectedItem = SelectRole(_roles, user.IdRole, user.RoleCode);
                DialogTitleText.Text = $"Редактирование: {user.LastName} {user.FirstName}".Trim();

                await LoadTeachersAsync(user.IdTeacher);
                UpdateTeacherPanelVisibility();

                if (UserSession.CurrentUser?.IdUser == user.IdUser)
                {
                    IsActiveCheck.IsEnabled = false;
                    IsActiveCheck.ToolTip = "Нельзя заблокировать собственную учётную запись.";
                }
            }
            else if (_roles.Count > 0)
            {
                RoleCombo.SelectedItem = _roles.FirstOrDefault(r => RoleCodes.IsManager(r.RoleCode))
                    ?? _roles.FirstOrDefault(IsTeacherRole)
                    ?? _roles.FirstOrDefault(r => RoleCodes.IsAdmin(r.RoleCode))
                    ?? _roles[0];
                await LoadTeachersAsync(null);
                UpdateTeacherPanelVisibility();
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private async Task LoadTeachersAsync(int? selectedTeacherId)
    {
        var teachers = await _repository.GetLinkableTeachersAsync(_idUser, selectedTeacherId);
        var items = new List<LookupItem> { new() { Id = 0, Name = "— не привязан —" } };
        items.AddRange(teachers);
        TeacherCombo.ItemsSource = items;
        TeacherCombo.SelectedItem = items.FirstOrDefault(item => item.Id == (selectedTeacherId ?? 0)) ?? items[0];
    }

    private static RoleItem? SelectRole(IReadOnlyList<RoleItem> roles, int idRole, string roleCode)
    {
        return roles.FirstOrDefault(role => role.IdRole == idRole)
            ?? roles.FirstOrDefault(role =>
                LookupDeduper.NormalizeKey(role.RoleCode) == LookupDeduper.NormalizeKey(roleCode));
    }

    private static bool IsTeacherRole(RoleItem role) => RoleCodes.IsTeacher(role.RoleCode);

    private void UpdateTeacherPanelVisibility()
    {
        TeacherPanel.Visibility = RoleCombo.SelectedItem is RoleItem role && IsTeacherRole(role)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || RoleCombo.SelectedItem is not RoleItem role)
            return;

        UpdateTeacherPanelVisibility();

        if (IsTeacherRole(role))
        {
            var selectedTeacherId = TeacherCombo.SelectedItem is LookupItem item && item.Id > 0
                ? item.Id
                : (int?)null;
            await LoadTeachersAsync(selectedTeacherId);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var role = RoleCombo.SelectedItem as RoleItem ?? new RoleItem { IdRole = 1, RoleCode = "admin", RoleName = "Администратор" };

            int? idTeacher = null;
            if (IsTeacherRole(role) && TeacherCombo.SelectedItem is LookupItem teacherItem && teacherItem.Id > 0)
                idTeacher = teacherItem.Id;

            var model = new UserEditModel
            {
                IdUser = _idUser ?? 0,
                Login = LoginBox.Text.Trim(),
                LastName = LastNameBox.Text.Trim(),
                FirstName = FirstNameBox.Text.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(MiddleNameBox.Text) ? null : MiddleNameBox.Text.Trim(),
                Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim(),
                IdRole = role.IdRole,
                RoleCode = role.RoleCode,
                IsActive = IsActiveCheck.IsChecked == true,
                IdTeacher = idTeacher
            };

            if (_idUser.HasValue)
            {

                var newPassword = string.IsNullOrWhiteSpace(GetPassword()) ? null : GetPassword();

                await _repository.UpdateUserAsync(model, newPassword);
            }
            else
            {

                await _repository.CreateUserAsync(model, GetPassword());
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.SaveFailed));
        }
    }

    private void ShowPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_passwordsVisible)
            HidePasswords();
        else
            ShowPasswords();
    }

    private void ShowPasswords()
    {
        PasswordTextBox.Text = PasswordBox.Password;
        ConfirmPasswordTextBox.Text = ConfirmPasswordBox.Password;
        PasswordTextBox.Visibility = Visibility.Visible;
        PasswordBox.Visibility = Visibility.Collapsed;
        ConfirmPasswordTextBox.Visibility = Visibility.Visible;
        ConfirmPasswordBox.Visibility = Visibility.Collapsed;
        EyeIconText.Text = "🙈";
        ShowPasswordButton.ToolTip = "Скрыть пароль";
        _passwordsVisible = true;
        PasswordTextBox.Focus();
        PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
    }

    private void HidePasswords()
    {
        PasswordBox.Password = PasswordTextBox.Text;
        ConfirmPasswordBox.Password = ConfirmPasswordTextBox.Text;
        PasswordTextBox.Clear();
        ConfirmPasswordTextBox.Clear();
        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        ConfirmPasswordTextBox.Visibility = Visibility.Collapsed;
        ConfirmPasswordBox.Visibility = Visibility.Visible;
        EyeIconText.Text = "👁";
        ShowPasswordButton.ToolTip = "Показать пароль";
        _passwordsVisible = false;
    }

    private string GetPassword() =>
        _passwordsVisible ? PasswordTextBox.Text : PasswordBox.Password;

    private string GetConfirmPassword() =>
        _passwordsVisible ? ConfirmPasswordTextBox.Text : ConfirmPasswordBox.Password;

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
