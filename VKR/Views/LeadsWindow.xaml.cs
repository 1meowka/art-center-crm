using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class LeadsWindow : Window
{
    private readonly LeadRepository _repo = AppServices.GetRequiredService<LeadRepository>();
    private readonly ObservableCollection<Lead> _allLeads = new();
    private ICollectionView? _view;
    private bool _isLoaded;
    private readonly SearchDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(350));

    public LeadsWindow()
    {
        InitializeComponent();
        _searchDebouncer.Debounced += RefreshFilterAsync;
        Loaded += async (_, _) =>
        {
            _isLoaded = true;
            await LoadDataAsync(CancellationToken.None);
        };
        Unloaded += (_, _) => _searchDebouncer.Dispose();
    }

    private Task RefreshFilterAsync(CancellationToken cancellationToken)
    {
        _view?.Refresh();
        UpdateShownCount();
        return Task.CompletedTask;
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var leads = await _repo.GetAllAsync(cancellationToken);
            _allLeads.Clear();
            foreach (var lead in leads)
                _allLeads.Add(lead);

            _view = CollectionViewSource.GetDefaultView(_allLeads);
            _view.Filter = FilterLead;
            LeadsDataGrid.ItemsSource = _view;

            UpdateStats();
            UpdateShownCount();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed),
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateStats()
    {
        if (!_isLoaded) return;

        TotalCountText.Text = _allLeads.Count.ToString();
        NewCountText.Text = _allLeads.Count(x => x.Status == "новая").ToString();
        InProgressCountText.Text = _allLeads.Count(x => x.Status == "подтверждена" || x.Status == "посещено").ToString();
        ConvertedCountText.Text = _allLeads.Count(x => x.Status == "конвертирована").ToString();
    }

    private void UpdateShownCount()
    {
        if (!_isLoaded) return;

        int shown = _view?.Cast<Lead>().Count() ?? 0;
        ShownCountText.Text = $"Показано: {shown} из {_allLeads.Count}";
        EmptyStatePanel.Visibility = shown == 0 && _allLeads.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool FilterLead(object obj)
    {
        if (obj is not Lead lead) return false;

        string search = SearchTextBox?.Text?.Trim().ToLower() ?? "";
        if (!string.IsNullOrEmpty(search))
        {
            bool match = lead.SearchBlob.ToLower().Contains(search);
            if (!match) return false;
        }

        string? selectedStatus = (StatusFilterComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(selectedStatus) && selectedStatus != "Все статусы")
        {
            if (lead.Status != selectedStatus) return false;
        }

        return true;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        _searchDebouncer.Schedule();
    }

    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        _view?.Refresh();
        UpdateShownCount();
    }

    private void LeadsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        bool hasSelection = LeadsDataGrid.SelectedItem != null;
        OpenCardButton.IsEnabled = hasSelection;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection && AuthorizationService.CanDeleteRecords(UserSession.CurrentUser);
        if (LeadsDataGrid.SelectedItem is Lead lead)
            FilledFieldsText.Text = string.IsNullOrWhiteSpace(lead.FilledFieldsText)
                ? "Нет заполненных полей."
                : lead.FilledFieldsText;
        else
            FilledFieldsText.Text = "Выберите гостя в списке. Двойной щелчок открывает карточку.";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadDataAsync(CancellationToken.None);

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LeadEditDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadDataAsync(CancellationToken.None);
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (LeadsDataGrid.SelectedItem is Lead selected)
            OpenEditDialog(selected);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (LeadsDataGrid.SelectedItem is not Lead selected) return;

        if (!AuthorizationService.CanDeleteRecords(UserSession.CurrentUser))
        {
            MessageBox.Show("Недостаточно прав для удаления.", "Доступ запрещён",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Удалить карточку гостя №{selected.IdLead} ({selected.ChildFullName})?\n\n" +
            "Это действие нельзя отменить.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _repo.DeleteAsync(selected.IdLead);
                await LoadDataAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UserFacingErrors.GetMessage(ex, "Не удалось удалить карточку гостя."),
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void OpenCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (LeadsDataGrid.SelectedItem is Lead selected)
            OpenGuestCard(selected);
    }

    private void LeadsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LeadsDataGrid.SelectedItem is not Lead selected) return;
        OpenGuestCard(selected);
    }

    private void OpenGuestCard(Lead selected)
    {

        var card = new LeadCardDialog(selected) { Owner = this };
        card.ShowDialog();

        switch (card.Action)
        {
            case LeadCardAction.Edit:
                OpenEditDialog(selected);
                break;
            case LeadCardAction.Convert:
                OpenConvertDialog(selected);
                break;
        }
    }

    private async void OpenEditDialog(Lead lead)
    {
        var dialog = new LeadEditDialog(lead) { Owner = this };
        if (dialog.ShowDialog() == true)
            await LoadDataAsync(CancellationToken.None);
    }

    private void OpenConvertDialog(Lead lead)
    {
        _ = OpenConvertDialogAsync(lead);
    }

    private async Task OpenConvertDialogAsync(Lead lead)
    {
        var freshLead = await _repo.GetByIdAsync(lead.IdLead) ?? lead;
        var dialog = new ConvertLeadDialog(freshLead) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            await LoadDataAsync(CancellationToken.None);

            MessageBox.Show(
                $"✅ Готово!\n\n" +
                $"Обучающийся «{dialog.Result.LastName} {dialog.Result.FirstName}» " +
                $"успешно зачислен.\n\n" +
                $"Карточка гостя №{lead.IdLead} помечена как «конвертирована».",
                "Зачисление выполнено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Импорт карточек гостей" };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var students = AppServices.GetRequiredService<StudentRepository>();
            var dictionaries = new DictionariesRepository();
            var (added, skipped, errors) = await ClientExcelService.ImportAsync(
                dialog.FileName, _repo, students, dictionaries);
            await LoadDataAsync(CancellationToken.None);
            var text = $"Добавлено: {added}. Пропущено (уже есть): {skipped}.";
            if (errors.Count > 0)
                text += "\n" + string.Join("\n", errors.Take(8));
            MessageBox.Show(text, "Импорт карточек гостей", MessageBoxButton.OK,
                errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex, "Не удалось импортировать карточки гостей."),
                "Импорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = "Экспорт_гостей.xlsx",
            Title = "Экспорт карточек гостей"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            ClientExcelService.Write(dialog.FileName, _allLeads);
            MessageBox.Show("Файл сохранён.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
