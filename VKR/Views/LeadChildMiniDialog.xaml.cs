#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class LeadChildMiniDialog : Window
{
    public LeadChildItem? Result { get; private set; }

    public LeadChildMiniDialog(LeadChildItem? existing = null)
    {
        InitializeComponent();
        if (existing == null)
            return;

        FullNameBox.Text = existing.FullName;
        if (existing.BirthDate.HasValue)
            BirthDateBox.Text = existing.BirthDate.Value.ToString("dd.MM.yyyy");
        if (!string.IsNullOrWhiteSpace(existing.Phone))
            PhoneBox.Text = existing.Phone;
        ResidenceAddressBox.Text = existing.ResidenceAddress ?? "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        DateTime? birthDate = null;
        var birthRaw = BirthDateBox.Text ?? "";
        if (!birthRaw.Contains("_") && !string.IsNullOrWhiteSpace(birthRaw))
        {
            if (!DateTime.TryParseExact(birthRaw, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                birthDate = OptionalFieldDefaults.BirthDateOrDefault(null);
            }
            else
                birthDate = parsed;
        }

        string? phone = null;
        var phoneRaw = PhoneBox.Text ?? "";
        if (!string.IsNullOrWhiteSpace(phoneRaw) && !phoneRaw.Contains("_"))
            phone = LeadEditDialogHelpers.ExtractCleanPhone(phoneRaw);

        var fullName = string.IsNullOrWhiteSpace(FullNameBox.Text)
            ? string.Empty
            : InputRules.CapitalizeFirstLetter(FullNameBox.Text.Trim());

        Result = new LeadChildItem
        {
            FullName = fullName,
            BirthDate = birthDate,
            Phone = phone,
            ResidenceAddress = string.IsNullOrWhiteSpace(ResidenceAddressBox.Text)
                ? null
                : ResidenceAddressBox.Text.Trim()
        };

        DialogResult = true;
        Close();
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
