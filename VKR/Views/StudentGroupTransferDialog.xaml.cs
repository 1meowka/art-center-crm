#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class StudentGroupTransferDialog : Window
{
    private readonly StudentRepository _repository;
    private readonly GroupsRepository _groupsRepository = new();
    private readonly int _idStudent;
    private readonly string _studentFullName;
    private readonly int? _idCurrentGroup;
    private readonly int? _idCurrentDirection;
    private readonly string _currentGroupDisplay;
    private readonly bool _isEnrollment;

    public StudentGroupTransferDialog(
        StudentRepository repository,
        int idStudent,
        string studentFullName,
        int? idCurrentGroup,
        int? idCurrentDirection,
        string currentGroupDisplay)
    {
        InitializeComponent();
        _repository = repository;
        _idStudent = idStudent;
        _studentFullName = studentFullName;
        _idCurrentGroup = idCurrentGroup;
        _idCurrentDirection = idCurrentDirection;
        _currentGroupDisplay = currentGroupDisplay;
        _isEnrollment = !idCurrentGroup.HasValue;

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        StudentNameText.Text = _studentFullName;
        CurrentGroupText.Text = _isEnrollment
            ? "не зачислен"
            : (string.IsNullOrWhiteSpace(_currentGroupDisplay) ? "—" : _currentGroupDisplay);
        TransferDatePicker.SelectedDate = DateTime.Today;

        Title = _isEnrollment ? "Зачисление в группу" : "Перевод на другое направление";
        HeaderTitleText.Text = Title;
        HeaderHintText.Text = _isEnrollment
            ? "Выберите направление и группу с подходящим возрастом"
            : "Текущая запись закроется датой перевода, новая откроется с этой же даты";
        DateLabelText.Text = _isEnrollment ? "ДАТА ЗАЧИСЛЕНИЯ" : "ДАТА ПЕРЕВОДА";
        SaveButton.Content = _isEnrollment ? "Зачислить" : "Перевести";

        await LoadDirectionsAsync();
        await ReloadCandidatesAsync();
    }

    private async Task LoadDirectionsAsync()
    {
        var directions = await _groupsRepository.GetDirectionsAsync();
        DirectionCombo.Items.Clear();
        DirectionCombo.Items.Add(new LookupItem { Id = 0, Name = "— все направления —" });
        foreach (var direction in directions.OrderBy(x => x.Name))
            DirectionCombo.Items.Add(direction);

        if (_idCurrentDirection.HasValue && _idCurrentDirection.Value > 0)
            DirectionCombo.SelectedValue = _idCurrentDirection.Value;
        else
            DirectionCombo.SelectedIndex = 0;
    }

    private async Task ReloadCandidatesAsync()
    {
        try
        {
            ClearError();
            int? directionId = null;
            if (DirectionCombo.SelectedItem is LookupItem item && item.Id > 0)
                directionId = item.Id;

            var filterByAge = FilterByAgeCheck.IsChecked == true;
            var candidates = await _repository.GetGroupTransferCandidatesAsync(
                _idStudent,
                directionId,
                filterByAge);

            TargetGroupCombo.ItemsSource = candidates;
            if (candidates.Count > 0)
                TargetGroupCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить список групп: " + ex.Message);
        }
    }

    private async void DirectionFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await ReloadCandidatesAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ClearError();

        var option = TargetGroupCombo.SelectedItem as GroupTransferOption
            ?? (TargetGroupCombo.ItemsSource as System.Collections.Generic.IEnumerable<GroupTransferOption>)?.FirstOrDefault();
        if (option == null)
            return;

        var actionDate = (TransferDatePicker.SelectedDate ?? DateTime.Today).Date;

        try
        {
            if (_isEnrollment)
            {
                await _repository.EnrollStudentInGroupAsync(
                    _idStudent,
                    option.IdGroup,
                    actionDate);
            }
            else
            {
                await _repository.TransferStudentToGroupAsync(
                    _idStudent,
                    _idCurrentGroup!.Value,
                    option.IdGroup,
                    actionDate);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError((_isEnrollment ? "Не удалось зачислить: " : "Не удалось перевести: ") + ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
