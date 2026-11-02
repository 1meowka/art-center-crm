using System.Windows;
using System.Windows.Input;

namespace VKR.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutProgram_Click(object sender, MouseButtonEventArgs e)
        {
            ShowSection("about_program");
        }

        private void UserGuide_Click(object sender, MouseButtonEventArgs e)
        {
            ShowSection("user_guide");
        }

        private void RolesAccess_Click(object sender, MouseButtonEventArgs e)
        {
            ShowSection("roles_access");
        }

        private void ToMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowSection("menu");
        }

        private void ShowSection(string section)
        {
            MenuPanel.Visibility = section == "menu" ? Visibility.Visible : Visibility.Collapsed;
            AboutProgramPanel.Visibility = section == "about_program" ? Visibility.Visible : Visibility.Collapsed;
            RolesAccessPanel.Visibility = section == "roles_access" ? Visibility.Visible : Visibility.Collapsed;
            UserGuidePanel.Visibility = section == "user_guide" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
