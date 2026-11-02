#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VKR.Models;

public sealed class BirthdayItem : INotifyPropertyChanged
{
    public int IdStudent { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public int AgeOnDate { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MainPayerName { get; set; } = string.Empty;
    public string MainPayerPhone { get; set; } = string.Empty;
    public int YearOf { get; set; }

    // Эти два поля теперь "уведомляющие": если код в обработчике
    // присваивает им значение — UI сам перерисует и галочку, и колонку даты.
    private bool _isGreeted;
    public bool IsGreeted
    {
        get => _isGreeted;
        set { if (_isGreeted != value) { _isGreeted = value; OnChanged(); } }
    }

    private DateTime? _greetingDate;
    public DateTime? GreetingDate
    {
        get => _greetingDate;
        set { if (_greetingDate != value) { _greetingDate = value; OnChanged(); OnChanged(nameof(GreetingDateText)); } }
    }

    public string BirthDateText => BirthDate.ToString("dd.MM.yyyy");
    public string AgeText => AgeOnDate > 0 ? $"{AgeOnDate} {RuYears(AgeOnDate)}" : "—";
    public string PhoneText => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
    public string PayerText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MainPayerName)) return "—";
            return string.IsNullOrWhiteSpace(MainPayerPhone)
                ? MainPayerName
                : $"{MainPayerName} ({MainPayerPhone})";
        }
    }
    public string GreetingDateText => GreetingDate?.ToString("dd.MM.yyyy") ?? "—";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string RuYears(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 14) return "лет";
        return mod10 switch
        {
            1 => "год",
            >= 2 and <= 4 => "года",
            _ => "лет"
        };
    }
}