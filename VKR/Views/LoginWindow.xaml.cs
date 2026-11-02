using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService = AppServices.GetRequiredService<AuthService>();
    private bool _isBusy;

    public LoginWindow()
    {
        InitializeComponent();
        WindowMinimize.Attach(this);
        LoginTextBox.Focus();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        string login = LoginTextBox.Text.Trim();
        string password = GetPassword();


        try
        {
            _isBusy = true;
            LoginButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            var result = await _authService.AuthenticateAsync(login, password);

            if (!result.IsSuccess)
            {
                ShowError(result.ErrorMessage ?? "Неверный логин или пароль.");
                PasswordBox.Clear();
                PasswordTextBox.Clear();
                return;
            }

            UserSession.SignIn(result.User!);

            var mainWindow = new MainWindow
            {
                CurrentUser = result.User
            };
            mainWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.DatabaseConnection));
        }
        finally
        {
            _isBusy = false;
            LoginButton.IsEnabled = true;
            Mouse.OverrideCursor = null;
        }
    }

    private string GetPassword()
    {
        if (PasswordTextBox.Visibility == Visibility.Visible)
            return PasswordTextBox.Text;

        return PasswordBox.Password;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowMinimize.Minimize(this);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void ShowPassword_Press(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        PasswordTextBox.Text = PasswordBox.Password;
        PasswordTextBox.Visibility = Visibility.Visible;
        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Focus();
        PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
        EyeIconText.Text = "🙈";
    }

    private void ShowPassword_Release(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (PasswordTextBox.Visibility != Visibility.Visible)
            return;

        PasswordBox.Password = PasswordTextBox.Text;
        PasswordTextBox.Clear();
        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        EyeIconText.Text = "👁";
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
