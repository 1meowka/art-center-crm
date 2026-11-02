#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class DictionaryEditorWindow : Window
{
    private readonly DictionariesRepository _repository = new();
    private readonly DictionaryKind _kind;
    private readonly ObservableCollection<DictionaryEntry> _entries = new();

    public DictionaryEditorWindow(DictionaryKind kind)
    {
        InitializeComponent();
        _kind = kind;
        EntriesGrid.ItemsSource = _entries;

        HeaderTitleText.Text = DictionaryKindInfo.Title(kind);
        Title = DictionaryKindInfo.Title(kind) + " — Арт-студия";
        if (kind == DictionaryKind.Direction)
        {
            AddEntryButton.Content = "➕  Добавить направление";
            EmptyText.Text = "Направлений пока нет. Нажмите «Добавить направление».";
        }
        else if (kind == DictionaryKind.CoursePackage)
            AddEntryButton.Content = "➕  Добавить курс";
        IconText.Text = DictionaryKindInfo.Icon(kind);
        IconBorder.Background = (Brush)new BrushConverter().ConvertFromString(DictionaryKindInfo.Color(kind))!;

        if (_kind is DictionaryKind.Street or DictionaryKind.EducationalInstitution)
        {
            ImportExcelButton.Visibility = Visibility.Visible;
            ExportExcelButton.Visibility = Visibility.Visible;
        }

        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _entries.Clear();
            var rows = await _repository.GetEntriesAsync(_kind);
            foreach (var r in rows) _entries.Add(r);
            CountText.Text = $"всего записей: {_entries.Count}";
            EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_kind == DictionaryKind.Manager)
        {
            await EditManagerAsync(null);
            return;
        }

        OpenEntryEditor(null);
    }

    private async void EntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not DictionaryEntry entry)
            return;

        if (_kind == DictionaryKind.Manager)
        {
            await EditManagerAsync(entry.Id);
            return;
        }

        OpenEntryEditor(entry);
    }

    private void OpenEntryEditor(DictionaryEntry? entry)
    {
        var dialog = new DictionaryEntryDialog(_repository, _kind, entry) { Owner = this };
        dialog.Closed += async (_, _) =>
        {
            if (dialog.Saved)
                await ReloadAsync();
        };
        dialog.Show();
    }

    private async Task EditManagerAsync(int? idUser)
    {
        var users = AppServices.GetRequiredService<UserRepository>();
        var dialog = new UserEditDialog(users, idUser) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ReloadAsync();
    }

    private async void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            Title = "Заполнить справочник из Excel"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var rows = DictionaryExcelImporter.Read(dialog.FileName, _kind);
            var added = 0;
            var failed = 0;
            var errors = new List<string>();
            foreach (var row in rows)
            {
                try
                {
                    await _repository.SaveEntryAsync(_kind, row);
                    added++;
                }
                catch (Exception rowEx)
                {
                    failed++;
                    if (errors.Count < 8)
                        errors.Add($"{row.Name}: {UserFacingErrors.GetMessage(rowEx, rowEx.Message)}");
                }
            }

            await ReloadAsync();
            var hint = _kind is DictionaryKind.Street or DictionaryKind.EducationalInstitution
                or DictionaryKind.School or DictionaryKind.Kindergarten
                ? " Файл можно править и загружать снова — совпадающие названия обновятся."
                : string.Empty;
            var text = $"Загружено записей: {added}.";
            if (failed > 0)
                text += $" Не удалось: {failed}.\n" + string.Join("\n", errors);
            MessageBox.Show(text + hint, "Excel", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = DictionaryKindInfo.Title(_kind) + ".xlsx",
            Title = "Экспорт справочника"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            DictionaryExcelExporter.Write(dialog.FileName, _kind, _entries.ToList());
            MessageBox.Show("Файл сохранён.", "Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
}
