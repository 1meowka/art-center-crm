#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class TeacherGroupsDialog : Window
{
    private readonly GroupsRepository _groupsRepo = new();
    private readonly TeachersRepository _teachersRepo;
    private readonly int _idTeacher;

    public TeacherGroupsDialog(TeachersRepository teachersRepo, int idTeacher)
    {
        InitializeComponent();
        _teachersRepo = teachersRepo;
        _idTeacher = idTeacher;
        Loaded += async (_, _) =>
        {
            try
            {
                var all = await _teachersRepo.GetTeachersAsync();
                var t = all.FirstOrDefault(x => x.IdTeacher == _idTeacher);
                TeacherNameText.Text = t?.FullName ?? "";

                var groups = await _groupsRepo.GetGroupsAsync();
                var filtered = groups.Where(g => g.IdTeacher == _idTeacher).ToList();
                GroupsGrid.ItemsSource = filtered;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
