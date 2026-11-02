#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class MaxBotWindow : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();

    public MaxBotWindow()
    {
        InitializeComponent();

        SettingsButton.Visibility = AuthorizationService.CanManageMaxBotSettings(UserSession.CurrentUser)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SectionHelp.AttachTopic(this, "Чат-бот MAX", SectionHelp.GetModuleBody(AppModule.MaxBot));
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await RefreshConfigStatusAsync();
        await RefreshCommandsPreviewAsync();
    }

    private async Task RefreshConfigStatusAsync()
    {
        try
        {
            var settings = await _repository.GetSettingsAsync();
            if (settings is { IsConfigured: true })
            {
                var name = string.IsNullOrWhiteSpace(settings.BotName) ? "бот MAX" : settings.BotName;
                ConfigStatusText.Text = $"✓ Подключён: {name}"
                    + (string.IsNullOrWhiteSpace(settings.BotUsername) ? string.Empty : $" (@{settings.BotUsername.TrimStart('@')})");
            }
            else if (settings is { TokenNeedsReentry: true })
            {
                ConfigStatusText.Text = "⚠ Токен перенесён с другого компьютера — откройте «Настройки бота» и введите токен заново.";
            }
            else
            {
                ConfigStatusText.Text = "⚠ Сначала укажите токен бота в «Настройки бота».";
            }
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = DescribeSettingsCheckError(ex);
        }
    }

    private static string DescribeSettingsCheckError(Exception ex)
    {
        if (ex is System.Security.Cryptography.CryptographicException
            || ex.Message.Contains("ключ", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            return "⚠ Токен сохранён на другом компьютере. Откройте «Настройки бота» и введите токен заново.";
        }

        return "Не удалось проверить настройки: " + UserFacingErrors.GetMessage(ex);
    }

    private async Task RefreshCommandsPreviewAsync()
    {
        try
        {
            var settings = await _repository.GetSettingsAsync() ?? new MaxBotSettings();
            var c = settings.ToComplianceInfo();

            var items = new List<MaxBotCommandPreview>
            {
                new("/help", "Список всех команд бота"),
                new("/расписание", "Занятия на текущую неделю"),
                new("/контакты", "Контакты по филиалам"),
                new("/направления", "Направления (консультация администратора)"),
                new("/скидки", "Скидки и льготы"),
                new("/start", FirstLine(MaxBotCommandHandler.BuildWelcomeMessage(c))),
                new("/privacy", "Политика конфиденциальности"),
                new("/stop", "Удаление данных пользователя"),
                new("Запись", "Инструкция по записи на пробное занятие")
            };

            CommandsList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 100 ? line[..97] + "…" : line;
    }

    private sealed record MaxBotCommandPreview(string Command, string Preview);

    private void Broadcast_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaxBotBroadcastWindow { Owner = this };
        window.ShowDialog();
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        var window = new MaxBotChatWindow { Owner = this };
        window.ShowDialog();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorizationService.CanManageMaxBotSettings(UserSession.CurrentUser))
            return;

        var dialog = new MaxBotSettingsDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            await RefreshAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
