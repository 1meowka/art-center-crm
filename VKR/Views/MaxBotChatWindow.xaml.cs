#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class MaxBotChatWindow : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();
    private readonly MaxBotService _botService = AppServices.GetRequiredService<MaxBotService>();
    private readonly MaxBotPollingService _poller = AppServices.GetRequiredService<MaxBotPollingService>();

    private readonly ObservableCollection<MaxBotConversation> _conversations = new();
    private readonly List<MaxBotConversation> _allConversations = new();
    private readonly List<MaxBotMessage> _allMessages = new();

    private MaxBotConversation? _selectedConversation;
    private bool _isSending;
    private bool _isSelectionUpdating;
    private bool _botConfigured;

    private int _displayedMessageCount;
    private int _displayedLastMessageId;
    private DispatcherTimer? _refreshTimer;
    private bool _isRefreshing;
    private readonly bool _useExternalWorker = MaxBotOptions.UseExternalWorker(App.Configuration);

    public MaxBotChatWindow()
    {
        InitializeComponent();
        ConversationsList.ItemsSource = _conversations;
        SectionHelp.AttachTopic(this, SectionHelp.MaxBotChatTitle, SectionHelp.MaxBotChatBody);
        Loaded += OnLoadedAsync;
        Closed += OnClosed;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _repository.GetSettingsAsync();
            _botConfigured = settings is { IsConfigured: true };
            PollingStatusText.Text = !_botConfigured
                ? "Бот не настроен — укажите токен в настройках"
                : _useExternalWorker
                    ? "Диалоги из базы (работает фоновая служба MAX)"
                    : "Слушаем MAX";

            if (_useExternalWorker)
            {
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _refreshTimer.Tick += OnRefreshTimerTick;
                _refreshTimer.Start();
            }
            else
                _poller.PollCompleted += OnPollCompleted;

            await ReloadConversationsAsync();
            UpdateReplyState();
        }
        catch (Exception ex)
        {
            PollingStatusText.Text = "Ошибка загрузки";
            ShowSendError(UserFacingErrors.GetMessage(ex, "Не удалось открыть чат MAX."));
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer?.Stop();
        if (!_useExternalWorker)
            _poller.PollCompleted -= OnPollCompleted;
    }

    private async void OnPollCompleted(object? sender, MaxBotPollEventArgs e)
    {
        try
        {
            if (e.ProcessedUpdates > 0)
                await ReloadConversationsAsync();

            if (_selectedConversation != null)
                await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: e.ProcessedUpdates > 0);

            var settings = await _repository.GetSettingsAsync();
            _botConfigured = settings is { IsConfigured: true };
            PollingStatusText.Text = _botConfigured ? "Слушаем MAX" : "Бот не настроен";
            UpdateReplyState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_isRefreshing)
            return;

        try
        {
            _isRefreshing = true;
            await ReloadConversationsAsync();

            if (_selectedConversation != null)
                await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task ReloadConversationsAsync()
    {
        var selectedId = _selectedConversation?.IdSubscriber;
        var newestFirst = ConversationSortCombo.SelectedIndex != 1;
        var incoming = await _repository.GetConversationsAsync(newestFirst);

        _allConversations.Clear();
        _allConversations.AddRange(incoming);
        ApplyConversationFilter();

        if (!selectedId.HasValue)
            return;

        var restored = _conversations.FirstOrDefault(c => c.IdSubscriber == selectedId.Value);
        if (restored == null)
            return;

        _selectedConversation = restored;
        if (ConversationsList.SelectedItem != restored && !_isSelectionUpdating)
        {
            _isSelectionUpdating = true;
            try
            {
                ConversationsList.SelectedItem = restored;
            }
            finally
            {
                _isSelectionUpdating = false;
            }
        }
    }

    private void ApplyConversationFilter()
    {
        var query = ConversationSearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<MaxBotConversation> filtered = _allConversations;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(c =>
                c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.LastMessagePreview.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        if (Dispatcher.CheckAccess())
            MaxBotChatSync.UpdateConversationList(_conversations, list);
        else
            Dispatcher.Invoke(() => MaxBotChatSync.UpdateConversationList(_conversations, list));
    }

    private async void ConversationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectionUpdating)
            return;

        HideSendError();

        if (ConversationsList.SelectedItem is not MaxBotConversation conversation)
        {
            _selectedConversation = null;
            MarkUnreadButton.IsEnabled = false;
            MessageSearchBox.IsEnabled = false;
            MessageSortCombo.IsEnabled = false;
            EmptyChatHint.Visibility = Visibility.Visible;
            MessagesList.Items.Clear();
            _allMessages.Clear();
            _displayedMessageCount = 0;
            _displayedLastMessageId = 0;
            UpdateChatHeader(null);
            UpdateReplyState();
            return;
        }

        _selectedConversation = conversation;
        UpdateChatHeader(conversation);
        MarkUnreadButton.IsEnabled = true;
        MessageSearchBox.IsEnabled = true;
        MessageSortCombo.IsEnabled = true;
        EmptyChatHint.Visibility = Visibility.Collapsed;

        _displayedMessageCount = 0;
        _displayedLastMessageId = 0;

        await _repository.MarkChatReadAsync(conversation.MaxChatId);
        conversation.UnreadCount = 0;
        await _poller.RefreshUnreadCountAsync();

        await RefreshMessagesIfNeededAsync(conversation, scrollToEnd: true, forceReload: true);
        UpdateReplyState();
        if (ReplyBox.IsEnabled)
            ReplyBox.Focus();
    }

    private async Task RefreshMessagesIfNeededAsync(
        MaxBotConversation conversation,
        bool scrollToEnd,
        bool forceReload = false)
    {
        var oldestFirst = MessageSortCombo.SelectedIndex != 1;
        var messages = await _repository.GetMessagesAsync(conversation.MaxChatId, oldestFirst);

        _allMessages.Clear();
        _allMessages.AddRange(messages);

        void Apply()
        {
            var filtered = FilterMessages(_allMessages);
            if (!forceReload
                && !MaxBotChatSync.NeedsMessageReload(_displayedMessageCount, _displayedLastMessageId, filtered))
            {
                return;
            }

            MessagesList.Items.Clear();
            foreach (var message in filtered)
                MessagesList.Items.Add(CreateMessageBubble(message));

            _displayedMessageCount = filtered.Count;
            _displayedLastMessageId = filtered.Count > 0 ? filtered[^1].IdMessage : 0;

            if (scrollToEnd && filtered.Count > 0)
                MessagesScroll.ScrollToEnd();
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            await Dispatcher.InvokeAsync(Apply);
    }

    private IReadOnlyList<MaxBotMessage> FilterMessages(IReadOnlyList<MaxBotMessage> messages)
    {
        var query = MessageSearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            return messages;

        return messages
            .Where(m => m.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void UpdateChatHeader(MaxBotConversation? conversation)
    {
        if (conversation == null)
        {
            ChatAvatarBorder.Visibility = Visibility.Collapsed;
            ChatTitleText.Text = "Выберите диалог слева";
            ChatSubtitleText.Text = "Сообщения клиента — слева, ответы центра — справа";
            return;
        }

        ChatAvatarInitials.Text = conversation.Initials;
        ChatAvatarBorder.Visibility = Visibility.Visible;
        ChatTitleText.Text = conversation.DisplayName;
        ChatSubtitleText.Text = conversation.HasUnread
            ? $"Непрочитанных: {conversation.UnreadCount}"
            : string.IsNullOrWhiteSpace(conversation.LastMessageTimeText)
                ? "Клиент в MAX"
                : $"Последнее сообщение: {conversation.LastMessageTimeText}";
    }

    private Border CreateMessageBubble(MaxBotMessage message)
    {
        var isIncoming = message.IsIncoming;
        var labelBrush = (Brush)FindResource(isIncoming ? "ThemeInfoFgBrush" : "ThemeOnPrimaryBrush");
        var bodyBrush = (Brush)FindResource(isIncoming ? "ThemeTextBrush" : "ThemeOnPrimaryBrush");
        var timeBrush = (Brush)FindResource(isIncoming ? "ThemeTextMutedBrush" : "ThemeOnPrimaryBrush");

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = isIncoming ? "КЛИЕНТ" : "ЦЕНТР",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = labelBrush,
            Opacity = isIncoming ? 1.0 : 0.8,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = message.Body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 20,
            Foreground = bodyBrush
        });

        if (isIncoming)
        {
            panel.Children.Add(new TextBlock
            {
                Text = message.IsRead ? "✓ Прочитано" : "● Новое",
                FontSize = 10,
                FontWeight = message.IsRead ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = message.IsRead
                    ? (Brush)FindResource("ThemeTextMutedBrush")
                    : (Brush)FindResource("ThemeDangerFgBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = message.SentAt.ToLocalTime().ToString("dd.MM, HH:mm"),
            FontSize = 10,
            Foreground = timeBrush,
            Opacity = isIncoming ? 0.9 : 0.75,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        });

        return new Border
        {
            Style = (Style)FindResource(isIncoming ? "ChatBubbleInStyle" : "ChatBubbleOutStyle"),
            Child = panel
        };
    }

    private void UpdateReplyState()
    {
        var hasConversation = _selectedConversation != null;
        var canSend = hasConversation && _botConfigured && !_isSending;

        ReplyBox.IsEnabled = hasConversation && !_isSending;
        SendReplyButton.IsEnabled = canSend;
        ReplyHintText.Text = MaxBotErrors.ExplainReplyDisabled(hasConversation, _botConfigured);
        UpdateReplyPlaceholder();
    }

    private void UpdateReplyPlaceholder()
    {
        var hasConversation = _selectedConversation != null;
        ReplyPlaceholder.Text = hasConversation
            ? "Введите ответ клиенту…"
            : "Выберите диалог слева…";

        ReplyPlaceholder.Visibility = !ReplyBox.IsEnabled
                                      || !string.IsNullOrEmpty(ReplyBox.Text)
                                      || ReplyBox.IsKeyboardFocusWithin
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ReplyBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateReplyPlaceholder();

    private void ReplyBox_FocusChanged(object sender, RoutedEventArgs e) => UpdateReplyPlaceholder();

    private void ShowSendError(string message)
    {
        SendErrorText.Text = message;
        SendErrorText.Visibility = Visibility.Visible;
    }

    private void HideSendError()
    {
        SendErrorText.Text = string.Empty;
        SendErrorText.Visibility = Visibility.Collapsed;
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e) => await SendReplyAsync();

    private async void ReplyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        e.Handled = true;
        await SendReplyAsync();
    }

    private async Task SendReplyAsync()
    {
        HideSendError();

        if (_isSending)
            return;

        if (_selectedConversation == null)
        {
            ShowSendError("Сначала выберите диалог слева.");
            return;
        }

        if (!_botConfigured)
        {
            ShowSendError(MaxBotErrors.ExplainReplyDisabled(true, false));
            return;
        }

        var text = ReplyBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowSendError("Введите текст сообщения.");
            ReplyBox.Focus();
            return;
        }

        try
        {
            _isSending = true;
            UpdateReplyState();
            await _botService.SendChatReplyAsync(_selectedConversation, text);
            ReplyBox.Clear();
            await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: true, forceReload: true);
            await ReloadConversationsAsync();
        }
        catch (Exception ex)
        {
            ShowSendError(MaxBotErrors.ForSend(ex));
            MessageBox.Show(MaxBotErrors.ForSend(ex), "Не удалось отправить",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isSending = false;
            UpdateReplyState();
        }
    }

    private async void MarkUnread_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConversation == null)
            return;

        try
        {
            await _repository.MarkChatUnreadAsync(_selectedConversation.MaxChatId);
            await ReloadConversationsAsync();
            _selectedConversation = _conversations.FirstOrDefault(c => c.IdSubscriber == _selectedConversation.IdSubscriber)
                ?? _selectedConversation;
            await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: false, forceReload: true);
            UpdateChatHeader(_selectedConversation);
            await _poller.RefreshUnreadCountAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ConversationSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        await ReloadConversationsAsync();
    }

    private void ConversationSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyConversationFilter();

    private async void MessageSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _selectedConversation == null)
            return;

        _displayedMessageCount = 0;
        _displayedLastMessageId = 0;
        await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: true, forceReload: true);
    }

    private async void MessageSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _selectedConversation == null)
            return;

        _displayedMessageCount = 0;
        _displayedLastMessageId = 0;
        await RefreshMessagesIfNeededAsync(_selectedConversation, scrollToEnd: false, forceReload: true);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
