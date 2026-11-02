#nullable enable
using System;
using System.Collections.Generic;
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

public partial class RepresentativeEditDialog : Window
{
    private readonly RepresentativeEditModel _model;
    private readonly IReadOnlyList<LookupItem>? _relationTypes;
    private readonly StudentRepository? _saveRepository;

    public RepresentativeEditModel Result { get; private set; }

    public StudentRepresentativeEditModel? LinkResult { get; private set; }

    public RepresentativeEditDialog(
        RepresentativeEditModel? model = null,
        IReadOnlyList<LookupItem>? relationTypes = null,
        StudentRepository? saveRepository = null)
    {
        InitializeComponent();
        _model = model ?? new RepresentativeEditModel();
        _relationTypes = relationTypes;
        _saveRepository = saveRepository;
        Result = _model;
        ConfigureLinkOptions();
        FillForm();
    }

    private void ConfigureLinkOptions()
    {
        if (_relationTypes is not { Count: > 0 })
            return;

        LinkOptionsPanel.Visibility = Visibility.Visible;
        RelationCombo.ItemsSource = _relationTypes;
        if (_model.FallbackRelationTypeId is > 0)
        {
            RelationCombo.SelectedItem = RelationTypeCatalog.Resolve(
                _relationTypes, _model.FallbackRelationTypeId, _relationTypes);
        }
        else
        {
            RelationCombo.SelectedItem = null;
        }
    }

    private void FillForm()
    {
        if (_model.IdRepresentative > 0)
            DialogTitleText.Text = "Редактирование представителя";

        LastNameBox.Text = _model.LastName;
        FirstNameBox.Text = _model.FirstName;
        MiddleNameBox.Text = _model.MiddleName ?? string.Empty;
        PhoneMaskedBox.Text = _model.Phone;
        EmailBox.Text = _model.Email ?? string.Empty;
        ResidenceAddressBox.Text = _model.ResidenceAddress ?? string.Empty;
        AgeMaskedBox.Text = _model.Age?.ToString() ?? string.Empty;
        WorkplaceBox.Text = _model.Workplace ?? string.Empty;
        CommentBox.Text = _model.Comment ?? string.Empty;
    }

    // ============ КУРСОР В НАЧАЛО МАСКИ ============
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

    // ============ АВТОКАПИТАЛИЗАЦИЯ ============
    private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = CapitalizeFirstLetter(tb.Text);
    }

    private string CapitalizeFirstLetter(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        input = input.Trim();
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

    // ============ СОХРАНЕНИЕ ============
    private void Save_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(sender as Button);

    private async Task SaveAsync(Button? saveButton)
    {
        HideError();

        LastNameBox.Text = CapitalizeFirstLetter(OptionalFieldDefaults.NameOrDefault(LastNameBox.Text));
        FirstNameBox.Text = CapitalizeFirstLetter(OptionalFieldDefaults.NameOrDefault(FirstNameBox.Text));
        if (!string.IsNullOrWhiteSpace(MiddleNameBox.Text))
            MiddleNameBox.Text = CapitalizeFirstLetter(MiddleNameBox.Text);

        var phoneClean = OptionalFieldDefaults.PhoneOrNull(PhoneMaskedBox.Text) ?? OptionalFieldDefaults.EmptyPhone;

        var emailRaw = EmailBox.Text?.Trim();
        if (!EmailValidator.IsValidOptional(emailRaw))
            emailRaw = null;

        int? age = null;
        string ageRaw = (AgeMaskedBox.Text ?? "").Replace("_", "").Trim();
        if (int.TryParse(ageRaw, out var parsedAge) && parsedAge >= 0 && parsedAge <= 120)
            age = parsedAge;

        Result = new RepresentativeEditModel
        {
            IdRepresentative = _model.IdRepresentative,
            LastName = LastNameBox.Text.Trim(),
            FirstName = FirstNameBox.Text.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(MiddleNameBox.Text) ? null : MiddleNameBox.Text.Trim(),
            Phone = phoneClean,
            Email = string.IsNullOrWhiteSpace(emailRaw) ? null : emailRaw,
            ResidenceAddress = string.IsNullOrWhiteSpace(ResidenceAddressBox.Text) ? null : ResidenceAddressBox.Text.Trim(),
            Age = age,
            Workplace = string.IsNullOrWhiteSpace(WorkplaceBox.Text) ? null : WorkplaceBox.Text.Trim(),
            Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim(),
            FallbackRelationTypeId = _model.FallbackRelationTypeId
        };

        if (LinkOptionsPanel.Visibility == Visibility.Visible)
        {
            if (RelationCombo.SelectedItem is not LookupItem { Id: > 0 } relation)
            {
                ShowError("Укажите степень родства.");
                return;
            }

            LinkResult = new StudentRepresentativeEditModel
            {
                IdRelationType = relation.Id,
                IsMainPayer = MainPayerCheck.IsChecked == true,
                IsLegalRepresentative = LegalRepresentativeCheck.IsChecked == true
            };
        }

        if (_saveRepository == null)
        {
            DialogResult = true;
            Close();
            return;
        }

        try
        {
            if (saveButton != null)
                saveButton.IsEnabled = false;
            var id = await _saveRepository.SaveRepresentativeAsync(Result);
            Result = new RepresentativeEditModel
            {
                IdRepresentative = id,
                LastName = Result.LastName,
                FirstName = Result.FirstName,
                MiddleName = Result.MiddleName,
                Phone = Result.Phone,
                Email = Result.Email,
                ResidenceAddress = Result.ResidenceAddress,
                Age = Result.Age,
                Workplace = Result.Workplace,
                Comment = Result.Comment,
                FallbackRelationTypeId = Result.FallbackRelationTypeId
            };
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

    private string ExtractCleanPhone(string masked)
        => Regex.Replace(masked, @"[^\d+]", "");

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
