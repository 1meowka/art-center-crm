#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class StudentPickerDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly ObservableCollection<StudentListItem> _students = new();
    private readonly StudentRepresentativeLink? _initialRepresentativeLink;

    public StudentListItem? SelectedStudent { get; private set; }
    public int SavedStudentId { get; private set; }

    public StudentPickerDialog(
        StudentRepository repository,
        StudentRepresentativeLink? initialRepresentativeLink = null)
    {
        InitializeComponent();
        _repository = repository;
        _initialRepresentativeLink = initialRepresentativeLink;
        StudentsGrid.ItemsSource = _students;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            _students.Clear();
            var rows = await _repository.GetStudentsAsync(SearchBox.Text);
            foreach (var row in rows)
                _students.Add(row);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SearchBox.Text.Length == 0 || SearchBox.Text.Length >= 2)
            await ReloadAsync();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new StudentEditDialog(_repository, initialRepresentativeLink: _initialRepresentativeLink)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        SavedStudentId = dialog.SavedStudentId;

        try
        {
            await ReloadAsync();
            foreach (var item in _students)
            {
                if (item.IdStudent == dialog.SavedStudentId)
                {
                    StudentsGrid.SelectedItem = item;
                    StudentsGrid.ScrollIntoView(item);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e) => SelectCurrent();

    private void StudentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SelectCurrent();

    private void SelectCurrent()
    {
        if (StudentsGrid.SelectedItem is not StudentListItem item)
            return;

        SelectedStudent = item;
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
