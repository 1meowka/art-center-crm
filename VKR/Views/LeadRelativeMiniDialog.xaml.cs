#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class LeadRelativeMiniDialog : Window
{
    private readonly IReadOnlyList<LookupItem> _allRelationTypes;
    private readonly bool _currentIsAdult;
    private readonly LeadRelationItem? _existing;
    private readonly SearchableLookupCombo _institutionCombo;

    public LeadRelationItem? Result { get; private set; }

    public LeadRelativeMiniDialog(
        IReadOnlyList<LookupItem> relationTypes,
        IReadOnlyList<LookupItem> institutions,
        bool currentIsAdult,
        LeadRelationItem? existing = null)
    {
        InitializeComponent();
        _allRelationTypes = relationTypes;
        _currentIsAdult = currentIsAdult;
        _existing = existing;
        _institutionCombo = new SearchableLookupCombo(InstitutionCombo);
        _institutionCombo.SetItems(institutions);
        AdultRadio.IsChecked = existing?.IsAdult ?? !currentIsAdult;
        ChildRadio.IsChecked = existing != null ? !existing.IsAdult : currentIsAdult;

        if (existing is { IdRelatedLead: > 0 })
        {
            DialogTitleText.Text = "Связь с родственником";
            DialogHintText.Text = "Личные данные этой карточки не меняются. Откройте карточку родственника, чтобы править ФИО, телефон или адрес.";
            KindPanel.Visibility = Visibility.Collapsed;
            PersonPanel.Visibility = Visibility.Collapsed;
            ExistingNameText.Visibility = Visibility.Visible;
            ExistingNameText.Text = $"{existing.FullName} · {existing.KindText}";
        }
        else if (existing != null)
        {
            FullNameBox.Text = existing.FullName;
            if (existing.BirthDate.HasValue)
                BirthDateBox.Text = existing.BirthDate.Value.ToString("dd.MM.yyyy");
            if (!string.IsNullOrWhiteSpace(existing.Phone))
                PhoneBox.Text = existing.Phone;
            WorkplaceBox.Text = existing.Workplace ?? "";
            ResidenceAddressBox.Text = existing.ResidenceAddress ?? "";
            if (existing.IdInstitution is > 0)
                _institutionCombo.SelectById(existing.IdInstitution);
            MainPayerCheck.IsChecked = existing.IsMainPayer;
            LegalCheck.IsChecked = existing.IsLegalRepresentative;
        }

        ApplyKindUi();
        BindRelationTypes();
        if (existing != null)
        {
            RelationCombo.SelectedItem = RelationTypeCatalog.Resolve(
                (IReadOnlyList<LookupItem>)(RelationCombo.ItemsSource ?? Array.Empty<LookupItem>()),
                existing.RelationTypeId,
                _allRelationTypes);
            MainPayerCheck.IsChecked = existing.IsMainPayer;
            LegalCheck.IsChecked = existing.IsLegalRepresentative;
        }
    }

    private bool RelativeIsAdult => AdultRadio.IsChecked == true;

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        ApplyKindUi();
        BindRelationTypes();
    }

    private void ApplyKindUi()
    {
        var adult = RelativeIsAdult;
        WorkplacePanel.Visibility = adult ? Visibility.Visible : Visibility.Collapsed;
        InstitutionPanel.Visibility = adult ? Visibility.Collapsed : Visibility.Visible;
        PhoneLabel.Text = adult ? "ТЕЛЕФОН *" : "ТЕЛЕФОН";

        var showRoles = (_currentIsAdult && !adult) || (!_currentIsAdult && adult);
        RolesPanel.Visibility = showRoles ? Visibility.Visible : Visibility.Collapsed;

        RelationLabel.Text = _currentIsAdult && !adult
            ? "КТО ВЫ ДЛЯ РЕБЁНКА *"
            : "СТЕПЕНЬ РОДСТВА *";
    }

    private void BindRelationTypes()
    {
        IEnumerable<string> names;
        if (_currentIsAdult && !RelativeIsAdult)
            names = new[] { RelationTypeCatalog.Mother, RelationTypeCatalog.Father, RelationTypeCatalog.Other };
        else if (!_currentIsAdult && RelativeIsAdult)
            names = new[] { RelationTypeCatalog.Mother, RelationTypeCatalog.Father, RelationTypeCatalog.Other };
        else
            names = new[] { RelationTypeCatalog.Brother, RelationTypeCatalog.Sister, RelationTypeCatalog.Other };

        var items = _allRelationTypes
            .Where(x => names.Any(n => string.Equals(n, x.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            .GroupBy(x => x.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .ToList();
        RelationCombo.ItemsSource = items;
        if (RelationCombo.SelectedItem == null && items.Count > 0)
            RelationCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        var relation = RelationCombo.SelectedItem as LookupItem;
        if (!RelationTypeCatalog.HasSelection(relation))
        {
            ShowError("Укажите степень родства.");
            return;
        }

        var isAdult = _existing is { IdRelatedLead: > 0 } ? _existing.IsAdult : RelativeIsAdult;

        if (_existing is { IdRelatedLead: > 0 })
        {
            Result = CloneExisting(_existing);
            Result.RelationTypeId = relation!.Id;
            Result.RelationName = relation.Name;
            Result.IsMainPayer = RolesPanel.Visibility == Visibility.Visible && MainPayerCheck.IsChecked == true;
            Result.IsLegalRepresentative = RolesPanel.Visibility == Visibility.Visible && LegalCheck.IsChecked == true;
            DialogResult = true;
            Close();
            return;
        }

        var fullNameRaw = FullNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullNameRaw))
        {
            ShowError("Укажите ФИО родственника.");
            return;
        }

        var fio = FioHelper.Parse(fullNameRaw);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);

        DateTime? birthDate = null;
        var birthRaw = BirthDateBox.Text ?? "";
        if (!birthRaw.Contains("_") && !string.IsNullOrWhiteSpace(birthRaw))
        {
            if (!DateTime.TryParseExact(birthRaw, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                ShowError("Дата рождения указана неверно.");
                return;
            }
            birthDate = parsed;
        }

        string? phone = null;
        var phoneRaw = PhoneBox.Text ?? "";
        if (!string.IsNullOrWhiteSpace(phoneRaw) && !phoneRaw.Contains("_"))
            phone = LeadEditDialogHelpers.ExtractCleanPhone(phoneRaw);

        if (isAdult && string.IsNullOrWhiteSpace(phone))
        {
            ShowError("У взрослого родственника укажите телефон.");
            return;
        }

        Result = new LeadRelationItem
        {
            IdRelatedLead = _existing?.IdRelatedLead ?? 0,
            IsAdult = isAdult,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = fio.MiddleName,
            BirthDate = birthDate,
            Phone = phone,
            ResidenceAddress = string.IsNullOrWhiteSpace(ResidenceAddressBox.Text)
                ? null
                : ResidenceAddressBox.Text.Trim(),
            Workplace = isAdult
                ? (string.IsNullOrWhiteSpace(WorkplaceBox.Text) ? null : WorkplaceBox.Text.Trim())
                : null,
            IdInstitution = !isAdult ? _institutionCombo.GetSelectedId() : null,
            InstitutionName = !isAdult && _institutionCombo.GetSelectedId() is > 0
                ? _institutionCombo.GetText()
                : null,
            RelationTypeId = relation!.Id,
            RelationName = relation.Name,
            IsMainPayer = RolesPanel.Visibility == Visibility.Visible && MainPayerCheck.IsChecked == true,
            IsLegalRepresentative = RolesPanel.Visibility == Visibility.Visible && LegalCheck.IsChecked == true
        };

        DialogResult = true;
        Close();
    }

    private static LeadRelationItem CloneExisting(LeadRelationItem item) =>
        new()
        {
            IdLeadRelation = item.IdLeadRelation,
            IdRelatedLead = item.IdRelatedLead,
            IsIncoming = item.IsIncoming,
            IsAdult = item.IsAdult,
            LastName = item.LastName,
            FirstName = item.FirstName,
            MiddleName = item.MiddleName,
            BirthDate = item.BirthDate,
            Phone = item.Phone,
            ResidenceAddress = item.ResidenceAddress,
            IdInstitution = item.IdInstitution,
            InstitutionName = item.InstitutionName,
            Workplace = item.Workplace
        };

    private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = InputRules.CapitalizeFirstLetter(tb.Text);
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
