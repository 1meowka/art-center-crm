#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;
using VKR.Utilities;

namespace VKR.Views;

public partial class CalendarEventEditDialog : Window
{
    private readonly CalendarEventRepository _repository;
    private readonly int? _idEvent;

    public CalendarEventEditDialog(CalendarEventRepository repository, int? idEvent = null)
    {
        InitializeComponent();
        _repository = repository;
        _idEvent = idEvent;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideError();

            EventTypeCombo.ItemsSource = CalendarEventTypeInfo.Options;
            EventTypeCombo.SelectedIndex = 0;

            DirectionCombo.ItemsSource = await _repository.GetDirectionsAsync();
            TeacherCombo.ItemsSource = await _repository.GetTeachersAsync();
            ClassroomCombo.ItemsSource = await _repository.GetClassroomsAsync();

            EventDatePicker.SelectedDate = DateTime.Today;
            TimeStartBox.Text = "15:00";
            TimeEndBox.Text = "16:30";

            if (!_idEvent.HasValue)
            {
                MaskedInputParser.SelectFirstIfNone(DirectionCombo);
                MaskedInputParser.SelectFirstIfNone(TeacherCombo);
                MaskedInputParser.SelectFirstIfNone(ClassroomCombo);
                return;
            }

            DialogTitleText.Text = "Редактирование события";
            DeleteButton.Visibility = Visibility.Visible;

            var model = await _repository.GetEditModelAsync(_idEvent.Value);

            TitleBox.Text = model.Title;
            SelectEventType(model.EventType);
            if (model.IdDirection is > 0)
                DirectionCombo.SelectedValue = model.IdDirection;
            if (model.IdTeacher is > 0)
                TeacherCombo.SelectedValue = model.IdTeacher;
            if (model.IdClassroom is > 0)
                ClassroomCombo.SelectedValue = model.IdClassroom;
            EventDatePicker.SelectedDate = model.ScheduledAt.Date;
            TimeStartBox.Text = model.ScheduledAt.ToString("HH:mm");
            TimeEndBox.Text = model.TimeEnd?.ToString(@"hh\:mm") ?? "16:30";
            MaxParticipantsBox.Text = model.MaxParticipants?.ToString() ?? "";
            DescriptionBox.Text = model.Description ?? "";
            IsActiveCheck.IsChecked = model.IsActive;
            DialogTitleText.Text = $"Редактирование: {model.Title}";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SelectEventType(string code)
    {
        foreach (var item in EventTypeCombo.Items)
        {
            if (item is CodeOption opt && string.Equals(opt.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                EventTypeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var title = string.IsNullOrWhiteSpace(TitleBox.Text)
                ? OptionalFieldDefaults.DictionaryNameOrDefault(null)
                : TitleBox.Text.Trim();
            var eventDate = EventDatePicker.SelectedDate ?? DateTime.Today;
            if (!MaskedInputParser.TryParseTime(TimeStartBox.Text, out var timeStart))
                timeStart = new TimeSpan(10, 0, 0);
            if (!MaskedInputParser.TryParseTime(TimeEndBox.Text, out var timeEnd))
                timeEnd = timeStart.Add(TimeSpan.FromHours(1));
            if (timeStart >= timeEnd)
                timeEnd = timeStart.Add(TimeSpan.FromHours(1));

            short? maxParticipants = null;
            if (!string.IsNullOrWhiteSpace(MaxParticipantsBox.Text)
                && short.TryParse(MaxParticipantsBox.Text.Trim(), out var parsed) && parsed > 0)
                maxParticipants = parsed;

            MaskedInputParser.TryGetSelectedId(DirectionCombo, out var idDirection);
            MaskedInputParser.TryGetSelectedId(TeacherCombo, out var idTeacher);
            MaskedInputParser.TryGetSelectedId(ClassroomCombo, out var idClassroom);

            var eventType = EventTypeCombo.SelectedItem is CodeOption opt ? opt.Code : "master_class";

            var model = new CalendarEventEditModel
            {
                IdEvent = _idEvent ?? 0,
                EventType = eventType,
                Title = title,
                IdDirection = idDirection > 0 ? idDirection : null,
                IdTeacher = idTeacher > 0 ? idTeacher : null,
                IdClassroom = idClassroom > 0 ? idClassroom : null,
                ScheduledAt = eventDate.Date.Add(timeStart),
                TimeEnd = timeEnd,
                MaxParticipants = maxParticipants,
                Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                IsActive = IsActiveCheck.IsChecked == true
            };

            await _repository.SaveAsync(model);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_idEvent.HasValue) return;

        var result = MessageBox.Show(
            "Удалить событие? Записи на пробное, привязанные к нему, останутся без ссылки на событие.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _repository.DeleteAsync(_idEvent.Value);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
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
