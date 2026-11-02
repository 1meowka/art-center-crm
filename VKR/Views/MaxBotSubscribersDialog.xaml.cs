#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class MaxBotSubscribersDialog : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();
    private readonly ObservableCollection<MaxBotSubscriber> _items = new();

    public MaxBotSubscribersDialog()
    {
        InitializeComponent();
        Grid.ItemsSource = _items;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _items.Clear();
        foreach (var item in await _repository.GetSubscribersAsync())
            _items.Add(item);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var nameDialog = new MaxBotPromptDialog("Новый подписчик", "Имя (для отображения):", "Клиент");
        if (nameDialog.ShowDialog() != true)
            return;

        var chatDialog = new MaxBotPromptDialog("Chat ID", "MAX chat_id (число):", string.Empty);
        if (chatDialog.ShowDialog() != true)
            return;

        if (!long.TryParse(chatDialog.Value, out var chatId) || chatId <= 0)
        {
            MessageBox.Show("Укажите корректный chat_id — положительное число.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _repository.UpsertSubscriberAsync(new MaxBotSubscriber
            {
                MaxChatId = chatId,
                DisplayName = nameDialog.Value
            });
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not MaxBotSubscriber subscriber)
        {
            MessageBox.Show("Выберите подписчика.", "Подсказка",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                $"Удалить «{subscriber.DisplayName}» из списка?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await _repository.DeleteSubscriberAsync(subscriber.IdSubscriber);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
