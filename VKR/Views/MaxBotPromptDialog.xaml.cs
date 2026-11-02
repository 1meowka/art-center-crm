#nullable enable
using System.Windows;

namespace VKR.Views;

public partial class MaxBotPromptDialog : Window
{
    public string Value { get; private set; } = string.Empty;

    public MaxBotPromptDialog(string title, string label, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        LabelText.Text = label;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = InputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Value))
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
