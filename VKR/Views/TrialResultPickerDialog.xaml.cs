#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;

namespace VKR.Views;

public partial class TrialResultPickerDialog : Window
{
    public string? SelectedResult { get; private set; }

    public TrialResultPickerDialog(TrialLessonListItem item, string[] results)
    {
        InitializeComponent();
        SummaryText.Text = $"{item.ChildFullName} · {item.ScheduledText} · {item.DirectionName}";
        ResultCombo.ItemsSource = results.Select(x => new ComboBoxItem { Content = x }).ToList();
        ResultCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SelectedResult = (ResultCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
