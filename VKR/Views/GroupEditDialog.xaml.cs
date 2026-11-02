#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class GroupEditDialog : Window
{
    private readonly GroupsRepository _repository;
    private readonly StudentRepository _studentRepository = AppServices.GetRequiredService<StudentRepository>();
    private readonly int? _idGroup;
    private int? _currentIdDirection;
    private string _currentGroupTitle = string.Empty;

    public GroupEditDialog(GroupsRepository repository, int? idGroup = null)
    {
        InitializeComponent();
        _repository = repository;
        _idGroup = idGroup;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideError();

            DirectionCombo.ItemsSource = await _repository.GetDirectionsAsync();
            TeacherCombo.ItemsSource = await _repository.GetTeachersAsync();

            if (!_idGroup.HasValue)
            {
                MaskedInputParser.SelectFirstIfNone(DirectionCombo);
                MaskedInputParser.SelectFirstIfNone(TeacherCombo);
            }

            if (_idGroup.HasValue)
            {
                DialogTitleText.Text = "Редактирование группы";
                DeleteButton.Visibility = Visibility.Visible;
                MembersPanel.Visibility = Visibility.Visible;

                var model = await _repository.GetGroupEditModelAsync(_idGroup.Value);
                if (model != null)
                {
                    _currentIdDirection = model.IdDirection;
                    DirectionCombo.SelectedValue = model.IdDirection;
                    TeacherCombo.SelectedValue = model.IdTeacher;
                    GroupNumberBox.Text = model.GroupNumber.ToString();
                    IsActiveCheck.IsChecked = model.IsActive;
                    AgeMinBox.Text = model.AgeMin?.ToString() ?? "";
                    AgeMaxBox.Text = model.AgeMax?.ToString() ?? "";
                    AgeBandLabelBox.Text = model.AgeBandLabel ?? "";
                    MaxStudentsBox.Text = model.MaxStudents?.ToString() ?? "";

                    var groups = await _repository.GetGroupsAsync();
                    foreach (var g in groups)
                    {
                        if (g.IdGroup == _idGroup.Value)
                        {
                            DialogTitleText.Text = $"Редактирование: {g.Title}";
                            _currentGroupTitle = g.Title;
                            break;
                        }
                    }
                }

                var members = await _repository.GetGroupMembersAsync(_idGroup.Value);
                MembersGrid.ItemsSource = members;
                NoMembersText.Visibility = members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            MaskedInputParser.TryGetSelectedId(DirectionCombo, out var idDirection);
            MaskedInputParser.TryGetSelectedId(TeacherCombo, out var idTeacher);
            short.TryParse(GroupNumberBox.Text?.Trim(), out var groupNumber);

            short? ageMin = null, ageMax = null, maxStudents = null;
            if (short.TryParse(AgeMinBox.Text?.Trim(), out var parsedMin) && parsedMin >= 0)
                ageMin = parsedMin;
            if (short.TryParse(AgeMaxBox.Text?.Trim(), out var parsedMax) && parsedMax >= 0)
                ageMax = parsedMax;
            if (short.TryParse(MaxStudentsBox.Text?.Trim(), out var parsedCap) && parsedCap > 0)
                maxStudents = parsedCap;

            var model = new StudyGroupEditModel
            {
                IdGroup = _idGroup ?? 0,
                IdDirection = OptionalFieldDefaults.DirectionOrNull(idDirection),
                IdTeacher = idTeacher > 0 ? idTeacher : null,
                GroupNumber = OptionalFieldDefaults.GroupNumberOrDefault(groupNumber),
                IsActive = IsActiveCheck.IsChecked == true,
                AgeMin = ageMin,
                AgeMax = ageMax,
                AgeBandLabel = string.IsNullOrWhiteSpace(AgeBandLabelBox.Text) ? null : AgeBandLabelBox.Text.Trim(),
                MaxStudents = maxStudents
            };

            await _repository.SaveGroupAsync(model);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_idGroup.HasValue) return;

        var result = MessageBox.Show(
            "Удалить группу? Если в ней есть ученики или занятия в расписании — удаление может не пройти.",
            "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteGroupAsync(_idGroup.Value);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void TransferMember_Click(object sender, RoutedEventArgs e)
    {
        if (!_idGroup.HasValue)
            return;

        if (MembersGrid.SelectedItem is not GroupMemberItem member)
        {
            MessageBox.Show("Выберите ученика в списке группы.", "Перевод",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new StudentGroupTransferDialog(
            _studentRepository,
            member.IdStudent,
            member.StudentFullName,
            _idGroup.Value,
            _currentIdDirection,
            _currentGroupTitle)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
            await ReloadMembersAsync();
    }

    private void OpenStudentCard_Click(object sender, RoutedEventArgs e)
    {
        if (MembersGrid.SelectedItem is not GroupMemberItem member)
        {
            MessageBox.Show("Выберите ученика в списке.", "Карточка",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new StudentCardDialog(_studentRepository, member.IdStudent) { Owner = this };
        dialog.ShowDialog();
    }

    private async Task ReloadMembersAsync()
    {
        if (!_idGroup.HasValue)
            return;

        var members = await _repository.GetGroupMembersAsync(_idGroup.Value);
        MembersGrid.ItemsSource = members;
        NoMembersText.Visibility = members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
