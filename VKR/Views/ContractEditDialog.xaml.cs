#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class ContractEditDialog : Window
{
    private readonly ContractRepository _repo = new();
    private readonly StudentRepository _studentRepo = new();
    private readonly LeadRepository _leadRepo = new();
    private readonly CoursePackageRepository _courseRepo = new();
    private readonly SearchDebouncer _studentSearchDebouncer = new(TimeSpan.FromMilliseconds(250));

    private readonly int? _idContract;
    private readonly int? _prefillStudentId;
    private readonly int? _prefillDirectionId;
    private Contract? _existing;
    private bool _suppressPricingRecalc;
    private string? _userComment;
    private string? _prefilledLeadNote;
    private bool _isPricingReady;
    private bool _suppressStudentSearch;
    private int? _selectedStudentId;
    private bool _selectedStudentIsMinor;
    private int? _preferredPayerId;
    private int _lessonsCount;
    private short _timesPerWeek = 1;
    private CoursePackageLookupItem? _selectedCourse;
    private bool _suppressCourseSelection;

    public ContractEditDialog() : this(null) { }

    public ContractEditDialog(int? idContract) : this(idContract, null, null) { }

    public ContractEditDialog(int? idContract, int? prefillStudentId, int? prefillDirectionId)
    {
        InitializeComponent();
        _isPricingReady = DiscountPercentBox is not null
            && DiscountAmountBox is not null
            && PriceBox is not null
            && TotalBox is not null
            && CommentBox is not null;
        _idContract = idContract;
        _prefillStudentId = prefillStudentId;
        _prefillDirectionId = prefillDirectionId;

        TitleText.Text = _idContract.HasValue ? "Редактирование договора" : "Новый договор";
        _studentSearchDebouncer.Debounced += async _ => await SearchStudentsAsync();

        Loaded += async (_, _) => await LoadAsync();
        Unloaded += (_, _) => _studentSearchDebouncer.Dispose();
    }

    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            _suppressStudentSearch = true;
            try
            {
                var studentTypes = await _repo.GetStudentTypesLookupAsync();
                var legend = await _repo.GetStudentTypeLegendAsync();
                var legendNames = legend.Select(x => x.Name).ToList();
                StudentTypeLegendItems.ItemsSource = legendNames;
                var legendText = legendNames.Count == 0
                    ? string.Empty
                    : string.Join("  ·  ", legendNames);
                StudentTypeLegendText.Text = legendText;
                StudentTypeCombo.ToolTip = string.IsNullOrWhiteSpace(legendText) ? null : legendText;

                int? ensureDirectionId = _prefillDirectionId;
                if (_idContract.HasValue)
                {
                    _existing = await _repo.GetByIdAsync(_idContract.Value);

                    ensureDirectionId = _existing.IdDirection;
                }

                var directions = await _repo.GetDirectionsLookupAsync(ensureDirectionId);
                if (_existing != null
                    && !string.IsNullOrWhiteSpace(_existing.DirectionNameSnapshot)
                    && directions.FirstOrDefault(d => d.Id == _existing.IdDirection) is { } dirItem
                    && dirItem.Name.Contains("удалено", StringComparison.OrdinalIgnoreCase))
                {
                    dirItem.Name = _existing.DirectionNameSnapshot + " (архив)";
                }

                await ApplyCoursesAsync(ensureDirectionId, _existing?.LessonsCount, _existing?.TimesPerWeek);

                BranchCombo.ItemsSource = StudyBranch.All;
                BranchCombo.SelectedValue = _existing?.IdBranch > 0 ? _existing.IdBranch : 1;
                if (BranchCombo.SelectedItem == null && BranchCombo.Items.Count > 0)
                    BranchCombo.SelectedIndex = 0;

                if (_existing != null)
                {
                    LookupComboHelper.SetItems(DirectionCombo, directions, _existing.IdDirection);
                    LookupComboHelper.SetItems(StudentTypeCombo, studentTypes, _existing.IdStudentType);
                    _preferredPayerId = _existing.IdRepresentative;
                    await SelectStudentAsync(_existing.IdStudent);

                    _suppressPricingRecalc = true;
                    NumberBox.Text = _existing.ContractNumber;
                    NumberBox.IsReadOnly = true;
                    ContractDatePicker.SelectedDate = _existing.ContractDate;
                    StartDatePicker.SelectedDate = _existing.StartDate;
                    EndDatePicker.SelectedDate = _existing.EndDate;
                    if (_selectedCourse == null)
                        SetCourseMetrics(_existing.LessonsCount, _existing.TimesPerWeek, null);
                    PriceBox.Text = _existing.PriceNoDiscount.ToString("0.##", CultureInfo.InvariantCulture);
                    DiscountPercentBox.Text = _existing.DiscountPercent?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
                    DiscountAmountBox.Text = _existing.DiscountAmount?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
                    _userComment = ContractPricing.StripDiscountComment(_existing.Comment);
                    CommentBox.Text = _existing.Comment ?? string.Empty;
                    _suppressPricingRecalc = false;

                    foreach (ComboBoxItem cbi in StatusCombo.Items)
                    {
                        if (string.Equals(cbi.Content?.ToString(), _existing.Status, StringComparison.OrdinalIgnoreCase))
                        {
                            StatusCombo.SelectedItem = cbi;
                            break;
                        }
                    }

                    RecalculatePricing(updateComment: false);
                }
                else
                {
                    LookupComboHelper.SetItems(DirectionCombo, directions, _prefillDirectionId);
                    LookupComboHelper.SetItems(StudentTypeCombo, studentTypes);

                    if (_prefillStudentId.HasValue)
                        await SelectStudentAsync(_prefillStudentId.Value);

                    ContractDatePicker.SelectedDate = DateTime.Today;
                    StartDatePicker.SelectedDate = DateTime.Today;
                    EndDatePicker.SelectedDate = ContractDurationHelper.AddDuration(DateTime.Today, 9);
                    StatusCombo.SelectedIndex = 0;
                    NumberBox.Text = "назначается автоматически";
                    NumberBox.IsReadOnly = true;

                    if (_prefillDirectionId.HasValue)
                        await ApplyCoursesAsync(_prefillDirectionId.Value);
                    else
                        await ApplyCoursesAsync(null);

                    RecalculatePricing(updateComment: false);
                }
            }
            finally
            {
                _suppressStudentSearch = false;
            }
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private void StudentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateStudentPlaceholder();
        if (_suppressStudentSearch)
            return;

        _selectedStudentId = null;
        _selectedStudentIsMinor = false;
        _preferredPayerId = PayerCombo.SelectedValue as int?;
        PayerCombo.ItemsSource = null;
        _studentSearchDebouncer.Schedule();
    }

    private async void StudentSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressStudentSearch)
            return;
        await SearchStudentsAsync();
    }

    private async void StudentSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        await Task.Delay(200);
        if (StudentResultsList.IsKeyboardFocusWithin || StudentSearchBox.IsKeyboardFocusWithin)
            return;
        StudentDropDown.IsOpen = false;
    }

    private async void StudentSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StudentDropDown.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (!StudentDropDown.IsOpen)
                await SearchStudentsAsync();
            if (StudentResultsList.Items.Count > 0)
            {
                StudentResultsList.Focus();
                if (StudentResultsList.SelectedIndex < 0)
                    StudentResultsList.SelectedIndex = 0;
            }
            e.Handled = true;
        }
    }

    private async Task SearchStudentsAsync()
    {
        if (_suppressStudentSearch || StudentSearchBox is null)
            return;

        var query = StudentSearchBox.Text?.Trim() ?? string.Empty;
        var items = await _repo.SearchStudentsLookupAsync(query, 40);

        _suppressStudentSearch = true;
        try
        {
            StudentResultsList.ItemsSource = items;
            StudentDropDown.Width = Math.Max(280, StudentSearchBox.ActualWidth);
            StudentDropDown.IsOpen = StudentSearchBox.IsKeyboardFocusWithin && items.Count > 0;
        }
        finally
        {
            _suppressStudentSearch = false;
        }
    }

    private void StudentResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStudentSearch)
            return;
        if (StudentResultsList.SelectedItem is not LookupItem item || item.Id <= 0)
            return;

        _ = SelectStudentAsync(item.Id, item.Name);
    }

    private async Task SelectStudentAsync(int idStudent, string? name = null)
    {
        var info = await _repo.GetStudentInfoAsync(idStudent);
        _selectedStudentId = idStudent;
        _selectedStudentIsMinor = info?.IsMinor ?? true;
        name = string.IsNullOrWhiteSpace(name) ? (info?.FullName ?? $"Обучающийся #{idStudent}") : name;

        _suppressStudentSearch = true;
        try
        {
            StudentSearchBox.Text = name;
            StudentDropDown.IsOpen = false;
            StudentResultsList.SelectedItem = null;
            UpdateStudentPlaceholder();
        }
        finally
        {
            _suppressStudentSearch = false;
        }

        await LoadPayersAsync(idStudent, _preferredPayerId ?? _existing?.IdRepresentative);
        _preferredPayerId = null;
        UpdatePartyHint();
        await TryPrefillContractNoteFromLeadAsync(idStudent);
    }

    private async Task LoadPayersAsync(int idStudent, int? preferredId)
    {
        if (_selectedStudentIsMinor)
        {
            try
            {
                await _leadRepo.EnsureGuestCardParentsOnStudentAsync(idStudent, _studentRepo);
            }
            catch (Exception ex)
            {
                ShowError(UserFacingErrors.GetMessage(ex, "Не удалось подтянуть родителей из карточки гостя."));
            }
        }

        var links = await _studentRepo.GetStudentRepresentativesAsync(idStudent);
        var items = links.Select(link => new LookupItem
        {
            Id = link.IdRepresentative,
            Name = FormatPayerName(link)
        }).ToList();

        if (preferredId is > 0 && items.All(x => x.Id != preferredId.Value))
        {
            try
            {
                var details = await _studentRepo.GetRepresentativeDetailsAsync(preferredId.Value);
                items.Insert(0, new LookupItem
                {
                    Id = preferredId.Value,
                    Name = details.Representative.FullName + " — из карточки"
                });
            }
            catch
            {
                items.Insert(0, new LookupItem
                {
                    Id = preferredId.Value,
                    Name = $"Заказчик #{preferredId.Value}"
                });
            }
        }

        PayerCombo.ItemsSource = items;

        var selectedId = preferredId
            ?? items.Select(x => (int?)x.Id).FirstOrDefault(id =>
                links.Any(l => l.IdRepresentative == id && l.IsMainPayer))
            ?? items.Select(x => (int?)x.Id).FirstOrDefault(id =>
                links.Any(l => l.IdRepresentative == id && l.IsLegalRepresentative))
            ?? items.FirstOrDefault()?.Id;

        PayerCombo.SelectedValue = selectedId;
        if (PayerCombo.SelectedItem == null && items.Count > 0)
            PayerCombo.SelectedIndex = 0;
    }

    private static string FormatPayerName(StudentRepresentativeLink link)
    {
        var roles = new List<string>();
        if (link.IsMainPayer)
            roles.Add("плательщик");
        if (link.IsLegalRepresentative)
            roles.Add("законный представитель");
        if (!string.IsNullOrWhiteSpace(link.RelationName))
            roles.Add(link.RelationName);
        var suffix = roles.Count == 0 ? string.Empty : " — " + string.Join(", ", roles.Distinct());
        return link.RepresentativeFullName + suffix;
    }

    private void UpdatePartyHint()
    {
        if (PartyHintText is null)
            return;

        if (_selectedStudentId is null)
        {
            PartyHintText.Text = "Договор заключается на обучающегося. Для ребёнка выберите родителя из его карточки.";
            return;
        }

        if (_selectedStudentIsMinor)
        {
            var count = PayerCombo.ItemsSource is System.Collections.ICollection payers
                ? payers.Count
                : PayerCombo.Items.Count;
            PartyHintText.Text = count == 0
                ? "В карточке ребёнка нет родителей. Добавьте маму или папу в карточку гостя — они появятся здесь. Договор заключается на ребёнка."
                : "Договор заключается на ребёнка. Выберите, кого из родителей подставить из карточки.";
            return;
        }

        PartyHintText.Text = "Договор заключается на этого обучающегося.";
    }

    private async void PickPayer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStudentId is null or <= 0)
        {
            ShowError("Сначала выберите обучающегося, затем родителя из карточки.");
            return;
        }

        var dialog = new RepresentativePickerDialog(_studentRepo) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedRepresentative is null)
            return;

        var picked = dialog.SelectedRepresentative;
        try
        {
            await _studentRepo.AttachRepresentativeAsync(new StudentRepresentativeEditModel
            {
                IdStudent = _selectedStudentId.Value,
                IdRepresentative = picked.IdRepresentative,
                IsMainPayer = true,
                IsLegalRepresentative = true
            });
            await LoadPayersAsync(_selectedStudentId.Value, picked.IdRepresentative);
            UpdatePartyHint();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось привязать заказчика к обучающемуся."));
        }
    }

    private void UpdateStudentPlaceholder()
    {
        if (StudentSearchPlaceholder is null)
            return;
        StudentSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(StudentSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task TryPrefillContractNoteFromLeadAsync(int idStudent)
    {
        if (_existing != null || CommentBox is null)
            return;

        var currentUser = ContractPricing.StripDiscountComment(CommentBox.Text);
        var userHasOwnText = !string.IsNullOrWhiteSpace(currentUser)
            && !string.Equals(currentUser.Trim(), _prefilledLeadNote, StringComparison.Ordinal);
        if (userHasOwnText)
            return;

        var note = await _leadRepo.GetContractNoteByStudentIdAsync(idStudent);
        if (string.IsNullOrWhiteSpace(note))
        {
            if (string.IsNullOrWhiteSpace(_prefilledLeadNote))
                return;

            _prefilledLeadNote = null;
            _userComment = null;
            RecalculatePricing(updateComment: true);
            return;
        }

        _prefilledLeadNote = note.Trim();
        _userComment = _prefilledLeadNote;
        RecalculatePricing(updateComment: true);
    }

    private async Task ReloadCoursesAsync(int? idDirection)
    {
        var courses = await _courseRepo.GetActiveLookupAsync(idDirection);
        CourseVariantCombo.ItemsSource = courses;
    }

    private async Task ApplyCoursesAsync(int? idDirection, int? matchLessons = null, short? matchTimesPerWeek = null)
    {
        await ReloadCoursesAsync(idDirection);
        var courses = CourseVariantCombo.ItemsSource as IList<CoursePackageLookupItem>
            ?? CourseVariantCombo.Items.OfType<CoursePackageLookupItem>().ToList();

        if (courses.Count == 0)
        {
            _selectedCourse = null;
            CourseVariantPanel.Visibility = Visibility.Collapsed;
            SetCourseMetrics(0, 0, null);
            return;
        }

        CoursePackageLookupItem? selected = null;
        if (matchLessons.HasValue && matchTimesPerWeek.HasValue)
        {
            selected = courses.FirstOrDefault(c =>
                c.LessonsCount == matchLessons && c.TimesPerWeek == matchTimesPerWeek);
        }

        selected ??= _selectedCourse is { } prev && courses.Any(c => c.Id == prev.Id)
            ? courses.First(c => c.Id == prev.Id)
            : courses[0];

        CourseVariantPanel.Visibility = Visibility.Visible;

        _suppressCourseSelection = true;
        try
        {
            CourseVariantCombo.SelectedItem = selected;
            ApplyCourse(selected);
        }
        finally
        {
            _suppressCourseSelection = false;
        }
    }

    private async void DirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        MaskedInputParser.TryGetSelectedId(DirectionCombo, out var dirId);
        await ApplyCoursesAsync(dirId > 0 ? dirId : null);
    }

    private void CourseVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCourseSelection)
            return;

        if (CourseVariantCombo.SelectedItem is CoursePackageLookupItem course)
            ApplyCourse(course);
    }

    private void ApplyCourse(CoursePackageLookupItem course)
    {
        _selectedCourse = course;
        var start = StartDatePicker.SelectedDate?.Date ?? DateTime.Today;
        StartDatePicker.SelectedDate = start;
        EndDatePicker.SelectedDate = ContractDurationHelper.AddDuration(start, course.DurationMonths);
        SetCourseMetrics(course.LessonsCount ?? 0, course.TimesPerWeek, course.DurationMonths);
    }

    private void SetCourseMetrics(int lessons, short timesPerWeek, decimal? durationMonths)
    {
        _lessonsCount = lessons;
        _timesPerWeek = timesPerWeek;
        CourseDurationText.Text = durationMonths.HasValue && durationMonths > 0
            ? ContractDurationHelper.FormatDuration(durationMonths.Value)
            : "—";
        CourseLessonsText.Text = lessons > 0 ? lessons.ToString(CultureInfo.InvariantCulture) : "—";
        CourseTimesPerWeekText.Text = timesPerWeek > 0 ? timesPerWeek.ToString(CultureInfo.InvariantCulture) : "—";
    }

    private void StartDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedCourse == null)
            return;

        var start = StartDatePicker.SelectedDate?.Date ?? DateTime.Today;
        EndDatePicker.SelectedDate = ContractDurationHelper.AddDuration(start, _selectedCourse.DurationMonths);
    }

    private void PricingField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressPricingRecalc || !_isPricingReady)
            return;

        RecalculatePricing(updateComment: true);
    }

    private void RecalculatePricing(bool updateComment)
    {
        if (!_isPricingReady)
            return;

        if (!TryParseDecimal(PriceBox.Text, out var price))
            price = 0;

        TryParseDecimal(DiscountPercentBox.Text, out var discountPercent);
        TryParseDecimal(DiscountAmountBox.Text, out var discountAmount);

        if (discountPercent < 0) discountPercent = 0;
        if (discountAmount < 0) discountAmount = 0;
        if (discountPercent > 100) discountPercent = 100;

        var percentAmount = price > 0 && discountPercent > 0
            ? Math.Round(price * discountPercent / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var total = ContractPricing.CalculateTotal(price, discountPercent, discountAmount);

        _suppressPricingRecalc = true;
        TotalBox.Text = total.ToString("0.##", CultureInfo.InvariantCulture);
        _suppressPricingRecalc = false;

        if (!updateComment)
            return;

        var discountNote = ContractPricing.BuildDiscountComment(price, discountPercent, percentAmount, discountAmount, total);
        if (string.IsNullOrWhiteSpace(discountNote))
        {
            CommentBox.Text = _userComment ?? string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(_userComment))
        {
            CommentBox.Text = discountNote;
            return;
        }

        CommentBox.Text = _userComment.Trim() + Environment.NewLine + discountNote;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearError();

            MaskedInputParser.TryGetSelectedId(DirectionCombo, out var dirId);
            MaskedInputParser.TryGetSelectedId(StudentTypeCombo, out var stypeId);
            TryParseDecimal(PriceBox.Text, out var price);
            TryParseDecimal(TotalBox.Text, out var total);
            TryParseDecimal(DiscountPercentBox.Text, out var discountPercent);
            TryParseDecimal(DiscountAmountBox.Text, out var discountAmount);

            _userComment = ContractPricing.StripDiscountComment(CommentBox.Text);
            if (_selectedCourse != null)
                RecalculatePricing(updateComment: true);

            var c = _existing ?? new Contract();
            c.ContractNumber = _existing?.ContractNumber ?? string.Empty;
            c.ContractDate = ContractDatePicker.SelectedDate ?? DateTime.Today;
            c.StartDate = StartDatePicker.SelectedDate;
            c.EndDate = EndDatePicker.SelectedDate ?? ContractDatePicker.SelectedDate?.AddMonths(9);
            c.IdStudent = _selectedStudentId ?? 0;
            if (c.IdStudent <= 0)
            {
                ShowError("Выберите обучающегося — на него будут записаны курс и абонемент.");
                return;
            }

            c.IdRepresentative = PayerCombo.SelectedValue is int payerId && payerId > 0 ? payerId : null;
            if (_selectedStudentIsMinor && c.IdRepresentative is null or <= 0)
            {
                ShowError("Несовершеннолетний может заключить договор: выберите родителя из его карточки. Сам договор оформляется на ребёнка.");
                return;
            }
            c.IdDirection = OptionalFieldDefaults.DirectionOrNull(dirId);
            c.IdStudentType = OptionalFieldDefaults.StudentTypeOrDefault(stypeId);
            c.LessonsCount = _lessonsCount > 0 ? _lessonsCount : 36;
            c.TimesPerWeek = _timesPerWeek > 0 ? _timesPerWeek : (short)1;
            c.PriceNoDiscount = price >= 0 ? price : 0;
            c.DiscountPercent = discountPercent > 0 ? discountPercent : null;
            c.DiscountAmount = discountAmount > 0 ? discountAmount : null;
            c.TotalAmount = total >= 0 ? total : c.PriceNoDiscount;
            c.Status = (StatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "активен";
            c.Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            c.IdBranch = BranchCombo.SelectedItem is StudyBranch branch && branch.IdBranch > 0
                ? branch.IdBranch
                : 1;

            if (DirectionCombo.SelectedItem is LookupItem dirItem)
                c.DirectionNameSnapshot = dirItem.Name.Replace(" (архив)", "", StringComparison.OrdinalIgnoreCase).Trim();

            if (_idContract.HasValue)
                await _repo.UpdateAsync(c);
            else
            {
                c.IdCreatedBy = UserSession.CurrentUserId;
                await _repo.CreateAsync(c);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.SaveFailed));
        }
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        return decimal.TryParse(text?.Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearError()
    {
        ErrorBorder.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
