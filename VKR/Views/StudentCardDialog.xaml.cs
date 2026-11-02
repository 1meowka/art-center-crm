#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

using VKR.Utilities;

namespace VKR.Views;

public partial class StudentCardDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly int _idStudent;

    public StudentCardDialog(StudentRepository repository, int idStudent)
    {
        InitializeComponent();
        _repository = repository;
        _idStudent = idStudent;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            ErrorText.Text = string.Empty;
            ErrorBorder.Visibility = Visibility.Collapsed;
            var details = await _repository.GetStudentDetailsAsync(_idStudent);
            var student = details.Student;

            TitleText.Text = student.FullName;
            SubtitleText.Text = details.IsMinor
                ? "Несовершеннолетний обучающийся, представитель обязателен"
                : "Совершеннолетний обучающийся, представитель опционален";

            FioText.Text = student.FullName;
            BirthText.Text = student.BirthDate.ToString("dd.MM.yyyy");
            BirthCertificateText.Text = string.IsNullOrWhiteSpace(student.BirthCertificate) ? "—" : student.BirthCertificate;
            AgeText.Text = details.Age.ToString();
            MinorText.Text = details.IsMinor ? "несовершеннолетний" : "18+";
            PhoneText.Text = string.IsNullOrWhiteSpace(student.Phone) ? "—" : student.Phone;
            EmailText.Text = string.IsNullOrWhiteSpace(student.Email) ? "—" : student.Email;
            ResidenceAddressText.Text = string.IsNullOrWhiteSpace(student.ResidenceAddress) ? "—" : student.ResidenceAddress;
            StatusText.Text = student.Status;
            TypeText.Text = details.StudentTypeName;
            ArrivalText.Text = student.ArrivalDate.ToString("dd.MM.yyyy");
            InstitutionText.Text = details.InstitutionName;
            SchoolText.Text = string.IsNullOrWhiteSpace(student.SchoolNumber)
                ? "—"
                : $"№{student.SchoolNumber}, смена: {(student.SchoolShift?.ToString() ?? "—")}";
            CommentText.Text = string.IsNullOrWhiteSpace(student.Comment) ? "—" : student.Comment;
            CreatedText.Text = student.CreatedAt.ToString("dd.MM.yyyy");

            RepresentativesGrid.ItemsSource = details.Representatives;
            ContractsGrid.ItemsSource = details.Contracts;
            ContactsGrid.ItemsSource = details.Contacts;
            GroupsGrid.ItemsSource = details.Groups;
        }
        catch (Exception ex)
        {
            ErrorText.Text = UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed);
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }

    private void RepresentativesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RepresentativesGrid.SelectedItem is not StudentRepresentativeLink link)
            return;

        var dialog = new RepresentativeCardDialog(_repository, link.IdRepresentative) { Owner = this };
        dialog.ShowDialog();
    }

    private async void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var details = await _repository.GetStudentDetailsAsync(_idStudent);
        ClientNotesWindow.OpenForStudent(this, _idStudent, details.Student.FullName);
        await LoadAsync();
    }

    // Открытие диалога перевода в другую группу.
    // Берём выбранную строку (или активную, если ничего не выбрано) и передаём её
    // в диалог. После успешного перевода — перечитываем карточку.
    private async void TransferToGroup_Click(object sender, RoutedEventArgs e)
    {
        // Пытаемся взять выбранную строку, иначе — активную membership
        var selected = GroupsGrid.SelectedItem as StudentGroupMembershipItem;
        if (selected == null)
        {
            foreach (var obj in GroupsGrid.ItemsSource ?? Array.Empty<object>())
            {
                if (obj is StudentGroupMembershipItem g && g.IsActive)
                {
                    selected = g;
                    break;
                }
            }
        }

        if (selected is { IsActive: false })
        {
            MessageBox.Show(
                "Эта запись о группе уже закрыта (отчисление произошло раньше). " +
                "Выберите активную группу для перевода.",
                "Перевод невозможен",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new StudentGroupTransferDialog(
            _repository,
            _idStudent,
            TitleText.Text,
            selected?.IdGroup,
            selected?.IdDirection,
            selected?.GroupDisplay ?? string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
