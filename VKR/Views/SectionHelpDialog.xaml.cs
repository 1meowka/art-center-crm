#nullable enable
using System.Windows;

namespace VKR.Views;

public partial class SectionHelpDialog : Window
{
    public SectionHelpDialog(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
