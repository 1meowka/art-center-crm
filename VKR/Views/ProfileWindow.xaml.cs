#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class ProfileWindow : Window
{
    private readonly UserRepository _repository = AppServices.GetRequiredService<UserRepository>();
    private UserProfileDetails? _profile;

    public ProfileWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideMessages();

            var currentUser = UserSession.CurrentUser;

            _profile = await _repository.GetProfileAsync(currentUser.IdUser);

            FullNameText.Text = _profile.FullName;
            RoleText.Text = _profile.RoleName;
            LoginText.Text = "@" + _profile.Login;
            EmailText.Text = string.IsNullOrWhiteSpace(_profile.Email) ? "—" : _profile.Email;
            LastLoginText.Text = _profile.LastLoginAt.HasValue
                ? _profile.LastLoginAt.Value.ToString("dd.MM.yyyy HH:mm")
                : "ещё не входил";

            InitialsText.Text = BuildInitials(_profile.FirstName, _profile.LastName);

            if (_profile.IsTeacherRole)
            {
                TeacherLinkPanel.Visibility = Visibility.Visible;
                if (_profile.IdTeacher.HasValue && !string.IsNullOrWhiteSpace(_profile.TeacherFullName))
                {
                    TeacherNameText.Text = _profile.TeacherFullName;
                    OpenTeacherButton.Visibility = Visibility.Visible;
                    TeacherHintText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TeacherNameText.Text = "Привязка не настроена";
                    OpenTeacherButton.Visibility = Visibility.Collapsed;
                    TeacherHintText.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private static string BuildInitials(string firstName, string lastName)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? "?" : firstName.Trim()[0].ToString();
        var last = string.IsNullOrWhiteSpace(lastName) ? "?" : lastName.Trim()[0].ToString();
        return $"{last}{first}".ToUpperInvariant();
    }

    private void OpenTeacherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profile?.IdTeacher is not int idTeacher)
            return;

        var dialog = new TeacherEditDialog(new TeachersRepository(), idTeacher) { Owner = this };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
        SuccessBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowSuccess(string message)
    {
        SuccessText.Text = message;
        SuccessBorder.Visibility = Visibility.Visible;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void HideMessages()
    {
        ErrorText.Text = string.Empty;
        SuccessText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
        SuccessBorder.Visibility = Visibility.Collapsed;
    }
}
