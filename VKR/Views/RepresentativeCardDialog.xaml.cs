#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class RepresentativeCardDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly int _idRepresentative;

    public RepresentativeCardDialog(StudentRepository repository, int idRepresentative)
    {
        InitializeComponent();
        _repository = repository;
        _idRepresentative = idRepresentative;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            ErrorText.Text = string.Empty;
            ErrorBorder.Visibility = Visibility.Collapsed;
            var details = await _repository.GetRepresentativeDetailsAsync(_idRepresentative);
            var representative = details.Representative;

            TitleText.Text = representative.FullName;
            SubtitleText.Text = details.Children.Count == 0
                ? "Пока не связан ни с одним обучающимся"
                : $"Связан с обучающимися: {details.Children.Count}";
            FioText.Text = representative.FullName;
            PhoneText.Text = representative.Phone;
            EmailText.Text = string.IsNullOrWhiteSpace(representative.Email) ? "—" : representative.Email;
            AgeText.Text = representative.Age?.ToString() ?? "—";
            WorkplaceText.Text = string.IsNullOrWhiteSpace(representative.Workplace) ? "—" : representative.Workplace;
            ResidenceAddressText.Text = string.IsNullOrWhiteSpace(representative.ResidenceAddress)
                ? "—"
                : representative.ResidenceAddress;
            CommentText.Text = string.IsNullOrWhiteSpace(representative.Comment) ? "—" : representative.Comment;
            CreatedText.Text = representative.CreatedAt.ToString("dd.MM.yyyy");
            ChildrenGrid.ItemsSource = details.Children;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }

    private void ChildrenGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChildrenGrid.SelectedItem is not RepresentativeChildItem child)
            return;

        var dialog = new StudentCardDialog(_repository, child.IdStudent) { Owner = this };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
