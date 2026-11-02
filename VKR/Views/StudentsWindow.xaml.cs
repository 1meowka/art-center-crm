#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class StudentsWindow : Window
{
    private enum SectionView
    {
        Hub,
        Students,
        Representatives
    }

    private readonly StudentRepository _repository = AppServices.GetRequiredService<StudentRepository>();
    private readonly ObservableCollection<StudentListItem> _students = new();
    private readonly ObservableCollection<RepresentativeListItem> _representatives = new();
    private IReadOnlyList<LookupItem> _relationTypes = Array.Empty<LookupItem>();
    private IReadOnlyList<IntakePeriod> _intakePeriods = Array.Empty<IntakePeriod>();
    private readonly SearchDebouncer _studentSearchDebouncer = new(TimeSpan.FromMilliseconds(400));
    private readonly SearchDebouncer _representativeSearchDebouncer = new(TimeSpan.FromMilliseconds(400));

    private bool _uiReady;
    private SectionView _currentView = SectionView.Hub;
    private bool _suppressIntakeFilterChange;

    public StudentsWindow()
    {
        InitializeComponent();

        StudentsGrid.ItemsSource = _students;
        RepresentativesGrid.ItemsSource = _representatives;

        Loaded += StudentsWindow_Loaded;
        Unloaded += StudentsWindow_Unloaded;

        _studentSearchDebouncer.Debounced += SearchStudentsDebouncedAsync;
        _representativeSearchDebouncer.Debounced += SearchRepresentativesDebouncedAsync;
    }

    private Task SearchStudentsDebouncedAsync(CancellationToken cancellationToken) =>
        SafeRunStudentsAsync(ReloadStudentsAsync);

    private Task SearchRepresentativesDebouncedAsync(CancellationToken cancellationToken) =>
        SafeRunRepresentativesAsync(ReloadRepresentativesAsync);

    private async void StudentsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiReady)
            return;

        _uiReady = true;

        StatusCombo.SelectionChanged -= StatusCombo_SelectionChanged;
        StatusCombo.SelectionChanged += StatusCombo_SelectionChanged;

        await SafeRunStudentsAsync(async () =>
        {
            _relationTypes = RelationTypeCatalog.ForForm(await _repository.GetRelationTypesAsync());
            await LoadIntakePeriodsAsync();
        });

        ShowView(SectionView.Students);
        await SafeRunStudentsAsync(ReloadStudentsAsync);
    }

    private void StudentsWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        StatusCombo.SelectionChanged -= StatusCombo_SelectionChanged;
        _studentSearchDebouncer.Dispose();
        _representativeSearchDebouncer.Dispose();
    }

    private void ShowView(SectionView view)
    {
        _currentView = view;

        HubPanel.Visibility = view == SectionView.Hub ? Visibility.Visible : Visibility.Collapsed;
        StudentsPanel.Visibility = view == SectionView.Students ? Visibility.Visible : Visibility.Collapsed;
        RepresentativesPanel.Visibility = view == SectionView.Representatives ? Visibility.Visible : Visibility.Collapsed;

        SectionTitleText.Text = view switch
        {
            SectionView.Students => "Клиенты",
            SectionView.Representatives => "Представители",
            _ => "Клиенты"
        };

        BackButton.Content = "← Назад";
    }

    private async void OpenStudentsView_Click(object sender, RoutedEventArgs e)
    {
        ShowView(SectionView.Students);
        await SafeRunStudentsAsync(ReloadStudentsAsync);
    }

    private async void OpenRepresentativesView_Click(object sender, RoutedEventArgs e)
    {
        ShowView(SectionView.Representatives);
        await SafeRunRepresentativesAsync(ReloadRepresentativesAsync);
    }

    private async Task LoadIntakePeriodsAsync()
    {
        _intakePeriods = await _repository.GetIntakePeriodsAsync();

        _suppressIntakeFilterChange = true;
        IntakePeriodCombo.ItemsSource = null;

        var items = new List<IntakePeriod>
        {
            new() { Key = string.Empty, Label = "все приходы" }
        };
        items.AddRange(_intakePeriods);

        IntakePeriodCombo.ItemsSource = items;
        IntakePeriodCombo.SelectedIndex = 0;
        _suppressIntakeFilterChange = false;
    }

    private async Task ReloadStudentsAsync()
    {
        ClearStudentsError();
        _students.Clear();

        var segment = GetSelectedSegment();
        var period = GetSelectedIntakePeriod();

        IReadOnlyList<StudentListItem> rows;
        if (segment == "пробные")
        {
            rows = await _repository.GetTrialProspectsAsync(StudentSearchBox?.Text);
        }
        else
        {
            rows = await _repository.GetStudentsAsync(
                StudentSearchBox?.Text,
                segment,
                OnlyMinorsCheck?.IsChecked == true,
                period?.From,
                period?.To);
        }

        foreach (var row in rows)
            _students.Add(row);

        StudentsCountText.Text = period == null
            ? $"найдено: {_students.Count}"
            : $"найдено: {_students.Count} · {period.Label}";

        UpdateSelectedStudentPanel();
    }

    private async Task ReloadRepresentativesAsync()
    {
        ClearRepresentativesError();
        _representatives.Clear();

        var rows = await _repository.GetRepresentativesAsync(RepresentativeSearchBox?.Text);

        foreach (var row in rows)
            _representatives.Add(row);

        RepresentativesCountText.Text = $"найдено: {_representatives.Count}";
        UpdateSelectedRepresentativePanel();
    }

    private string? GetSelectedSegment()
    {
        if (StatusCombo?.SelectedItem is ComboBoxItem item)
        {
            var value = item.Content?.ToString();
            return value == "все" ? null : value;
        }

        return null;
    }

    private IntakePeriod? GetSelectedIntakePeriod()
    {
        if (IntakePeriodCombo?.SelectedItem is IntakePeriod period && !string.IsNullOrEmpty(period.Key))
            return period;

        return null;
    }

    private async void RefreshStudents_Click(object sender, RoutedEventArgs e)
    {
        await SafeRunStudentsAsync(async () =>
        {
            await LoadIntakePeriodsAsync();
            await ReloadStudentsAsync();
        });
    }

    private async void ImportClients_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Импорт клиентов" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var leads = AppServices.GetRequiredService<LeadRepository>();
            var (added, skipped, errors) = await ClientExcelService.ImportAsync(
                dialog.FileName, leads, _repository, new DictionariesRepository());
            await ReloadStudentsAsync();
            var text = $"Добавлено: {added}. Пропущено: {skipped}.";
            if (errors.Count > 0)
                text += "\n" + string.Join("\n", errors.Take(8));
            MessageBox.Show(text, "Импорт клиентов", MessageBoxButton.OK,
                errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex, "Не удалось импортировать клиентов."),
                "Импорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportClients_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = "Экспорт_клиентов.xlsx",
            Title = "Экспорт клиентов"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var leads = await AppServices.GetRequiredService<LeadRepository>().GetAllAsync();
            ClientExcelService.Write(dialog.FileName, leads);
            MessageBox.Show("Файл сохранён.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshRepresentatives_Click(object sender, RoutedEventArgs e)
    {
        await SafeRunRepresentativesAsync(ReloadRepresentativesAsync);
    }

    private async void StudentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _currentView != SectionView.Students)
            return;

        var text = StudentSearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _studentSearchDebouncer.Schedule();
    }

    private async void RepresentativeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _currentView != SectionView.Representatives)
            return;

        var text = RepresentativeSearchBox?.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _representativeSearchDebouncer.Schedule();
    }

    private async void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _currentView != SectionView.Students)
            return;

        await SafeRunStudentsAsync(ReloadStudentsAsync);
    }

    private async void IntakePeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _currentView != SectionView.Students || _suppressIntakeFilterChange)
            return;

        await SafeRunStudentsAsync(ReloadStudentsAsync);
    }

    private async void OnlyMinorsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _currentView != SectionView.Students)
            return;

        await SafeRunStudentsAsync(ReloadStudentsAsync);
    }

    private void StudentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedStudentPanel();

    private void RepresentativesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedRepresentativePanel();

    private void UpdateSelectedStudentPanel()
    {
        if (SelectedStudentTitle == null || SelectedStudentInfo == null)
            return;

        if (StudentsGrid?.SelectedItem is not StudentListItem item)
        {
            SelectedStudentTitle.Text = "Клиент не выбран";
            SelectedStudentInfo.Text = "Выберите строку для быстрых действий и привязки представителя.";
            return;
        }

        SelectedStudentTitle.Text = item.FullName;
        if (item.IsTrialProspect)
        {
            SelectedStudentInfo.Text =
                $"Пробный клиент · направление: {item.GroupDisplay}, клиент: {item.MainPayer}, тел.: {item.Phone}, пробное: {item.ArrivalDateText}.";
            return;
        }

        SelectedStudentInfo.Text =
            $"Приход: {item.ArrivalPeriodText} ({item.ArrivalDateText}), {item.MinorText}, статус: {item.Status}, " +
            $"группа: {item.GroupDisplay}, представителей: {item.RepresentativeState}, плательщик: {item.MainPayer}.";
    }

    private void UpdateSelectedRepresentativePanel()
    {
        if (SelectedRepresentativeTitle == null || SelectedRepresentativeInfo == null)
            return;

        if (RepresentativesGrid?.SelectedItem is not RepresentativeListItem item)
        {
            SelectedRepresentativeTitle.Text = "Представитель не выбран";
            SelectedRepresentativeInfo.Text = "Выберите строку, чтобы привязать ребёнка или открыть карточку.";
            return;
        }

        SelectedRepresentativeTitle.Text = item.FullName;
        SelectedRepresentativeInfo.Text =
            $"Телефон: {item.Phone}. Дети: {item.ChildrenNames}. Основной плательщик для: {item.MainPayerFor}.";
    }

    private async void AddStudent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LeadEditDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            await SafeRunStudentsAsync(async () =>
            {
                await LoadIntakePeriodsAsync();
                await ReloadStudentsAsync();
            });
    }

    private async void AddRepresentative_Click(object sender, RoutedEventArgs e)
    {
        var edit = new RepresentativeEditModel();

        var dialog = new RepresentativeEditDialog(edit, saveRepository: _repository) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        RepresentativeListItem? created = null;

        await SafeRunRepresentativesAsync(async () =>
        {
            await ReloadRepresentativesAsync();

            created = _representatives.FirstOrDefault(x => x.IdRepresentative == dialog.Result.IdRepresentative);
            if (created != null)
                RepresentativesGrid.SelectedItem = created;
        });

        if (created == null)
            return;

        var addChildNow = MessageBox.Show(
            "Представитель добавлен. Сразу привязать ребёнка?",
            "Привязка ребёнка",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (addChildNow == MessageBoxResult.Yes)
            await AttachStudentToRepresentativeAsync(created);
    }

    private async void AddChildForRepresentative_Click(object sender, RoutedEventArgs e)
    {
        if (RepresentativesGrid?.SelectedItem is not RepresentativeListItem representative)
        {
            SetRepresentativesError("Сначала выберите представителя.");
            return;
        }

        await AddChildForRepresentativeAsync(representative);
    }

    private async Task AddChildForRepresentativeAsync(RepresentativeListItem representative)
    {
        if (_relationTypes.Count == 0)
        {
            SetRepresentativesError("В справочнике нет типов родства.");
            return;
        }

        var attachDialog = new StudentRepresentativeAttachDialog(
            0,
            representative.IdRepresentative,
            representative.FullName,
            _relationTypes)
        {
            Owner = this
        };

        if (attachDialog.ShowDialog() != true)
            return;

        var relation = _relationTypes.FirstOrDefault(x => x.Id == attachDialog.Result.IdRelationType);
        if (relation == null)
        {
            SetRepresentativesError("Выбранный тип родства больше недоступен.");
            return;
        }

        var initialLink = new StudentRepresentativeLink
        {
            IdStudent = 0,
            IdRepresentative = representative.IdRepresentative,
            RepresentativeFullName = representative.FullName,
            Phone = representative.Phone,
            IdRelationType = attachDialog.Result.IdRelationType,
            RelationName = relation.Name,
            IsMainPayer = attachDialog.Result.IsMainPayer,
            IsLegalRepresentative = attachDialog.Result.IsLegalRepresentative,
            Note = attachDialog.Result.Note
        };

        var dialog = new StudentEditDialog(_repository, initialRepresentativeLink: initialLink) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        await SafeRunRepresentativesAsync(async () =>
        {
            await ReloadRepresentativesAsync();

            var updatedRepresentative = _representatives.FirstOrDefault(x => x.IdRepresentative == representative.IdRepresentative);
            if (updatedRepresentative != null)
                RepresentativesGrid.SelectedItem = updatedRepresentative;
        });
    }

    private async void AttachRepresentativeToStudent_Click(object sender, RoutedEventArgs e)
    {
        if (StudentsGrid?.SelectedItem is not StudentListItem student)
        {
            SetStudentsError("Сначала выберите обучающегося.");
            return;
        }

        if (_relationTypes.Count == 0)
        {
            SetStudentsError("В справочнике нет типов родства.");
            return;
        }

        var picker = new RepresentativePickerDialog(_repository) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRepresentative == null)
            return;

        var representative = picker.SelectedRepresentative;

        var attachDialog = new StudentRepresentativeAttachDialog(
            student.IdStudent,
            representative.IdRepresentative,
            representative.FullName,
            _relationTypes)
        {
            Owner = this
        };

        if (attachDialog.ShowDialog() != true)
            return;

        await SafeRunStudentsAsync(async () =>
        {
            await _repository.AttachRepresentativeAsync(attachDialog.Result);
            await ReloadStudentsAsync();
        });
    }

    private async void AttachStudentToRepresentative_Click(object sender, RoutedEventArgs e)
    {
        if (RepresentativesGrid?.SelectedItem is not RepresentativeListItem representative)
        {
            SetRepresentativesError("Сначала выберите представителя.");
            return;
        }

        await AttachStudentToRepresentativeAsync(representative);
    }

    private async Task AttachStudentToRepresentativeAsync(RepresentativeListItem representative)
    {
        if (_relationTypes.Count == 0)
        {
            SetRepresentativesError("В справочнике нет типов родства.");
            return;
        }

        var initialLink = new StudentRepresentativeLink
        {
            IdRepresentative = representative.IdRepresentative,
            RepresentativeFullName = representative.FullName,
            Phone = representative.Phone
        };

        var picker = new StudentPickerDialog(_repository, initialLink) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedStudent == null)
            return;

        var student = picker.SelectedStudent;

        var attachDialog = new StudentRepresentativeAttachDialog(
            student.IdStudent,
            representative.IdRepresentative,
            representative.FullName,
            _relationTypes)
        {
            Owner = this
        };

        if (attachDialog.ShowDialog() != true)
            return;

        await SafeRunRepresentativesAsync(async () =>
        {
            await _repository.AttachRepresentativeAsync(attachDialog.Result);
            await ReloadRepresentativesAsync();
        });
    }

    private async void EditStudent_Click(object sender, RoutedEventArgs e)
    {
        if (StudentsGrid?.SelectedItem is not StudentListItem item)
        {
            SetStudentsError("Сначала выберите обучающегося.");
            return;
        }

        var leads = AppServices.GetRequiredService<LeadRepository>();
        var leadId = item.IdLead is > 0 ? item.IdLead : await leads.GetLeadIdByStudentIdAsync(item.IdStudent);
        if (leadId is > 0)
        {
            var lead = await leads.GetByIdAsync(leadId.Value);
            if (lead != null)
            {
                var dialog = new LeadEditDialog(lead) { Owner = this };
                if (dialog.ShowDialog() == true)
                    await SafeRunStudentsAsync(ReloadStudentsAsync);
                return;
            }
        }

        await OpenGuestCardForStudentAsync(item);
    }

    private async void OpenStudentCard_Click(object sender, RoutedEventArgs e) =>
        await OpenSelectedStudentGuestAsync();

    private async void StudentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        await OpenSelectedStudentGuestAsync();

    private async Task OpenSelectedStudentGuestAsync()
    {
        if (StudentsGrid?.SelectedItem is not StudentListItem item)
        {
            SetStudentsError("Выберите обучающегося для открытия карточки.");
            return;
        }

        await OpenGuestCardForStudentAsync(item);
    }

    private async Task OpenGuestCardForStudentAsync(StudentListItem item)
    {
        var leads = AppServices.GetRequiredService<LeadRepository>();
        var leadId = item.IdLead is > 0 ? item.IdLead : await leads.GetLeadIdByStudentIdAsync(item.IdStudent);
        if (leadId is not > 0)
        {
            new StudentCardDialog(_repository, item.IdStudent) { Owner = this }.ShowDialog();
            return;
        }

        var lead = await leads.GetByIdAsync(leadId.Value);
        if (lead == null)
        {
            new StudentCardDialog(_repository, item.IdStudent) { Owner = this }.ShowDialog();
            return;
        }

        var card = new LeadCardDialog(lead) { Owner = this };
        card.ShowDialog();
        if (card.Action == LeadCardAction.Edit)
        {
            var edit = new LeadEditDialog(lead) { Owner = this };
            if (edit.ShowDialog() == true)
                await SafeRunStudentsAsync(ReloadStudentsAsync);
        }
    }

    private void RepresentativesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        OpenSelectedRepresentativeCard();

    private void OpenRepresentativeCard_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedRepresentativeCard();

    private void OpenSelectedRepresentativeCard()
    {
        if (RepresentativesGrid?.SelectedItem is not RepresentativeListItem item)
        {
            SetRepresentativesError("Выберите представителя для открытия карточки.");
            return;
        }

        new RepresentativeCardDialog(_repository, item.IdRepresentative) { Owner = this }.ShowDialog();
    }

    private async void EditRepresentative_Click(object sender, RoutedEventArgs e)
    {
        if (RepresentativesGrid?.SelectedItem is not RepresentativeListItem item)
        {
            SetRepresentativesError("Сначала выберите представителя.");
            return;
        }

        await SafeRunRepresentativesAsync(async () =>
        {
            var details = await _repository.GetRepresentativeDetailsAsync(item.IdRepresentative);
            var representative = details.Representative;

            var edit = new RepresentativeEditModel
            {
                IdRepresentative = representative.IdRepresentative,
                LastName = representative.LastName,
                FirstName = representative.FirstName,
                MiddleName = representative.MiddleName,
                Phone = representative.Phone,
                Email = representative.Email,
                ResidenceAddress = representative.ResidenceAddress,
                Age = representative.Age,
                Workplace = representative.Workplace,
                Comment = representative.Comment,
                FallbackRelationTypeId = representative.IdRelationTypeLegacy
            };

            var dialog = new RepresentativeEditDialog(edit, saveRepository: _repository) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            await ReloadRepresentativesAsync();

            var updated = _representatives.FirstOrDefault(x => x.IdRepresentative == dialog.Result.IdRepresentative);
            if (updated != null)
                RepresentativesGrid.SelectedItem = updated;
        });
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentView == SectionView.Hub)
            Close();
        else
            ShowView(SectionView.Hub);
    }

    private async Task SafeRunStudentsAsync(Func<Task> action)
    {
        try
        {
            ClearStudentsError();
            await action();
        }
        catch (Exception ex)
        {
            SetStudentsError(UserFacingErrors.GetMessage(ex));
        }
    }

    private async Task SafeRunRepresentativesAsync(Func<Task> action)
    {
        try
        {
            ClearRepresentativesError();
            await action();
        }
        catch (Exception ex)
        {
            SetRepresentativesError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void ClearStudentsError()
    {
        if (StudentsErrorText != null)
            StudentsErrorText.Text = string.Empty;
        if (StudentsErrorBorder != null)
            StudentsErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void ClearRepresentativesError()
    {
        if (RepresentativesErrorText != null)
            RepresentativesErrorText.Text = string.Empty;
        if (RepresentativesErrorBorder != null)
            RepresentativesErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void SetStudentsError(string message)
    {
        if (StudentsErrorText != null)
            StudentsErrorText.Text = message;
        if (StudentsErrorBorder != null)
            StudentsErrorBorder.Visibility = Visibility.Visible;
    }

    private void SetRepresentativesError(string message)
    {
        if (RepresentativesErrorText != null)
            RepresentativesErrorText.Text = message;
        if (RepresentativesErrorBorder != null)
            RepresentativesErrorBorder.Visibility = Visibility.Visible;
    }
}
