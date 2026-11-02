#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TrialLessonsWindow : Window
{
    private readonly TrialLessonRepository _trialRepository = AppServices.GetRequiredService<TrialLessonRepository>();
    private readonly LeadRepository _leadRepository = AppServices.GetRequiredService<LeadRepository>();
    private readonly TrialFollowupRepository _followupRepository = new();
    private readonly GroupsRepository _groupsRepository = new();

    private readonly ObservableCollection<TrialLessonSlotItem> _slots = new();
    private readonly ObservableCollection<TrialLessonListItem> _booked = new();
    private readonly ObservableCollection<TrialJournalDayColumn> _journalDays = new();
    private readonly ObservableCollection<TrialJournalSlotCard> _journalList = new();
    private readonly ObservableCollection<TrialDirectionOption> _directions = new();
    private readonly ObservableCollection<TrialFollowupDueItem> _followupTasks = new();
    private readonly ObservableCollection<TrialAvailableDateItem> _availableDates = new();
    private readonly ObservableCollection<TrialContractCandidateItem> _contractCandidates = new();

    private readonly SearchDebouncer _directionFilterDebouncer = new(TimeSpan.FromMilliseconds(300));

    private readonly List<TrialLessonListItem> _journalWeekBookings = new();
    private readonly List<TrialJournalSlotCard> _journalSlotCards = new();

    private bool _uiReady;
    private bool _journalCardsView = true;
    private bool _syncingJournalDatePicker;
    private DateTime? _directionModeDate;
    private DateTime _journalWeekStart = GetWeekStartMonday(DateTime.Today);
    private TrialJournalSlotCard? _selectedJournalSlot;

    public TrialLessonsWindow()
    {
        InitializeComponent();
        SlotsGrid.ItemsSource = _slots;
        BookedGrid.ItemsSource = _booked;
        JournalCardsControl.ItemsSource = _journalDays;
        JournalListGrid.ItemsSource = _journalList;
        DirectionsList.ItemsSource = _directions;
        AvailableDatesList.ItemsSource = _availableDates;
        FollowupTasksGrid.ItemsSource = _followupTasks;
        ContractCandidatesGrid.ItemsSource = _contractCandidates;

        InitializeJournalDays();

        Loaded += TrialLessonsWindow_Loaded;
        Unloaded += (_, _) => _directionFilterDebouncer.Dispose();
        _directionFilterDebouncer.Debounced += _ => SafeRunAsync(ReloadSlotsByDirectionAsync);
    }

    private async void TrialLessonsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady)
            return;

        _uiReady = true;
        SelectedDatePicker.SelectedDate = DateTime.Today;
        UpdateJournalWeekHeader();
        await SafeRunAsync(InitializeAsync);
    }

    private void InitializeJournalDays()
    {
        _journalDays.Clear();
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 1, DayName = "Понедельник", ShortDayName = "ПН" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 2, DayName = "Вторник", ShortDayName = "ВТ" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 3, DayName = "Среда", ShortDayName = "СР" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 4, DayName = "Четверг", ShortDayName = "ЧТ" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 5, DayName = "Пятница", ShortDayName = "ПТ" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 6, DayName = "Суббота", ShortDayName = "СБ" });
        _journalDays.Add(new TrialJournalDayColumn { DayOfWeek = 7, DayName = "Воскресенье", ShortDayName = "ВС" });
        UpdateJournalWeekDates();
    }

    private static DateTime GetWeekStartMonday(DateTime date)
    {
        var day = date.Date;
        var diff = (7 + (day.DayOfWeek - DayOfWeek.Monday)) % 7;
        return day.AddDays(-diff);
    }

    private static short GetIsoDayOfWeek(DateTime date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)date.DayOfWeek;

    private void UpdateJournalWeekDates()
    {
        for (var i = 0; i < _journalDays.Count; i++)
            _journalDays[i].Date = _journalWeekStart.AddDays(i);
    }

    private void UpdateJournalWeekHeader()
    {
        var weekEnd = _journalWeekStart.AddDays(6);
        JournalWeekRangeText.Text = $"{_journalWeekStart:dd.MM.yyyy} — {weekEnd:dd.MM.yyyy}";
        JournalHintText.Text = $"Пробные на неделе {JournalWeekRangeText.Text} — позвоните и напомните о занятии";
        SyncJournalJumpDatePicker();
    }

    private void SyncJournalJumpDatePicker()
    {
        if (JournalJumpDatePicker == null)
            return;

        var weekEnd = _journalWeekStart.AddDays(6);
        var target = _journalWeekStart <= DateTime.Today && DateTime.Today <= weekEnd
            ? DateTime.Today
            : _journalWeekStart;

        if (JournalJumpDatePicker.SelectedDate?.Date == target.Date)
            return;

        try
        {
            _syncingJournalDatePicker = true;
            JournalJumpDatePicker.SelectedDate = target;
        }
        finally
        {
            _syncingJournalDatePicker = false;
        }
    }

    private async Task InitializeAsync()
    {
        _directions.Clear();
        var directions = await _groupsRepository.GetDirectionsAsync();
        foreach (var direction in directions.OrderBy(x => x.Name))
        {
            _directions.Add(new TrialDirectionOption
            {
                IdDirection = direction.Id,
                Name = direction.Name
            });
        }

        await ReloadJournalAsync();
        await ReloadFollowupTasksAsync();
        await ReloadContractCandidatesAsync();
        if (SlotSelectionPanel.Visibility == Visibility.Visible)
            await ReloadSlotsByDateAsync();
    }

    private bool IsByDateMode => ByDateRadio?.IsChecked == true;

    private void SearchMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        var byDate = IsByDateMode;
        ByDatePanel.Visibility = byDate ? Visibility.Visible : Visibility.Collapsed;
        ByDirectionPanel.Visibility = byDate ? Visibility.Collapsed : Visibility.Visible;
        DatesPanel.Visibility = byDate ? Visibility.Collapsed : Visibility.Visible;

        _ = SafeRunAsync(async () =>
        {
            if (byDate)
                await ReloadSlotsByDateAsync();
            else
                await ReloadSlotsByDirectionAsync();
        });
    }

    private async void SelectedDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || !IsByDateMode)
            return;

        await SafeRunAsync(ReloadSlotsByDateAsync);
    }

    private void DirectionFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || IsByDateMode)
            return;

        _directionFilterDebouncer.Schedule();
    }

    private async void AvailableDatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || IsByDateMode)
            return;

        if (AvailableDatesList.SelectedItem is not TrialAvailableDateItem item)
            return;

        _directionModeDate = item.Date;
        await SafeRunAsync(LoadSlotsForDirectionDateAsync);
    }

    private async Task ReloadSlotsByDateAsync()
    {
        ClearError();
        _slots.Clear();
        _directionModeDate = null;

        var date = SelectedDatePicker.SelectedDate?.Date ?? DateTime.Today;
        var rows = await _trialRepository.GetSlotsForDateAsync(date);
        foreach (var row in rows)
            _slots.Add(row);

        await _trialRepository.EnrichSlotsWithOccupancyAsync(_slots);

        SlotsCountText.Text = $"найдено слотов: {_slots.Count} · {date:dd.MM.yyyy, dddd}";
    }

    private async Task ReloadSlotsByDirectionAsync()
    {
        ClearError();
        _slots.Clear();
        _availableDates.Clear();

        var selectedIds = _directions.Where(x => x.IsSelected).Select(x => x.IdDirection).ToList();
        if (selectedIds.Count == 0)
        {
            SlotsCountText.Text = "выберите одно или несколько направлений";
            return;
        }

        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(56);
        var dates = await _trialRepository.GetAvailableDatesAsync(selectedIds, from, to);
        foreach (var date in dates)
            _availableDates.Add(date);

        if (_availableDates.Count == 0)
        {
            SlotsCountText.Text = "для выбранных направлений нет слотов в ближайшие 8 недель";
            return;
        }

        AvailableDatesList.SelectedIndex = 0;
        _directionModeDate = _availableDates[0].Date;
        await LoadSlotsForDirectionDateAsync();
    }

    private async Task LoadSlotsForDirectionDateAsync()
    {
        if (_directionModeDate == null)
            return;

        ClearError();
        _slots.Clear();

        var selectedIds = _directions.Where(x => x.IsSelected).Select(x => x.IdDirection).ToList();
        var rows = await _trialRepository.GetSlotsForDateAsync(_directionModeDate.Value, selectedIds);
        foreach (var row in rows)
            _slots.Add(row);

        await _trialRepository.EnrichSlotsWithOccupancyAsync(_slots);

        SlotsCountText.Text = $"найдено слотов: {_slots.Count} · {_directionModeDate:dd.MM.yyyy, dddd}";
    }

    private async Task ReloadJournalAsync()
    {
        _journalWeekBookings.Clear();
        _journalSlotCards.Clear();
        _journalList.Clear();
        _selectedJournalSlot = null;

        foreach (var day in _journalDays)
            day.Slots.Clear();

        var weekEnd = _journalWeekStart.AddDays(6);
        var rows = await _trialRepository.GetBookedTrialsAsync(_journalWeekStart, weekEnd);
        _journalWeekBookings.AddRange(rows);

        var slotCards = rows
            .GroupBy(x => new
            {
                x.ScheduledAt,
                x.DirectionName,
                x.GroupTitle,
                x.TeacherName,
                x.ClassroomText,
                x.TimeEnd
            })
            .Select(g => new TrialJournalSlotCard
            {
                ScheduledAt = g.Key.ScheduledAt,
                DirectionName = g.Key.DirectionName,
                GroupTitle = g.Key.GroupTitle,
                TeacherName = g.Key.TeacherName,
                ClassroomText = g.Key.ClassroomText,
                TimeEnd = g.Key.TimeEnd,
                BookedCount = g.Count(),
                AttendeesText = string.Join("; ", g.Select(x => $"{x.ChildFullName} ({x.ContactPhone})")),
                Bookings = g.ToList()
            })
            .OrderBy(x => x.ScheduledAt)
            .ThenBy(x => x.DirectionName)
            .ToList();

        _journalSlotCards.AddRange(slotCards);

        foreach (var card in slotCards)
        {
            foreach (var day in _journalDays)
            {
                if (day.DayOfWeek == GetIsoDayOfWeek(card.ScheduledAt) && day.Date.Date == card.ScheduledAt.Date)
                {
                    day.Slots.Add(card);
                    break;
                }
            }
        }

        foreach (var card in slotCards)
            _journalList.Add(card);

        var totalTrials = rows.Count;
        var totalSlots = slotCards.Count;
        JournalCountText.Text = totalTrials == 0
            ? "на этой неделе пробных записей нет"
            : $"пробных записей: {totalTrials} · слотов: {totalSlots}";

        JournalEmptyText.Visibility = totalTrials == 0 ? Visibility.Visible : Visibility.Collapsed;
        JournalCardsControl.Visibility = _journalCardsView && totalTrials > 0 ? Visibility.Visible : Visibility.Collapsed;
        JournalListBorder.Visibility = !_journalCardsView && totalTrials > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateJournalDetailList(null);
    }

    private void UpdateJournalDetailList(TrialJournalSlotCard? selectedSlot)
    {
        if (_selectedJournalSlot != null)
            _selectedJournalSlot.IsSelected = false;

        _selectedJournalSlot = selectedSlot;
        if (_selectedJournalSlot != null)
            _selectedJournalSlot.IsSelected = true;

        _booked.Clear();
        var source = selectedSlot?.Bookings ?? _journalWeekBookings;
        foreach (var row in source.OrderBy(x => x.ScheduledAt).ThenBy(x => x.ChildFullName))
            _booked.Add(row);

        if (selectedSlot == null)
        {
            JournalDetailHint.Text = "Показаны все записи недели. Кликните по слоту выше, чтобы отфильтровать список.";
            JournalDetailTitle.Text = "СПИСОК ДЛЯ ОБЗВОНА";
        }
        else
        {
            JournalDetailHint.Text =
                $"{selectedSlot.TimeRangeText}, {selectedSlot.DirectionName} — {selectedSlot.CountText}";
            JournalDetailTitle.Text = "КТО ЗАПИСАН НА ЭТОТ СЛОТ";
        }
    }

    private void SelectJournalSlot(TrialJournalSlotCard? slot)
    {
        if (_selectedJournalSlot == slot)
        {
            UpdateJournalDetailList(null);
            return;
        }

        UpdateJournalDetailList(slot);
    }

    private async Task GoToJournalWeekAsync(DateTime anyDateInWeek)
    {
        var newWeekStart = GetWeekStartMonday(anyDateInWeek);
        if (newWeekStart == _journalWeekStart)
            return;

        _journalWeekStart = newWeekStart;
        UpdateJournalWeekDates();
        UpdateJournalWeekHeader();
        await ReloadJournalAsync();
    }

    private async Task ShiftJournalWeekAsync(int days)
    {
        _journalWeekStart = _journalWeekStart.AddDays(days);
        UpdateJournalWeekDates();
        UpdateJournalWeekHeader();
        await ReloadJournalAsync();
    }

    private async void JournalPrevWeek_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(() => ShiftJournalWeekAsync(-7));

    private async void JournalNextWeek_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(() => ShiftJournalWeekAsync(7));

    private async void JournalTodayWeek_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(() => GoToJournalWeekAsync(DateTime.Today));

    private async void JournalJumpDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _syncingJournalDatePicker)
            return;

        if (JournalJumpDatePicker.SelectedDate is not DateTime selected)
            return;

        await SafeRunAsync(() => GoToJournalWeekAsync(selected));
    }

    private void JournalViewCards_Click(object sender, RoutedEventArgs e)
    {
        _journalCardsView = true;
        JournalViewCardsButton.Style = (Style)FindResource("TabButtonActiveStyle");
        JournalViewListButton.Style = (Style)FindResource("TabButtonStyle");
        JournalCardsControl.Visibility = _journalSlotCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        JournalListBorder.Visibility = Visibility.Collapsed;
    }

    private void JournalViewList_Click(object sender, RoutedEventArgs e)
    {
        _journalCardsView = false;
        JournalViewListButton.Style = (Style)FindResource("TabButtonActiveStyle");
        JournalViewCardsButton.Style = (Style)FindResource("TabButtonStyle");
        JournalCardsControl.Visibility = Visibility.Collapsed;
        JournalListBorder.Visibility = _journalSlotCards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void JournalSlotCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrialJournalSlotCard slot })
            SelectJournalSlot(slot);
    }

    private void JournalListGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (JournalListGrid.SelectedItem is TrialJournalSlotCard slot)
            SelectJournalSlot(slot);
    }

    private void BookTrial_Click(object sender, RoutedEventArgs e) => BookTrial();

    private void SlotsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => BookCurrentSlot();

    private void BookTrial()
    {
        var dialog = new TrialLessonBookDialog(_leadRepository, _trialRepository) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _ = SafeRunAsync(async () =>
        {
            await ReloadJournalAsync();
            await ReloadFollowupTasksAsync();
        });
    }

    private void BookCurrentSlot()
    {
        if (SlotSelectionPanel.Visibility != Visibility.Visible)
        {
            BookTrial();
            return;
        }

        if (SlotsGrid.SelectedItem is not TrialLessonSlotItem slot)
        {
            SetError("Выберите слот расписания для записи.");
            return;
        }

        if (slot.IsFull)
        {
            SetError($"В группе нет свободных мест на {slot.DateText} {slot.TimeRangeText}. Выберите другую дату или время.");
            return;
        }

        var dialog = new TrialLessonBookDialog(_leadRepository, _trialRepository, slot.IdDirection) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _ = SafeRunAsync(async () =>
        {
            await ReloadJournalAsync();
            await ReloadFollowupTasksAsync();
            if (SlotSelectionPanel.Visibility == Visibility.Visible)
            {
                if (IsByDateMode)
                    await ReloadSlotsByDateAsync();
                else if (_directionModeDate.HasValue)
                    await LoadSlotsForDirectionDateAsync();
            }
        });
    }

    private async Task ReloadFollowupTasksAsync()
    {
        AcademicSchema.Ensure();
        var rows = await _followupRepository.GetDueAsync();
        _followupTasks.Clear();
        foreach (var row in rows)
            _followupTasks.Add(row);

        FollowupTasksEmptyText.Visibility = _followupTasks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RefreshFollowupTasks_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(ReloadFollowupTasksAsync);

    private async void CompleteFollowupTask_Click(object sender, RoutedEventArgs e)
    {
        if (FollowupTasksGrid.SelectedItem is not TrialFollowupDueItem item)
        {
            SetError("Выберите задачу из списка.");
            return;
        }

        var results = new[]
        {
            "понравилось",
            "заключил_договор",
            "перенесено",
            "не_понравилось",
            "не_пришёл"
        };

        var dialog = new TrialFollowupCompleteDialog(item, results) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null || dialog.SelectedTrialResult == null)
            return;

        dialog.Result.IdFollowup = item.IdFollowup;
        dialog.Result.IdCompletedBy = UserSession.CurrentUserId;

        await SafeRunAsync(async () =>
        {
            if (dialog.Result.MarkCompleted || dialog.Result.OnControl)
                await _trialRepository.UpdateTrialResultAsync(item.IdTrial, dialog.SelectedTrialResult);

            await _followupRepository.CompleteAsync(dialog.Result);
            await ReloadJournalAsync();
            await ReloadFollowupTasksAsync();
            await ReloadContractCandidatesAsync();
        });
    }

    private void MarkResult_Click(object sender, RoutedEventArgs e) => MarkSelectedResult();

    private void BookedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => MarkSelectedResult();

    private async void MarkSelectedResult()
    {
        if (BookedGrid.SelectedItem is not TrialLessonListItem item)
        {
            SetError("Выберите записанное пробное занятие.");
            return;
        }

        var results = new[]
        {
            "понравилось",
            "заключил_договор",
            "перенесено",
            "не_понравилось",
            "не_пришёл"
        };

        var dialog = new TrialResultPickerDialog(item, results) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedResult == null)
            return;

        await SafeRunAsync(async () =>
        {
            await _trialRepository.UpdateTrialResultAsync(item.IdTrial, dialog.SelectedResult);
            await ReloadJournalAsync();
            await ReloadContractCandidatesAsync();
        });
    }

    private async Task ReloadContractCandidatesAsync()
    {
        _contractCandidates.Clear();
        var items = await _trialRepository.GetContractCandidatesAsync();
        foreach (var item in items)
            _contractCandidates.Add(item);

        if (ContractCandidatesEmptyText != null)
            ContractCandidatesEmptyText.Visibility = _contractCandidates.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ContractCandidatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void RefreshContractCandidates_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(ReloadContractCandidatesAsync);

    private TrialContractCandidateItem? GetSelectedCandidate()
    {
        if (ContractCandidatesGrid.SelectedItem is TrialContractCandidateItem selected)
            return selected;

        return _contractCandidates.FirstOrDefault();
    }

    private async void EnrollCandidate_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(EnrollSelectedCandidateAsync);

    private async void CreateContractForCandidate_Click(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(CreateContractForSelectedCandidateAsync);

    private async Task EnrollSelectedCandidateAsync()
    {
        var candidate = GetSelectedCandidate();
        if (candidate == null)
        {
            SetError("Выберите кандидата на договор.");
            return;
        }

        var freshLead = await _leadRepository.GetByIdAsync(candidate.IdLead);
        if (freshLead == null)
        {
            SetError("Карточка гостя не найдена.");
            return;
        }

        var dialog = new ConvertLeadDialog(freshLead) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        MessageBox.Show(
            $"Обучающийся «{dialog.Result.LastName} {dialog.Result.FirstName}» зачислен.",
            "Зачисление выполнено",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        await ReloadContractCandidatesAsync();
        await ReloadJournalAsync();
    }

    private async Task CreateContractForSelectedCandidateAsync()
    {
        var candidate = GetSelectedCandidate();
        if (candidate == null)
        {
            SetError("Выберите кандидата на договор.");
            return;
        }

        var freshLead = await _leadRepository.GetByIdAsync(candidate.IdLead);
        if (freshLead == null)
        {
            SetError("Карточка гостя не найдена.");
            return;
        }

        int? studentId = null;

        if (!string.Equals(freshLead.Status, "конвертирована", StringComparison.OrdinalIgnoreCase))
        {
            var convertDialog = new ConvertLeadDialog(freshLead) { Owner = this };
            if (convertDialog.ShowDialog() != true || convertDialog.Result == null)
                return;

            studentId = await _leadRepository.ConvertLeadAsync(
                candidate.IdLead,
                convertDialog.Result,
                freshLead.IdRepresentative,
                freshLead.RepRelationTypeId);
        }
        else
        {
            var students = await AppServices.GetRequiredService<StudentRepository>().GetStudentsAsync(
                freshLead.ChildFirstName + " " + freshLead.ChildLastName);
            studentId = students.FirstOrDefault(x => x.IdStudent > 0)?.IdStudent;
        }

        if (!studentId.HasValue || studentId.Value <= 0)
        {
            SetError("Сначала зачислите обучающегося из карточки гостя.");
            return;
        }

        var contractDialog = new ContractEditDialog(null, studentId, candidate.IdDirection) { Owner = this };
        if (contractDialog.ShowDialog() == true)
        {
            await ReloadContractCandidatesAsync();
            await ReloadJournalAsync();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            ClearError();
            await action();
        }
        catch (Exception ex)
        {
            SetError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void SetError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
