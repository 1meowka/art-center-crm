#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class StudentRepresentativeAttachDialog : Window
{
    private readonly int _idStudent;
    private readonly int _idRepresentative;

    public StudentRepresentativeEditModel Result { get; private set; }

    public StudentRepresentativeAttachDialog(
        int idStudent,
        int idRepresentative,
        string representativeFullName,
        IReadOnlyList<LookupItem> relationTypes,
        StudentRepresentativeLink? existing = null)
    {
        InitializeComponent();
        _idStudent = idStudent;
        _idRepresentative = idRepresentative;
        TitleText.Text = $"Связь: {representativeFullName}";
        RelationCombo.ItemsSource = relationTypes;
        RelationCombo.SelectedItem = null;

        if (existing != null)
        {
            RelationCombo.SelectedItem = RelationTypeCatalog.Resolve(relationTypes, existing.IdRelationType, relationTypes);
            MainPayerCheck.IsChecked = existing.IsMainPayer;
            LegalRepresentativeCheck.IsChecked = existing.IsLegalRepresentative;
            NoteBox.Text = existing.Note ?? string.Empty;
        }
        else
        {
            LegalRepresentativeCheck.IsChecked = true;
        }

        Result = new StudentRepresentativeEditModel
        {
            IdStudent = idStudent,
            IdRepresentative = idRepresentative,
            IdRelationType = 0,
            IsLegalRepresentative = true
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        if (RelationCombo.SelectedItem is not LookupItem { Id: > 0 } relation)
        {
            ShowError("Укажите степень родства.");
            return;
        }

        Result = new StudentRepresentativeEditModel
        {
            IdStudent = _idStudent,
            IdRepresentative = _idRepresentative,
            IdRelationType = relation.Id,
            IsMainPayer = MainPayerCheck.IsChecked == true,
            IsLegalRepresentative = LegalRepresentativeCheck.IsChecked == true,
            Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim()
        };

        DialogResult = true;
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
