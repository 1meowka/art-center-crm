#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xceed.Wpf.Toolkit;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class LeadEditDialog : Window
{
    private readonly LeadRepository _repo = AppServices.GetRequiredService<LeadRepository>();
    private readonly StudentRepository _studentRepo = AppServices.GetRequiredService<StudentRepository>();
    private readonly Lead? _editingLead;
    private readonly int? _preselectedDirectionId;
    private List<DirectionRef> _allDirections = new();
    private IReadOnlyList<LookupItem> _streets = Array.Empty<LookupItem>();
    private IReadOnlyList<LookupItem> _institutions = Array.Empty<LookupItem>();
    private SearchableLookupCombo? _streetCombo;
    private SearchableLookupCombo? _institutionCombo;
    private SearchableLookupCombo? _sourceCombo;
    private SearchableLookupCombo? _statusCombo;
    private readonly ObservableCollection<LeadDirectionTag> _selectedDirections = new();

    public Lead? Result { get; private set; }

    public LeadEditDialog() : this(null, null) { }
    public LeadEditDialog(Lead? lead) : this(lead, null) { }

    public LeadEditDialog(Lead? lead, int? preselectedDirectionId)
    {
        InitializeComponent();
        _editingLead = lead;
        _preselectedDirectionId = preselectedDirectionId;
        DirectionTagsControl.ItemsSource = _selectedDirections;
        _streetCombo = new SearchableLookupCombo(StreetCombo);
        _institutionCombo = new SearchableLookupCombo(InstitutionCombo);
        _sourceCombo = new SearchableLookupCombo(SourceComboBox);
        _statusCombo = new SearchableLookupCombo(StatusComboBox);
        Loaded += async (_, _) => await InitializeFormAsync();
    }

    private async Task InitializeFormAsync()
    {
        NotificationSchema.Ensure();
        _allDirections = await _repo.GetDirectionsAsync();
        DirectionAddCombo.ItemsSource = _allDirections;
        if (_allDirections.Count > 0)
            DirectionAddCombo.SelectedIndex = 0;
        _sourceCombo?.SetItems((await _repo.GetSourcesAsync())
            .Select(source => new LookupItem { Id = source.IdSource, Name = source.Name }));
        _statusCombo?.SetItems(LeadStatusItems);
        _statusCombo?.SetText("новая");
        _streets = await _studentRepo.GetStreetsAsync();
        _streetCombo?.SetItems(_streets);
        _institutions = await _studentRepo.GetInstitutionsAsync();
        _institutionCombo?.SetItems(_institutions);
        InitParentRelationCombos();

        Lead? leadForForm = _editingLead;
        if (_editingLead is { IdLead: > 0 })
        {
            var fresh = await _repo.GetByIdAsync(_editingLead.IdLead);
            if (fresh != null)
                leadForForm = fresh;
        }

        var selectedIds = leadForForm != null
            ? await _repo.GetLeadDirectionIdsAsync(leadForForm.IdLead)
            : new List<int>();

        if (selectedIds.Count == 0 && _preselectedDirectionId.HasValue)
            selectedIds.Add(_preselectedDirectionId.Value);
        else if (selectedIds.Count == 0 && leadForForm?.IdDirection > 0)
            selectedIds.Add(leadForForm.IdDirection);

        _selectedDirections.Clear();
        foreach (var id in selectedIds.Distinct())
        {
            var dir = _allDirections.FirstOrDefault(x => x.IdDirection == id);
            if (dir != null)
                _selectedDirections.Add(new LeadDirectionTag { IdDirection = dir.IdDirection, Name = dir.Name });
        }

        RefreshDirectionUi();

        if (leadForForm != null)
        {
            DialogTitleText.Text = leadForForm.IdLead > 0
                ? $"Карточка гостя №{leadForForm.IdLead}"
                : "Новая карточка гостя";

            GuestFullNameTextBox.Text = leadForForm.ChildFullName;
            if (leadForForm.ChildBirthDate.HasValue)
                BirthDateMaskedBox.Text = leadForForm.ChildBirthDate.Value.ToString("dd.MM.yyyy");
            PhoneMaskedBox.Text = leadForForm.OwnPhone;
            var addressParts = StreetAddressHelper.Split(leadForForm.ChildResidenceAddress, _streets);
            _streetCombo?.SetText(addressParts.Street);
            HouseNumberBox.Text = addressParts.House;
            ApartmentNumberBox.Text = addressParts.Apartment;
            WorkplaceBox.Text = leadForForm.Workplace ?? "";
            if (leadForForm.IdInstitution is > 0)
                _institutionCombo?.SelectById(leadForForm.IdInstitution);
            CommentTextBox.Text = leadForForm.Comment ?? "";
            SchoolShiftBox.Text = leadForForm.SchoolShiftComment ?? "";
            FillParentBlock(Parent1RelationCombo, Parent1NameBox, Parent1PhoneBox, Parent1AgeBox,
                leadForForm.Parent1Relation, leadForForm.Parent1FullName, leadForForm.Parent1Phone, leadForForm.Parent1Age);
            FillParentBlock(Parent2RelationCombo, Parent2NameBox, Parent2PhoneBox, Parent2AgeBox,
                leadForForm.Parent2Relation, leadForForm.Parent2FullName, leadForForm.Parent2Phone, leadForForm.Parent2Age);
            FillParentBlock(Parent3RelationCombo, Parent3NameBox, Parent3PhoneBox, Parent3AgeBox,
                leadForForm.Parent3Relation, leadForForm.Parent3FullName, leadForForm.Parent3Phone, leadForForm.Parent3Age);

            if (leadForForm.IdSource != null)
                _sourceCombo?.SelectById(leadForForm.IdSource);
            _statusCombo?.SetText(string.IsNullOrWhiteSpace(leadForForm.Status) ? "новая" : leadForForm.Status);
        }

        ApplyGuestKindUi();
    }

    private DateTime? ReadBirthDate()
    {
        var birthRaw = BirthDateMaskedBox.Text ?? "";
        if (birthRaw.Contains("_") || string.IsNullOrWhiteSpace(birthRaw))
            return null;
        if (DateTime.TryParseExact(birthRaw, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return parsed;
        return null;
    }

    private bool CurrentIsAdult()
    {
        if (_editingLead is { IsAdultRecord: true } && !ReadBirthDate().HasValue)
            return true;
        return LeadGuestRules.IsAdult(ReadBirthDate()) || (_editingLead?.IsAdultRecord == true && LeadGuestRules.IsAdult(_editingLead.ChildBirthDate));
    }

    private void ApplyGuestKindUi()
    {
        var adult = CurrentIsAdult();
        InstitutionPanel.Visibility = adult ? Visibility.Collapsed : Visibility.Visible;
        WorkplacePanel.Visibility = adult ? Visibility.Visible : Visibility.Collapsed;
        PhoneLabelText.Text = adult ? "ТЕЛЕФОН *" : "ТЕЛЕФОН";
        GuestKindHint.Text = adult
            ? "Взрослому учреждение не указывается. Место работы можно не заполнять."
            : "Укажите родителей в блоках справа: принадлежность, ФИО, телефон, возраст.";
    }

    private void BirthDateMaskedBox_LostFocus(object sender, RoutedEventArgs e) => ApplyGuestKindUi();

    private void RefreshDirectionUi()
    {
        NoDirectionsText.Visibility = _selectedDirections.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddDirection_Click(object sender, RoutedEventArgs e)
    {
        if (DirectionAddCombo.SelectedItem is not DirectionRef dir)
            return;

        if (_selectedDirections.Any(x => x.IdDirection == dir.IdDirection))
            return;

        _selectedDirections.Add(new LeadDirectionTag { IdDirection = dir.IdDirection, Name = dir.Name });
        RefreshDirectionUi();
        HideError();
    }

    private void RemoveDirection_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        LeadDirectionTag? tag = (sender as FrameworkElement)?.DataContext as LeadDirectionTag;
        if (tag == null && sender is Button btn && btn.Tag is LeadDirectionTag tagFromTag)
            tag = tagFromTag;

        if (tag == null)
            return;

        var item = _selectedDirections.FirstOrDefault(x => x.IdDirection == tag.IdDirection);
        if (item != null)
            _selectedDirections.Remove(item);
        RefreshDirectionUi();
        HideError();
    }

    private void MaskedBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is MaskedTextBox box)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!box.IsLoaded) return;
                box.SelectionStart = 0;
                box.SelectionLength = 0;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void MaskedBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is MaskedTextBox box &&
            (string.IsNullOrEmpty(box.Text) || box.Text.All(c => !char.IsDigit(c))))
        {
            box.SelectionStart = 0;
            box.SelectionLength = 0;
            e.Handled = true;
        }
    }

    private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = InputRules.CapitalizeFirstLetter(tb.Text);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(sender as Button);

    private async Task SaveAsync(Button? saveButton)
    {
        HideError();
        var birthDate = ReadBirthDate();
        var isAdult = LeadGuestRules.IsAdult(birthDate) || (_editingLead?.IsAdultRecord == true && birthDate == null && CurrentIsAdult());

        if (string.IsNullOrWhiteSpace(GuestFullNameTextBox.Text))
        {
            ShowError("Укажите ФИО гостя.");
            return;
        }

        string? phone = null;
        var phoneRaw = PhoneMaskedBox.Text ?? "";
        if (!string.IsNullOrWhiteSpace(phoneRaw) && !phoneRaw.Contains("_"))
            phone = ExtractCleanPhone(phoneRaw);

        if (isAdult && string.IsNullOrWhiteSpace(phone))
        {
            ShowError("У взрослого гостя телефон обязателен.");
            return;
        }

        if (!isAdult)
        {
            if (string.IsNullOrWhiteSpace(Parent1NameBox.Text) &&
                string.IsNullOrWhiteSpace(Parent2NameBox.Text) &&
                string.IsNullOrWhiteSpace(Parent3NameBox.Text))
            {
                ShowError("Заполните хотя бы один блок родителя (ФИО).");
                return;
            }
        }

        if (saveButton != null)
            saveButton.IsEnabled = false;

        try
        {
            var lead = BuildLeadFromForm(birthDate, isAdult, phone);
            var id = await _repo.SaveWithRepresentativeAsync(lead, _studentRepo);
            lead.IdLead = id;
            Result = lead;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.SaveFailed));
        }
        finally
        {
            if (saveButton != null)
                saveButton.IsEnabled = true;
        }
    }

    private Lead BuildLeadFromForm(DateTime? birthDate, bool isAdult, string? phone)
    {
        var fullName = InputRules.CapitalizeFirstLetter(GuestFullNameTextBox.Text.Trim());
        var fio = FioHelper.Parse(fullName);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
        var firstStored = string.IsNullOrWhiteSpace(fio.MiddleName)
            ? firstName
            : $"{firstName} {fio.MiddleName}".Trim();

        var directionIds = _selectedDirections.Select(x => x.IdDirection).Distinct().OrderBy(x => x).ToList();
        var status = ResolveLeadStatus(_statusCombo?.GetText());
        var address = StreetAddressHelper.Combine(
            _streetCombo?.GetText(),
            HouseNumberBox.Text,
            ApartmentNumberBox.Text);

        var lead = _editingLead ?? new Lead { CreatedDate = DateTime.Now };
        lead.ChildLastName = lastName;
        lead.ChildFirstName = string.IsNullOrWhiteSpace(firstStored) ? lastName : firstStored;
        lead.ChildBirthDate = birthDate;
        lead.ContactPhone = phone ?? string.Empty;
        lead.ChildPhone = phone;
        lead.ChildResidenceAddress = address;
        lead.IsAdultRecord = isAdult;
        lead.IdInstitution = isAdult ? null : _institutionCombo?.GetSelectedId();
        lead.Workplace = isAdult
            ? (string.IsNullOrWhiteSpace(WorkplaceBox.Text) ? null : WorkplaceBox.Text.Trim())
            : null;
        lead.IdDirection = directionIds.FirstOrDefault();
        lead.DirectionIds = directionIds;
        lead.IdSource = _sourceCombo?.GetSelectedId();
        lead.Status = status;
        lead.ConsentPersonalData = false;
        lead.Comment = string.IsNullOrWhiteSpace(CommentTextBox.Text) ? null : CommentTextBox.Text.Trim();
        lead.SchoolShiftComment = string.IsNullOrWhiteSpace(SchoolShiftBox.Text) ? null : SchoolShiftBox.Text.Trim();
        lead.Parent1Relation = Parent1RelationCombo.Text?.Trim();
        lead.Parent1FullName = EmptyToNull(Parent1NameBox.Text);
        lead.Parent1Phone = ExtractOptionalPhone(Parent1PhoneBox.Text);
        lead.Parent1Age = ParseAge(Parent1AgeBox.Text);
        lead.Parent2Relation = Parent2RelationCombo.Text?.Trim();
        lead.Parent2FullName = EmptyToNull(Parent2NameBox.Text);
        lead.Parent2Phone = ExtractOptionalPhone(Parent2PhoneBox.Text);
        lead.Parent2Age = ParseAge(Parent2AgeBox.Text);
        lead.Parent3Relation = Parent3RelationCombo.Text?.Trim();
        lead.Parent3FullName = EmptyToNull(Parent3NameBox.Text);
        lead.Parent3Phone = ExtractOptionalPhone(Parent3PhoneBox.Text);
        lead.Parent3Age = ParseAge(Parent3AgeBox.Text);
        lead.SyncParentBlocks();
        lead.Relations = new List<LeadRelationItem>();
        return lead;
    }

    private void InitParentRelationCombos()
    {
        var items = new[] { "мама", "папа", "бабушка", "дедушка", "опекун", "другое" };
        foreach (var combo in new[] { Parent1RelationCombo, Parent2RelationCombo, Parent3RelationCombo })
        {
            combo.ItemsSource = items;
            combo.IsEditable = true;
        }
        Parent1RelationCombo.Text = "мама";
        Parent2RelationCombo.Text = "папа";
    }

    private static void FillParentBlock(
        ComboBox relation, TextBox name, MaskedTextBox phone, TextBox age,
        string? relationValue, string? nameValue, string? phoneValue, short? ageValue)
    {
        if (!string.IsNullOrWhiteSpace(relationValue))
            relation.Text = relationValue;
        name.Text = nameValue ?? "";
        phone.Text = phoneValue ?? "";
        age.Text = ageValue?.ToString() ?? "";
    }

    private static string? EmptyToNull(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static short? ParseAge(string? text) =>
        short.TryParse(text, out var age) ? age : null;

    private static string? ExtractOptionalPhone(string? masked)
    {
        var raw = masked ?? "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('_'))
            return null;
        var phone = ExtractCleanPhone(raw);
        return string.IsNullOrWhiteSpace(phone) ? null : phone;
    }

    private static readonly LookupItem[] LeadStatusItems =
    {
        new() { Id = 1, Name = "новая" },
        new() { Id = 2, Name = "подтверждена" },
        new() { Id = 3, Name = "посещено" },
        new() { Id = 4, Name = "конвертирована" },
        new() { Id = 5, Name = "отказ" }
    };

    private static string ResolveLeadStatus(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        var match = LeadStatusItems.FirstOrDefault(item =>
            string.Equals(item.Name, value, StringComparison.CurrentCultureIgnoreCase));
        return match?.Name ?? "новая";
    }

    private static string ExtractCleanPhone(string masked)
        => LeadEditDialogHelpers.ExtractCleanPhone(masked);

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = System.Windows.WindowState.Minimized;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
