#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class ClientNotesWindow : Window
{
    private readonly ClientNoteRepository _repository = new();
    private readonly string _entityType;
    private readonly int _entityId;
    private readonly string _title;

    public ClientNotesWindow(string entityType, int entityId, string title)
    {
        InitializeComponent();
        _entityType = entityType;
        _entityId = entityId;
        _title = title;
        SubtitleText.Text = title;
        Loaded += async (_, _) => await ReloadAsync();
    }

    public static void OpenForStudent(Window owner, int idStudent, string fullName) =>
        new ClientNotesWindow(ClientNoteEntityType.Student, idStudent, fullName) { Owner = owner }.ShowDialog();

    public static void OpenForLead(Window owner, int idLead, string childName) =>
        new ClientNotesWindow(ClientNoteEntityType.Lead, idLead, childName) { Owner = owner }.ShowDialog();

    public static void OpenForRepresentative(Window owner, int idRepresentative, string fullName) =>
        new ClientNotesWindow(ClientNoteEntityType.Representative, idRepresentative, fullName) { Owner = owner }.ShowDialog();

    private async Task ReloadAsync()
    {
        var notes = await _repository.GetNotesAsync(_entityType, _entityId);
        NotesList.ItemsSource = notes;
        EmptyText.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NewNoteBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            await _repository.AddNoteAsync(_entityType, _entityId, text, UserSession.CurrentUserId);
            NewNoteBox.Clear();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
