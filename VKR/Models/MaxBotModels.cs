#nullable enable
using System;
using System.Collections.Generic;

namespace VKR.Models;

public sealed class MaxBotSettings
{
    public string BotToken { get; set; } = string.Empty;
    public string? BotName { get; set; }
    public string? BotUsername { get; set; }

    public string OrgName { get; set; } = "Творческий центр «Арт-студия»";
    public string? OrgAddress { get; set; } = "г. Примерск, ул. Центральная, д. 1";
    public string? OrgInn { get; set; }
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string? PrivacyUrl { get; set; }
    public string? TermsUrl { get; set; }
    public string? WelcomeText { get; set; }
    public string? DiscountsText { get; set; }

    /// <summary>Токен в БД есть, но не расшифровался (перенос БД с другого ПК).</summary>
    public bool TokenNeedsReentry { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    public MaxBotComplianceInfo ToComplianceInfo() => new()
    {
        OrgName = OrgName,
        OrgAddress = OrgAddress,
        OrgInn = OrgInn,
        SupportPhone = SupportPhone,
        SupportEmail = SupportEmail,
        PrivacyUrl = PrivacyUrl,
        TermsUrl = TermsUrl,
        WelcomeText = WelcomeText
    };
}

/// <summary>Юридические и контактные данные для автоответов бота (требования MAX).</summary>
public sealed class MaxBotComplianceInfo
{
    public string OrgName { get; set; } = "Творческий центр «Арт-студия»";
    public string? OrgAddress { get; set; }
    public string? OrgInn { get; set; }
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string? PrivacyUrl { get; set; }
    public string? TermsUrl { get; set; }
    public string? WelcomeText { get; set; }

    public string OrgDisplayName => string.IsNullOrWhiteSpace(OrgName)
        ? "организации"
        : OrgName.Trim();
}

public sealed class MaxBotTemplateFolder
{
    public int IdFolder { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int TemplateCount { get; set; }
}

public sealed class MaxBotTemplate
{
    public int IdTemplate { get; set; }
    public int IdFolder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class MaxBotSubscriber
{
    public int IdSubscriber { get; set; }
    public long MaxChatId { get; set; }
    public long? MaxUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int? IdRepresentative { get; set; }
    public int? IdStudent { get; set; }
    public DateTime SubscribedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public sealed class MaxBotSendResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
}

public sealed class MaxBotConnectionInfo
{
    public bool Success { get; set; }
    public string? BotName { get; set; }
    public string? BotUsername { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class MaxBotMessage
{
    public int IdMessage { get; set; }
    public long MaxChatId { get; set; }
    public int? IdSubscriber { get; set; }
    public string Direction { get; set; } = "in";
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }

    public bool IsIncoming => Direction == "in";
}

public sealed class MaxBotConversation : System.ComponentModel.INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _lastMessagePreview = string.Empty;
    private DateTime? _lastMessageAt;
    private int _unreadCount;

    public int IdSubscriber { get; set; }
    public long MaxChatId { get; set; }
    public long? MaxUserId { get; set; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value, nameof(DisplayName)))
                OnPropertyChanged(nameof(Initials));
        }
    }

    public string LastMessagePreview
    {
        get => _lastMessagePreview;
        set => SetField(ref _lastMessagePreview, value, nameof(LastMessagePreview));
    }

    public DateTime? LastMessageAt
    {
        get => _lastMessageAt;
        set
        {
            if (SetField(ref _lastMessageAt, value, nameof(LastMessageAt)))
                OnPropertyChanged(nameof(LastMessageTimeText));
        }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (SetField(ref _unreadCount, value, nameof(UnreadCount)))
            {
                OnPropertyChanged(nameof(UnreadBadge));
                OnPropertyChanged(nameof(UnreadBadgeText));
                OnPropertyChanged(nameof(HasUnread));
            }
        }
    }

    public string UnreadBadge => UnreadCount > 0 ? UnreadCount.ToString() : string.Empty;

    public string UnreadBadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public bool HasUnread => UnreadCount > 0;

    public string Initials
    {
        get
        {
            var name = DisplayName?.Trim();
            if (string.IsNullOrEmpty(name))
                return "?";

            return char.ToUpperInvariant(name[0]).ToString();
        }
    }

    public string LastMessageTimeText =>
        LastMessageAt.HasValue
            ? LastMessageAt.Value.ToLocalTime().ToString("dd.MM, HH:mm")
            : string.Empty;

    public void SyncFrom(MaxBotConversation other)
    {
        MaxUserId = other.MaxUserId;
        DisplayName = other.DisplayName;
        LastMessagePreview = other.LastMessagePreview;
        LastMessageAt = other.LastMessageAt;
        UnreadCount = other.UnreadCount;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
