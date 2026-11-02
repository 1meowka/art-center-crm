#nullable enable
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VKR.Services;

namespace VKR.Views;

public partial class NotificationsWindow : Window
{
    private readonly NotificationsRepository _notificationsRepository = AppServices.GetRequiredService<NotificationsRepository>();
    private readonly StudentSatisfactionRepository _satisfactionRepository = AppServices.GetRequiredService<StudentSatisfactionRepository>();
    private readonly ContractRenewalRepository _renewalRepository = AppServices.GetRequiredService<ContractRenewalRepository>();
    private readonly TrialFollowupRepository _trialFollowupRepository = new();

    public NotificationsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshBadgesAsync();
    }

    private async Task RefreshBadgesAsync()
    {
        try
        {
            ClearError();
            NotificationSchema.Ensure();
            AcademicSchema.Ensure();
            var birthdays = await _notificationsRepository.CountTodayBirthdaysAsync();
            var satisfaction = await _satisfactionRepository.CountDueSurveysAsync();
            var renewal = await _renewalRepository.CountDueAsync();
            var trialFollowup = await _trialFollowupRepository.CountDueAsync();
            BirthdaysBadgeText.Text = FormatBadge(birthdays);
            SatisfactionBadgeText.Text = FormatBadge(satisfaction);
            RenewalBadgeText.Text = FormatBadge(renewal);
            TrialFollowupBadgeText.Text = FormatBadge(trialFollowup);
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить счётчики: " + ex.Message);
        }
    }

    private static string FormatBadge(int count) => count > 99 ? "99+" : count.ToString();

    private void BirthdaysCard_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new BirthdaysNotificationsWindow { Owner = this };
        win.ShowDialog();
        _ = RefreshBadgesAsync();
    }

    private void SatisfactionCard_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new StudentSatisfactionWindow { Owner = this };
        win.ShowDialog();
        _ = RefreshBadgesAsync();
    }

    private void RenewalCard_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new ContractRenewalWindow { Owner = this };
        win.ShowDialog();
        _ = RefreshBadgesAsync();
    }

    private void TrialFollowupCard_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new TrialFollowupWindow { Owner = this };
        win.ShowDialog();
        _ = RefreshBadgesAsync();
    }

    private void StatsCard_Click(object sender, MouseButtonEventArgs e)
    {
        var win = new NotificationStatsWindow { Owner = this };
        win.ShowDialog();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
