#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class LeadRepresentativeMiniDialog : Window
{
    private readonly IReadOnlyList<LookupItem> _relationTypes;

    public LeadRepresentativeItem? Result { get; private set; }

    public LeadRepresentativeMiniDialog(
        IReadOnlyList<LookupItem> relationTypes,
        LeadRepresentativeItem? existing = null)
    {
        InitializeComponent();
        _relationTypes = relationTypes;
        RelationCombo.ItemsSource = relationTypes;
        RelationCombo.SelectedItem = null;

        if (existing != null)
        {
            FullNameBox.Text = existing.FullName;
            PhoneBox.Text = existing.Phone;
            ResidenceAddressBox.Text = existing.ResidenceAddress ?? existing.Email ?? "";
            RelationCombo.SelectedItem = RelationTypeCatalog.Resolve(
                relationTypes, existing.RelationTypeId, relationTypes);

            if (RelationCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(existing.RelationName))
            {
                var match = relationTypes.FirstOrDefault(x => x.Name == existing.RelationName)
                    ?? relationTypes.FirstOrDefault(x => RelationTypeCatalog.IsOther(x.Name));
                RelationCombo.SelectedItem = match;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        var fullNameRaw = FullNameBox.Text?.Trim() ?? string.Empty;
        var fio = FioHelper.Parse(fullNameRaw);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);

        string? phone = null;
        var phoneRaw = PhoneBox.Text ?? "";
        if (!string.IsNullOrWhiteSpace(phoneRaw) && !phoneRaw.Contains("_"))
            phone = LeadEditDialogHelpers.ExtractCleanPhone(phoneRaw);

        var relation = RelationCombo.SelectedItem as LookupItem;
        if (!RelationTypeCatalog.HasSelection(relation))
        {
            ShowError("Укажите степень родства.");
            return;
        }

        Result = new LeadRepresentativeItem
        {
            LastName = lastName,
            FirstName = firstName,
            MiddleName = fio.MiddleName,
            Phone = phone ?? string.Empty,
            ResidenceAddress = string.IsNullOrWhiteSpace(ResidenceAddressBox.Text)
                ? null
                : ResidenceAddressBox.Text.Trim(),
            RelationTypeId = relation?.Id,
            RelationName = relation?.Name ?? ""
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

internal static class LeadEditDialogHelpers
{
    public static string ExtractCleanPhone(string masked)
    {
        var digits = new string(masked.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("7") && digits.Length == 11)
            return "+" + digits;
        if (digits.Length == 10)
            return "+7" + digits;
        return digits.Length > 0 ? "+" + digits : "";
    }
}
