#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xceed.Wpf.Toolkit;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class StudentEditDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly int? _idStudent;
    private readonly StudentRepresentativeLink? _initialRepresentativeLink;
    private IReadOnlyList<LookupItem> _allRelationTypes = Array.Empty<LookupItem>();
    private IReadOnlyList<LookupItem> _relationTypes = Array.Empty<LookupItem>();
    private readonly ObservableCollection<StudentRepresentativeLink> _links = new();
    private SearchableLookupCombo? _institutionCombo;

    // Минимальный/максимальный возраст обучающегося
    private const int MIN_AGE = 3;
    private const int MAX_AGE = 80;

    public int SavedStudentId { get; private set; }

    public StudentEditDialog(
        StudentRepository repository,
        int? idStudent = null,
        StudentRepresentativeLink? initialRepresentativeLink = null)
    {
        InitializeComponent();
        _repository = repository;
        _idStudent = idStudent;
        _initialRepresentativeLink = initialRepresentativeLink;
        RepresentativesGrid.ItemsSource = _links;
        _institutionCombo = new SearchableLookupCombo(InstitutionCombo);
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            HideError();
            _allRelationTypes = await _repository.GetRelationTypesAsync();
            _relationTypes = RelationTypeCatalog.ForForm(_allRelationTypes);

            var studentTypes = await _repository.GetStudentTypesAsync();
            StudentTypeCombo.ItemsSource = studentTypes;
            if (studentTypes.Count > 0)
                StudentTypeCombo.SelectedValue = studentTypes[0].Id;

            var institutions = (await _repository.GetInstitutionsAsync()).ToList();
            institutions.Insert(0, new LookupItem { Id = 0, Name = "не указано" });
            _institutionCombo?.SetItems(institutions);
            _institutionCombo?.SelectById(0);

            // По умолчанию: дата прихода = сегодня, дата рождения пустая
            ArrivalDateMaskedBox.Text = DateTime.Today.ToString("dd.MM.yyyy");

            if (_idStudent.HasValue)
                await LoadStudentAsync(_idStudent.Value);
            else if (_initialRepresentativeLink != null)
                AddInitialRepresentativeLink();

            UpdateAgeInfo();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task LoadStudentAsync(int idStudent)
    {
        var details = await _repository.GetStudentDetailsAsync(idStudent);
        HeaderText.Text = $"Редактирование: {details.Student.FullName}";

        LastNameBox.Text = details.Student.LastName;
        FirstNameBox.Text = details.Student.FirstName;
        MiddleNameBox.Text = details.Student.MiddleName ?? string.Empty;
        PhoneMaskedBox.Text = details.Student.Phone ?? string.Empty;
        EmailBox.Text = details.Student.Email ?? string.Empty;
        ResidenceAddressBox.Text = details.Student.ResidenceAddress ?? string.Empty;
        BirthCertificateBox.Text = details.Student.BirthCertificate ?? string.Empty;
        BirthDateMaskedBox.Text = details.Student.BirthDate.ToString("dd.MM.yyyy");
        ArrivalDateMaskedBox.Text = details.Student.ArrivalDate.ToString("dd.MM.yyyy");
        StudentTypeCombo.SelectedValue = details.Student.IdStudentType;
        _institutionCombo?.SelectById(details.Student.IdInstitution ?? 0);
        SchoolNumberBox.Text = details.Student.SchoolNumber ?? string.Empty;
        SetComboByText(StatusCombo, details.Student.Status);
        SetComboByText(SchoolShiftCombo, details.Student.SchoolShift?.ToString() ?? "не указано");
        StudentCommentBox.Text = details.Student.Comment ?? string.Empty;

        _links.Clear();
        foreach (var link in details.Representatives)
            _links.Add(link);
    }

    private void AddInitialRepresentativeLink()
    {
        if (_initialRepresentativeLink == null)
            return;

        if (_links.Any(x => x.IdRepresentative == _initialRepresentativeLink.IdRepresentative))
            return;

        if (_initialRepresentativeLink.IsMainPayer)
        {
            foreach (var item in _links)
                item.IsMainPayer = false;
        }

        _links.Add(_initialRepresentativeLink);

        if (_links.Count > 0 && !_links.Any(x => x.IsMainPayer))
            _links[0].IsMainPayer = true;
    }

    // ============ КУРСОР В НАЧАЛО МАСКИ (как в LeadEditDialog) ============
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
        if (sender is MaskedTextBox box)
        {
            if (string.IsNullOrEmpty(box.Text) || box.Text.All(c => !char.IsDigit(c)))
            {
                box.SelectionStart = 0;
                box.SelectionLength = 0;
                e.Handled = true;
            }
        }
    }

    // ============ АВТОКАПИТАЛИЗАЦИЯ ФИО ============
    private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = CapitalizeFirstLetter(tb.Text);
    }

    private string CapitalizeFirstLetter(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        input = input.Trim();
        // Поддержка двойных имён через дефис: "анна-мария" → "Анна-Мария"
        string[] parts = input.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpper(parts[i][0]) +
                           (parts[i].Length > 1 ? parts[i].Substring(1).ToLower() : "");
            }
        }
        return string.Join("-", parts);
    }

    // ============ ОБНОВЛЕНИЕ ВОЗРАСТА ПРИ ВВОДЕ ДАТЫ ============
    private void BirthDate_LostFocus(object sender, RoutedEventArgs e) => UpdateAgeInfo();

    private void UpdateAgeInfo()
    {
        var birthDate = TryParseDate(BirthDateMaskedBox.Text);
        if (birthDate == null)
        {
            AgeBox.Text = string.Empty;
            AgeInfoBorder.Visibility = Visibility.Collapsed;
            return;
        }

        AgeInfoBorder.Visibility = Visibility.Visible;

        var age = StudentRepository.CalculateAge(birthDate.Value);
        AgeBox.Text = age + " " + GetYearsWord(age);

        if (age < 18)
        {
            // Бледно-розовый, как у заявок при предупреждениях
            AgeInfoBorder.Background = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#FEF2F2")!;
            AgeInfoText.Foreground = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#9F1239")!;
            AgeInfoText.Text = "Несовершеннолетний: представитель обязателен. Выберите или создайте его в блоке ниже.";

            RepresentativeInfoBorder.Background = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#FEF2F2")!;
            RepresentativeInfoText.Foreground = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#9F1239")!;
        }
        else
        {
            // Светло-зелёный — как нормальный фон
            AgeInfoBorder.Background = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#F0FDF4")!;
            AgeInfoText.Foreground = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#166534")!;
            AgeInfoText.Text = "Совершеннолетний: представитель не обязателен. Можно добавить контактное лицо или плательщика.";

            RepresentativeInfoBorder.Background = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#EFF6FF")!;
            RepresentativeInfoText.Foreground = (System.Windows.Media.SolidColorBrush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#1E3A8A")!;
        }
    }

    private static string GetYearsWord(int age)
    {
        int n = Math.Abs(age) % 100;
        int n1 = n % 10;
        if (n > 10 && n < 20) return "лет";
        if (n1 > 1 && n1 < 5) return "года";
        if (n1 == 1) return "год";
        return "лет";
    }

    // ============ ПРЕДСТАВИТЕЛИ ============
    private async void PickRepresentative_Click(object sender, RoutedEventArgs e)
    {

        var picker = new RepresentativePickerDialog(_repository, _relationTypes[0].Id) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRepresentative == null)
            return;

        await AddOrEditLinkAsync(
            picker.SelectedRepresentative.IdRepresentative,
            picker.SelectedRepresentative.FullName,
            picker.SelectedRepresentative.Phone);
    }

    private async void CreateRepresentative_Click(object sender, RoutedEventArgs e)
    {
        var edit = new RepresentativeEditModel();
        var dialog = new RepresentativeEditDialog(edit, _relationTypes, _repository) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var fullName = Student.FormatFio(dialog.Result.LastName, dialog.Result.FirstName, dialog.Result.MiddleName);
            await AddOrEditLinkAsync(dialog.Result.IdRepresentative, fullName, dialog.Result.Phone, dialog.LinkResult);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private Task AddOrEditLinkAsync(
        int idRepresentative,
        string fullName,
        string phone,
        StudentRepresentativeEditModel? presetLink = null)
    {
        StudentRepresentativeEditModel linkData;
        if (presetLink != null)
        {
            linkData = new StudentRepresentativeEditModel
            {
                IdStudent = _idStudent ?? 0,
                IdRepresentative = idRepresentative,
                IdRelationType = presetLink.IdRelationType,
                IsMainPayer = presetLink.IsMainPayer,
                IsLegalRepresentative = presetLink.IsLegalRepresentative,
                Note = presetLink.Note
            };
        }
        else
        {
            var existing = _links.FirstOrDefault(x => x.IdRepresentative == idRepresentative);
            var attach = new StudentRepresentativeAttachDialog(
                _idStudent ?? 0,
                idRepresentative,
                fullName,
                _relationTypes,
                existing) { Owner = this };

            if (attach.ShowDialog() != true)
                return Task.CompletedTask;

            linkData = attach.Result;
        }

        var relation = _relationTypes.FirstOrDefault(x => x.Id == linkData.IdRelationType);
        if (relation == null)
        {
            ShowError("Выбранный тип родства больше недоступен. Обновите справочник.");
            return Task.CompletedTask;
        }

        var linked = _links.FirstOrDefault(x => x.IdRepresentative == idRepresentative);

        if (linkData.IsMainPayer)
        {
            foreach (var item in _links)
                item.IsMainPayer = false;
        }

        if (linked != null)
        {
            linked.IdRelationType = linkData.IdRelationType;
            linked.RelationName = relation.Name;
            linked.IsMainPayer = linkData.IsMainPayer;
            linked.IsLegalRepresentative = linkData.IsLegalRepresentative;
            linked.Note = linkData.Note;
        }
        else
        {
            _links.Add(new StudentRepresentativeLink
            {
                IdStudent = _idStudent ?? 0,
                IdRepresentative = idRepresentative,
                RepresentativeFullName = fullName,
                Phone = phone,
                IdRelationType = linkData.IdRelationType,
                RelationName = relation.Name,
                IsMainPayer = linkData.IsMainPayer,
                IsLegalRepresentative = linkData.IsLegalRepresentative,
                Note = linkData.Note
            });
        }

        if (_links.Count > 0 && !_links.Any(x => x.IsMainPayer))
            _links[0].IsMainPayer = true;

        return Task.CompletedTask;
    }

    private async void RepresentativesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RepresentativesGrid.SelectedItem is not StudentRepresentativeLink link)
            return;

        await AddOrEditLinkAsync(link.IdRepresentative, link.RepresentativeFullName, link.Phone);
    }

    private void MakeMainPayer_Click(object sender, RoutedEventArgs e)
    {
        if (RepresentativesGrid.SelectedItem is not StudentRepresentativeLink link)
            return;

        foreach (var item in _links)
            item.IsMainPayer = false;

        link.IsMainPayer = true;
        HideError();
    }

    private void RemoveRepresentative_Click(object sender, RoutedEventArgs e)
    {
        if (RepresentativesGrid.SelectedItem is not StudentRepresentativeLink link)
            return;

        _links.Remove(link);
        if (_links.Count > 0 && !_links.Any(x => x.IsMainPayer))
            _links[0].IsMainPayer = true;
        HideError();
    }

    // ============ СОХРАНЕНИЕ ============
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            LastNameBox.Text = CapitalizeFirstLetter(OptionalFieldDefaults.NameOrDefault(LastNameBox.Text));
            FirstNameBox.Text = CapitalizeFirstLetter(OptionalFieldDefaults.NameOrDefault(FirstNameBox.Text));
            if (!string.IsNullOrWhiteSpace(MiddleNameBox.Text))
                MiddleNameBox.Text = CapitalizeFirstLetter(MiddleNameBox.Text);

            var birthDate = TryParseDate(BirthDateMaskedBox.Text);
            var resolvedBirth = OptionalFieldDefaults.BirthDateOrDefault(birthDate);

            DateTime arrivalDate = DateTime.Today;
            var parsedArrival = TryParseDate(ArrivalDateMaskedBox.Text ?? "");
            if (parsedArrival != null)
                arrivalDate = parsedArrival.Value;

            var idStudentType = StudentTypeCombo.SelectedValue is int st && st > 0
                ? st
                : OptionalFieldDefaults.StudentTypeOrDefault(0);

            var phoneClean = OptionalFieldDefaults.PhoneOrNull(PhoneMaskedBox.Text);

            var emailRaw = EmailBox.Text?.Trim();
            if (!EmailValidator.IsValidOptional(emailRaw))
                emailRaw = null;

            if (_links.Count > 0 && !_links.Any(x => x.IsMainPayer))
                _links[0].IsMainPayer = true;

            var model = new StudentEditModel
            {
                IdStudent = _idStudent ?? 0,
                LastName = LastNameBox.Text.Trim(),
                FirstName = FirstNameBox.Text.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(MiddleNameBox.Text) ? null : MiddleNameBox.Text.Trim(),
                BirthDate = resolvedBirth,
                Phone = phoneClean,
                Email = string.IsNullOrWhiteSpace(emailRaw) ? null : emailRaw,
                ResidenceAddress = string.IsNullOrWhiteSpace(ResidenceAddressBox.Text) ? null : ResidenceAddressBox.Text.Trim(),
                BirthCertificate = string.IsNullOrWhiteSpace(BirthCertificateBox.Text) ? null : BirthCertificateBox.Text.Trim(),
                Status = GetComboText(StatusCombo, "активный"),
                IdInstitution = _institutionCombo?.GetSelectedId(),
                SchoolNumber = string.IsNullOrWhiteSpace(SchoolNumberBox.Text) ? null : SchoolNumberBox.Text.Trim(),
                SchoolShift = GetSchoolShift(),
                IdStudentType = idStudentType,
                ArrivalDate = arrivalDate,
                Comment = string.IsNullOrWhiteSpace(StudentCommentBox.Text) ? null : StudentCommentBox.Text.Trim()
            };

            var linkModels = _links.Select(x => new StudentRepresentativeEditModel
            {
                IdStudent = model.IdStudent,
                IdRepresentative = x.IdRepresentative,
                IdRelationType = x.IdRelationType,
                IsMainPayer = x.IsMainPayer,
                IsLegalRepresentative = x.IsLegalRepresentative,
                Note = x.Note
            }).ToList();

            SavedStudentId = await _repository.SaveStudentWithRepresentativesAsync(model, linkModels);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // ============ HELPERS ============
    private static DateTime? TryParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains('_')) return null;
        if (!DateTime.TryParseExact(text, "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;
        return parsed;
    }

    private string ExtractCleanPhone(string masked)
        => Regex.Replace(masked, @"[^\d+]", "");

    private short? GetSchoolShift()
    {
        var value = GetComboText(SchoolShiftCombo, "не указано");
        return short.TryParse(value, out var shift) ? shift : null;
    }

    private static string GetComboText(ComboBox combo, string fallback)
    {
        return combo.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? fallback
            : combo.Text ?? fallback;
    }

    private static void SetComboByText(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
