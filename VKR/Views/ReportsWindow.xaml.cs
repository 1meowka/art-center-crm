#nullable enable

using System.Windows;

using VKR.Services;

using VKR.Utilities;



namespace VKR.Views;



public partial class ReportsWindow : Window

{

    public ReportsWindow()
    {
        InitializeComponent();
        SectionHelp.Attach(this, AppModule.Reports);
        ApplyReportAccess();
    }

    private void ApplyReportAccess()
    {
        var user = UserSession.CurrentUser;
        OperationalReportButton.Visibility = AuthorizationService.CanViewReportExport(user, ReportExportKind.Operational)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FinanceReportButton.Visibility = AuthorizationService.CanViewReportExport(user, ReportExportKind.Finance)
            ? Visibility.Visible
            : Visibility.Collapsed;
        YearReportButton.Visibility = AuthorizationService.CanViewReportExport(user, ReportExportKind.Year)
            ? Visibility.Visible
            : Visibility.Collapsed;
        GeographyReportButton.Visibility = AuthorizationService.CanViewReportExport(user, ReportExportKind.Geography)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }



    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();



    private static bool EnsureReportAccess(ReportExportKind kind)
    {
        if (AuthorizationService.CanViewReportExport(UserSession.CurrentUser, kind))
            return true;

        MessageBox.Show(
            "Этот отчёт недоступен для вашей роли.",
            "Доступ ограничен",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void OperationalReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReportAccess(ReportExportKind.Operational))
            return;

        var dialog = new VpExportDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void FinanceReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReportAccess(ReportExportKind.Finance))
            return;

        var dialog = new PpoExportDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void YearReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReportAccess(ReportExportKind.Year))
            return;

        var dialog = new AdminReportExportDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void GeographyReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReportAccess(ReportExportKind.Geography))
            return;

        var window = new InstitutionGeographyWindow { Owner = this };
        window.ShowDialog();
    }

}

