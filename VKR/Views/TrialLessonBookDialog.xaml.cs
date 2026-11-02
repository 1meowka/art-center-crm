#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TrialLessonBookDialog : Window
{
    private readonly LeadRepository _leadRepository;
    private readonly TrialLessonRepository _trialRepository;
    private readonly StudentRepository _studentRepository;
    private readonly GroupsRepository _groupsRepository = new();

    private readonly int? _preselectedDirectionId;
    private List<Lead> _allLeads = new();
    private List<StudyGroupListItem> _groups = new();

    public int SavedTrialId { get; private set; }

    public TrialLessonBookDialog(
        LeadRepository leadRepository,
        TrialLessonRepository trialRepository,
        int? preselectedDirectionId = null)
    {
        InitializeComponent();
        _leadRepository = leadRepository;
        _trialRepository = trialRepository;
        _studentRepository = AppServices.GetRequiredService<StudentRepository>();
        _preselectedDirectionId = preselectedDirectionId;

        ScheduledDatePicker.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            HideError();
            var directions = await _groupsRepository.GetDirectionsAsync();
            DirectionCombo.ItemsSource = directions;
            DirectionCombo.SelectedIndex = -1;
            if (_preselectedDirectionId is > 0)
                DirectionCombo.SelectedValue = _preselectedDirectionId.Value;

            await LoadLeadsAsync();
            await LoadGroupsAndTeachersAsync();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async Task LoadLeadsAsync()
    {
        _allLeads = await _leadRepository.GetAllAsync();
        ApplyLeadFilter();
    }

    private void ApplyLeadFilter()
    {
        var query = (LeadSearchBox?.Text ?? string.Empty).Trim();
        IEnumerable<Lead> filtered = _allLeads;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = _allLeads.Where(l =>
                (l.ChildFullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.ContactPhone?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (l.RepresentativeDisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = filtered.ToList();
        LeadCombo.ItemsSource = list;
        if (list.Count > 0 && LeadCombo.SelectedItem == null)
            LeadCombo.SelectedIndex = 0;
    }

    private async Task LoadGroupsAndTeachersAsync()
    {
        var directionId = DirectionCombo.SelectedValue is int id && id > 0 ? id : (int?)null;
        var allGroups = await _groupsRepository.GetGroupsAsync(onlyActive: true);
        _groups = directionId.HasValue
            ? allGroups.Where(g => g.IdDirection == directionId.Value).ToList()
            : allGroups.ToList();

        GroupCombo.ItemsSource = _groups;
        if (_groups.Count > 0)
            GroupCombo.SelectedIndex = 0;
        else
            GroupCombo.SelectedItem = null;

        var teachers = await _groupsRepository.GetTeachersAsync();
        TeacherCombo.ItemsSource = teachers;
    }

    private async Task LoadLeadHistoryAsync()
    {
        if (LeadCombo.SelectedItem is not Lead lead || lead.IdLead <= 0)
        {
            HistoryGrid.ItemsSource = null;
            TrialStatsText.Text = "Выберите карточку гостя";
            return;
        }

        var history = await _trialRepository.GetLeadTrialHistoryAsync(lead.IdLead);
        var stats = await _trialRepository.GetLeadTrialStatsAsync(lead.IdLead);
        HistoryGrid.ItemsSource = history;
        TrialStatsText.Text = stats.SummaryText;
    }

    private async void LeadSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyLeadFilter();

    private async void LeadCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadLeadHistoryAsync();
    }

    private async void DirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadGroupsAndTeachersAsync();
    }

    private void GroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupCombo.SelectedItem is StudyGroupListItem group)
            TeacherCombo.SelectedValue = group.IdTeacher;
    }

    private async void CreateLead_Click(object sender, RoutedEventArgs e)
    {
        var directionId = DirectionCombo.SelectedValue is int id && id > 0 ? id : _preselectedDirectionId;
        var dialog = new LeadEditDialog(null, directionId) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        await LoadLeadsAsync();
        var created = _allLeads.FirstOrDefault(x => x.IdLead == dialog.Result.IdLead);
        if (created != null)
            LeadCombo.SelectedItem = created;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var directionId = DirectionCombo.SelectedValue is int dir && dir > 0 ? dir : 0;
            var lead = LeadCombo.SelectedItem as Lead;
            var group = GroupCombo.SelectedItem as StudyGroupListItem;
            var teacherId = TeacherCombo.SelectedValue is int tid && tid > 0 ? tid : 0;

            var date = ScheduledDatePicker.SelectedDate?.Date ?? DateTime.Today;
            var startTime = TimeSpan.FromHours(10);
            var startRaw = StartTimeMaskedBox.Text ?? "";
            if (!startRaw.Contains("_") && TryParseTime(startRaw, out var parsedStart))
                startTime = parsedStart;

            TimeSpan? endTime = null;
            var endRaw = EndTimeMaskedBox.Text ?? "";
            if (!string.IsNullOrWhiteSpace(endRaw) && !endRaw.Contains("_") && TryParseTime(endRaw, out var parsedEnd))
                endTime = parsedEnd;

            var scheduledAt = date.Add(startTime);
            var directionName = (DirectionCombo.SelectedItem as LookupItem)?.Name ?? "";
            var teacherName = (TeacherCombo.SelectedItem as LookupItem)?.Name ?? "";
            var groupDisplay = group?.ComboDisplay ?? "—";
            var summary = endTime.HasValue
                ? $"{scheduledAt:dd.MM.yyyy}, {startTime:hh\\:mm} – {endTime.Value:hh\\:mm}, {directionName}, {groupDisplay}, {teacherName}"
                : $"{scheduledAt:dd.MM.yyyy}, {startTime:hh\\:mm}, {directionName}, {groupDisplay}, {teacherName}";

            var booking = new TrialLessonBookModel
            {
                IdLead = lead?.IdLead ?? 0,
                ScheduledAt = scheduledAt,
                IdDirection = directionId,
                IdGroup = group?.IdGroup ?? 0,
                IdTeacher = teacherId
            };

            var paymentDialog = new TrialLessonPaymentDialog(booking, summary, endTime, lead ?? new Lead(), _trialRepository)
            {
                Owner = this
            };

            if (paymentDialog.ShowDialog() != true)
                return;

            SavedTrialId = paymentDialog.SavedTrialId;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private static bool TryParseTime(string raw, out TimeSpan time) =>
        TimeSpan.TryParseExact(raw.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time)
        || TimeSpan.TryParseExact(raw.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out time);

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
