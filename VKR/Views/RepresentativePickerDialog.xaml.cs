#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class RepresentativePickerDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly ObservableCollection<RepresentativeListItem> _representatives = new();
    private readonly int? _fallbackRelationTypeId;

    public RepresentativeListItem? SelectedRepresentative { get; private set; }

    public RepresentativePickerDialog(StudentRepository repository, int? fallbackRelationTypeId = null)
    {
        InitializeComponent();
        _repository = repository;
        _fallbackRelationTypeId = fallbackRelationTypeId;
        RepresentativesGrid.ItemsSource = _representatives;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            HideError();
            _representatives.Clear();
            var rows = await _repository.GetRepresentativesAsync(SearchBox.Text);
            foreach (var row in rows)
                _representatives.Add(row);
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
        var edit = new RepresentativeEditModel { FallbackRelationTypeId = _fallbackRelationTypeId };
        var dialog = new RepresentativeEditDialog(edit, saveRepository: _repository) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        await ReloadAsync();
        foreach (var item in _representatives)
        {
            if (item.IdRepresentative == dialog.Result.IdRepresentative)
            {
                RepresentativesGrid.SelectedItem = item;
                RepresentativesGrid.ScrollIntoView(item);
                break;
            }
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e) => SelectCurrent();

    private void RepresentativesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SelectCurrent();

    private void SelectCurrent()
    {
        if (RepresentativesGrid.SelectedItem is not RepresentativeListItem item)
            return;

        SelectedRepresentative = item;
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
