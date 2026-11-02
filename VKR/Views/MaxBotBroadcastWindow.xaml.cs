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

public partial class MaxBotBroadcastWindow : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();
    private readonly MaxBotService _botService = AppServices.GetRequiredService<MaxBotService>();

    private readonly ObservableCollection<MaxBotTemplateFolder> _folders = new();
    private readonly ObservableCollection<MaxBotTemplate> _templates = new();
    private readonly ObservableCollection<MaxBotSubscriber> _subscribers = new();

    private bool _uiReady;
    private bool _isSending;

    public MaxBotBroadcastWindow()
    {
        InitializeComponent();
        FoldersList.ItemsSource = _folders;
        TemplatesList.ItemsSource = _templates;
        SubscriberCombo.ItemsSource = _subscribers;

        SettingsButton.Visibility = AuthorizationService.CanManageMaxBotSettings(UserSession.CurrentUser)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SectionHelp.AttachTopic(this, SectionHelp.MaxBotBroadcastTitle, SectionHelp.MaxBotBroadcastBody);
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        AudienceCombo.ItemsSource = new[]
        {
            new LookupItem { Id = 1, Name = "Всем подписчикам (только сервисные сообщения)" },
            new LookupItem { Id = 2, Name = "Одному получателю" }
        };
        AudienceCombo.DisplayMemberPath = "Name";
        AudienceCombo.SelectedIndex = 0;

        await ReloadFoldersAsync();
        await ReloadSubscribersAsync();
        await RefreshConfigStatusAsync();

        _uiReady = true;
        UpdateSingleRecipientVisibility();
    }

    private async Task ReloadFoldersAsync()
    {
        _folders.Clear();
        foreach (var folder in await _repository.GetFoldersAsync())
            _folders.Add(folder);

        if (_folders.Count > 0 && FoldersList.SelectedIndex < 0)
            FoldersList.SelectedIndex = 0;
        else if (_folders.Count == 0)
        {
            _templates.Clear();
            MessageTextBox.Text = string.Empty;
        }
    }

    private async Task ReloadTemplatesAsync(int idFolder)
    {
        _templates.Clear();
        foreach (var template in await _repository.GetTemplatesAsync(idFolder))
            _templates.Add(template);

        TemplatesHeaderText.Text = $"ШАБЛОНЫ ({_templates.Count})";

        if (_templates.Count > 0 && TemplatesList.SelectedIndex < 0)
            TemplatesList.SelectedIndex = 0;
        else if (_templates.Count == 0)
            MessageTextBox.Text = string.Empty;
    }

    private async Task ReloadSubscribersAsync()
    {
        _subscribers.Clear();
        foreach (var subscriber in await _repository.GetSubscribersAsync())
            _subscribers.Add(subscriber);

        if (_subscribers.Count > 0 && SubscriberCombo.SelectedIndex < 0)
            SubscriberCombo.SelectedIndex = 0;
    }

    private async Task RefreshConfigStatusAsync()
    {
        try
        {
            var settings = await _repository.GetSettingsAsync();
            if (settings is { IsConfigured: true })
            {
                var name = string.IsNullOrWhiteSpace(settings.BotName) ? "бот MAX" : settings.BotName;
                ConfigStatusText.Text = $"✓ Бот подключён: {name}"
                    + (string.IsNullOrWhiteSpace(settings.BotUsername) ? string.Empty : $" (@{settings.BotUsername.TrimStart('@')})");
                ConfigStatusText.Foreground = (System.Windows.Media.Brush)FindResource("ThemeTextBrush");
            }
            else
            {
                ConfigStatusText.Text = "⚠ Токен бота MAX не задан — откройте «Настройки бота».";
            }
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = "Не удалось проверить настройки: " + ex.Message;
        }
    }

    private void FoldersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || FoldersList.SelectedItem is not MaxBotTemplateFolder folder)
            return;

        _ = ReloadTemplatesAsync(folder.IdFolder);
    }

    private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplatesList.SelectedItem is MaxBotTemplate template)
            MessageTextBox.Text = template.Body;
    }

    private void AudienceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;

        UpdateSingleRecipientVisibility();
    }

    private void UpdateSingleRecipientVisibility()
    {
        SingleRecipientPanel.Visibility = AudienceCombo.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private MaxBotTemplateFolder? GetSelectedFolder() =>
        FoldersList.SelectedItem as MaxBotTemplateFolder;

    private MaxBotTemplate? GetSelectedTemplate() =>
        TemplatesList.SelectedItem as MaxBotTemplate;

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("Новая папка", "Название папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            ClearError();
            await _repository.InsertFolderAsync(name);
            await ReloadFoldersAsync();
            FoldersList.SelectedItem = _folders.LastOrDefault(f =>
                f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetSelectedFolder();

        var name = PromptText("Переименовать папку", "Название:", folder.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == folder.Name)
            return;

        try
        {
            ClearError();
            await _repository.RenameFolderAsync(folder.IdFolder, name);
            await ReloadFoldersAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetSelectedFolder();

        if (MessageBox.Show(
                $"Удалить папку «{folder.Name}» и все шаблоны в ней?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            ClearError();
            await _repository.DeleteFolderAsync(folder.IdFolder);
            await ReloadFoldersAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetSelectedFolder();

        var dialog = new MaxBotTemplateEditDialog(folder.IdFolder) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        await ReloadTemplatesAsync(folder.IdFolder);
    }

    private async void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = GetSelectedTemplate();

        var dialog = new MaxBotTemplateEditDialog(template) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        await ReloadTemplatesAsync(template.IdFolder);
    }

    private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = GetSelectedTemplate();

        if (MessageBox.Show(
                $"Удалить шаблон «{template.Title}»?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ClearError();
            await _repository.DeleteTemplateAsync(template.IdTemplate);
            await ReloadTemplatesAsync(template.IdFolder);
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_isSending)
            return;

        try
        {
            ClearError();

            var text = MessageTextBox.Text?.Trim() ?? string.Empty;

            var audienceType = AudienceCombo.SelectedIndex == 1 ? "single" : "all";
            int? idSubscriber = null;
            if (audienceType == "single")
            {
                if (SubscriberCombo.SelectedValue is int subscriberId)
                    idSubscriber = subscriberId;
            }

            var template = GetSelectedTemplate();
            var recipientCount = audienceType == "single" ? 1 : _subscribers.Count;
            var complianceNote = audienceType == "all"
                ? "\n\n⚠ По правилам MAX массовые рекламные рассылки запрещены. Отправляйте только сервисные сообщения: напоминания о занятиях, оплате, изменениях расписания."
                : string.Empty;
            var confirm = MessageBox.Show(
                $"Отправить сообщение в MAX ({recipientCount} получ.)?{complianceNote}",
                "Подтверждение рассылки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            _isSending = true;
            ProgressText.Text = "Отправка...";

            var userId = UserSession.CurrentUser?.IdUser
                ?? throw new InvalidOperationException("Пользователь не авторизован.");

            var progress = new Progress<(int done, int total)>(p =>
                ProgressText.Text = $"Отправлено {p.done} из {p.total}...");

            var result = await _botService.SendBroadcastAsync(
                text,
                template?.IdTemplate,
                template?.Title ?? "Произвольное сообщение",
                audienceType,
                idSubscriber,
                userId,
                progress);

            ProgressText.Text = $"Готово: {result.SuccessCount} из {result.TotalCount}"
                + (result.FailCount > 0 ? $", ошибок: {result.FailCount}" : string.Empty);

            MessageBox.Show(
                $"Рассылка завершена.\nУспешно: {result.SuccessCount}\nОшибок: {result.FailCount}",
                "Рассылка MAX",
                MessageBoxButton.OK,
                result.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
            ProgressText.Text = string.Empty;
        }
        finally
        {
            _isSending = false;
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanManageMaxBotSettings(UserSession.CurrentUser))
            return;

        var dialog = new MaxBotSettingsDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            await RefreshConfigStatusAsync();
    }

    private async void Subscribers_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MaxBotSubscribersDialog { Owner = this };
        dialog.ShowDialog();
        await ReloadSubscribersAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private string? PromptText(string title, string label, string defaultValue)
    {
        var dialog = new MaxBotPromptDialog(title, label, defaultValue) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
