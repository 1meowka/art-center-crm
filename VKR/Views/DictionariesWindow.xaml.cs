#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Services;

namespace VKR.Views;

public partial class DictionariesWindow : Window
{
    public DictionariesWindow()
    {
        InitializeComponent();
        HubGrid.ItemsSource = BuildHub();
    }

    private static DictionaryHubRow[] BuildHub()
    {
        var rows = new System.Collections.Generic.List<DictionaryHubRow>
        {
            new("Преподаватели", "Список преподавателей строками", "section:teachers"),
            new("Группы", "Группы и расписание", "section:groups"),
            new("Мастер-классы", "Разовые события календаря", "section:events"),
        };

        foreach (DictionaryKind kind in Enum.GetValues<DictionaryKind>())
        {
            if (kind is DictionaryKind.School or DictionaryKind.Kindergarten or DictionaryKind.ShopProduct)
                continue;
            rows.Add(new(DictionaryKindInfo.Title(kind), DictionaryKindInfo.Subtitle(kind), "dict:" + kind));
        }

        return rows.ToArray();
    }

    private void HubGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HubGrid.SelectedItem is not DictionaryHubRow row)
            return;
        OpenRow(row);
    }

    private void OpenRow(DictionaryHubRow row)
    {
        if (row.Key.StartsWith("dict:") && Enum.TryParse<DictionaryKind>(row.Key[5..], out var kind))
        {
            if (kind is DictionaryKind.School or DictionaryKind.Kindergarten)
                kind = DictionaryKind.EducationalInstitution;
            new DictionaryEditorWindow(kind) { Owner = this }.ShowDialog();
            return;
        }

        Window? win = row.Key switch
        {
            "section:students" => new StudentsWindow { Owner = this },
            "section:teachers" => new TeachersWindow { Owner = this },
            "section:groups" => new GroupsWindow { Owner = this },
            "section:events" => new CalendarEventsWindow { Owner = this },
            _ => null
        };
        win?.ShowDialog();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class DictionaryHubRow
{
    public DictionaryHubRow(string title, string subtitle, string key)
    {
        Title = title;
        Subtitle = subtitle;
        Key = key;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Key { get; }
}
