#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VKR.Services;
using VKR.Views;

namespace VKR.Utilities;

public static class StudentCardOpener
{
    public static void Open(Window owner, int idStudent)
    {
        try
        {
            var repository = AppServices.GetRequiredService<StudentRepository>();
            var dialog = new StudentCardDialog(repository, idStudent) { Owner = owner };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                UserFacingErrors.GetMessage(ex, "Не удалось открыть карточку обучающегося."),
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public static bool ShouldIgnoreGridClick(DependencyObject? source)
    {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is CheckBox or Button or TextBox)
                return true;
        }

        return false;
    }
}
