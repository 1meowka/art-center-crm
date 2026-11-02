#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class MaxBotSettingsDialog : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();
    private readonly MaxBotService _botService = AppServices.GetRequiredService<MaxBotService>();
    private MaxBotSettings? _storedSettings;

    public MaxBotSettingsDialog()
    {
        InitializeComponent();

        if (!AuthorizationService.CanManageMaxBotSettings(UserSession.CurrentUser))
        {
            MessageBox.Show(
                "Настройки бота MAX доступны только администратору.",
                "Доступ запрещён",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Loaded += (_, _) => Close();
            return;
        }

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _repository.GetSettingsAsync();
            if (settings == null)
                return;

            _storedSettings = settings;

            if (settings.IsConfigured)
            {
                TokenHintText.Text = "Токен сохранён (зашифрован в БД). Введите новый только для замены.";
                StatusText.Text = BuildStatus(settings);
            }
            else if (settings.TokenNeedsReentry)
            {
                TokenHintText.Text = "База перенесена с другого ПК — токен нужно ввести заново (тот же, что в кабинете MAX).";
                StatusText.Text = string.IsNullOrWhiteSpace(settings.BotName)
                    ? "Введите токен и нажмите «Проверить»."
                    : BuildStatus(settings);
            }

            OrgNameBox.Text = settings.OrgName;
            OrgAddressBox.Text = settings.OrgAddress ?? string.Empty;
            OrgInnBox.Text = settings.OrgInn ?? string.Empty;
            SupportPhoneBox.Text = settings.SupportPhone ?? string.Empty;
            SupportEmailBox.Text = settings.SupportEmail ?? string.Empty;
            PrivacyUrlBox.Text = settings.PrivacyUrl ?? string.Empty;
            TermsUrlBox.Text = settings.TermsUrl ?? string.Empty;
            WelcomeTextBox.Text = settings.WelcomeText ?? string.Empty;
            DiscountsTextBox.Text = settings.DiscountsText ?? string.Empty;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private static string BuildStatus(MaxBotSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BotName))
            return "Токен сохранён. Нажмите «Проверить», чтобы обновить данные бота.";

        return $"Подключён: {settings.BotName}"
            + (string.IsNullOrWhiteSpace(settings.BotUsername) ? string.Empty : $" (@{settings.BotUsername.TrimStart('@')})");
    }

    private MaxBotSettings ReadForm(string token, MaxBotConnectionInfo? check = null) => new()
    {
        BotToken = token,
        BotName = check?.BotName ?? _storedSettings?.BotName,
        BotUsername = check?.BotUsername ?? _storedSettings?.BotUsername,
        OrgName = OrgNameBox.Text?.Trim() ?? string.Empty,
        OrgAddress = NullIfEmpty(OrgAddressBox.Text),
        OrgInn = NullIfEmpty(OrgInnBox.Text),
        SupportPhone = NullIfEmpty(SupportPhoneBox.Text),
        SupportEmail = NullIfEmpty(SupportEmailBox.Text),
        PrivacyUrl = NullIfEmpty(PrivacyUrlBox.Text),
        TermsUrl = NullIfEmpty(TermsUrlBox.Text),
        WelcomeText = NullIfEmpty(WelcomeTextBox.Text),
        DiscountsText = NullIfEmpty(DiscountsTextBox.Text)
    };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string? ResolveTokenForOperation()
    {
        var entered = TokenBox.Password?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(entered))
            return entered;

        if (_storedSettings is { IsConfigured: true })
            return _storedSettings.BotToken;

        return null;
    }

    private bool ValidateComplianceFields(out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(OrgNameBox.Text))
        {
            error = "Укажите название организации — оно отображается в юридических сообщениях бота.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SupportPhoneBox.Text) && string.IsNullOrWhiteSpace(SupportEmailBox.Text))
        {
            error = "Укажите телефон или e-mail поддержки — это требование платформы MAX.";
            return false;
        }

        return true;
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Удалить сохранённый токен и отключить бота MAX?\n\nОрганизационные поля останутся — их не нужно заполнять заново.",
            "Отключение бота",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            HideError();
            await _repository.ClearBotCredentialsAsync();
            _storedSettings = new MaxBotSettings();
            TokenBox.Clear();
            TokenHintText.Text = "Токен удалён. Введите новый токен, когда будете подключать бота.";
            StatusText.Text = "Бот отключён. Укажите новый токен для подключения.";
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var token = ResolveTokenForOperation();

            StatusText.Text = "Проверка подключения...";
            var result = await _botService.TestConnectionAsync(token);
            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Не удалось подключиться.");
                StatusText.Text = _storedSettings is { IsConfigured: true } ? BuildStatus(_storedSettings) : string.Empty;
                return;
            }

            StatusText.Text = $"✓ Связь установлена: {result.BotName}"
                + (string.IsNullOrWhiteSpace(result.BotUsername) ? string.Empty : $" (@{result.BotUsername.TrimStart('@')})");
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var token = ResolveTokenForOperation();


            var check = await _botService.TestConnectionAsync(token);

            await _repository.SaveSettingsAsync(ReadForm(token, check), UserSession.CurrentUser?.IdUser);

            try
            {
                await _botService.SyncBotCommandsAsync(token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                MessageBox.Show(
                    "Настройки сохранены, но не удалось обновить меню команд в MAX.\n\n" +
                    UserFacingErrors.GetMessage(ex, "Команды можно вызывать вручную: /start, /help"),
                    "Частичное сохранение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
