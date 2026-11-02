using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VKR.Resources;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR
{
    public partial class MainWindow : Window
    {
        private readonly DashboardRepository _dashboardRepository = AppServices.GetRequiredService<DashboardRepository>();
        private readonly NotificationsRepository _notificationsRepository = AppServices.GetRequiredService<NotificationsRepository>();
        private readonly StudentSatisfactionRepository _satisfactionRepository = AppServices.GetRequiredService<StudentSatisfactionRepository>();
        private readonly ContractRenewalRepository _renewalRepository = AppServices.GetRequiredService<ContractRenewalRepository>();
        private readonly MaxBotPollingService _maxBotPoller = AppServices.GetRequiredService<MaxBotPollingService>();
        private readonly CultureInfo _ruCulture = CultureInfo.GetCultureInfo("ru-RU");
        private bool _isPeriodInitializing;
        private bool _isDashboardLoading;
        private SessionIdleMonitor? _idleMonitor;
        private DispatcherTimer? _maxBotBadgeTimer;
        private SectionShell? _sectionShell;

        public AppUser? CurrentUser { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            WindowMinimize.Attach(this);
            _sectionShell = new SectionShell(this, HomeWorkArea, SectionHost);
            _sectionShell.ReturnedHome += OnSectionReturnedHome;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _sectionShell?.Dispose();
            _maxBotBadgeTimer?.Stop();
            _maxBotPoller.UnreadCountChanged -= OnMaxBotUnreadChanged;
            if (!MaxBotOptions.UseExternalWorker(App.Configuration))
                _maxBotPoller.Stop();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (CurrentUser == null)
            {
                MessageBox.Show(
                    "Сессия не установлена. Выполните вход в систему.",
                    "Доступ запрещён",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Close();
                return;
            }

            UserSession.SignIn(CurrentUser);
            ConfigureUserButton();
            WelcomeText.Text = $"Добро пожаловать, {CurrentUser.FirstName}!";

            ApplyModuleAccess();
            ApplyDashboardAccess();
            SetActiveNav("Home");

            _idleMonitor = new SessionIdleMonitor(this, OnSessionIdleTimeout);
            Closed += (_, _) => _idleMonitor.Dispose();

            _isPeriodInitializing = true;
            PeriodPresetCombo.SelectedIndex = 0;
            _isPeriodInitializing = false;

            ApplyPeriodPreset("CurrentMonth");
            await LoadDashboardAsync();
            await RefreshBellBadgeAsync();

            if (AuthorizationService.CanAccess(CurrentUser, AppModule.MaxBot))
            {
                _maxBotPoller.UnreadCountChanged += OnMaxBotUnreadChanged;
                await _maxBotPoller.RefreshUnreadCountAsync();

                if (!MaxBotOptions.UseExternalWorker(App.Configuration))
                    _maxBotPoller.Start();

                _maxBotBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _maxBotBadgeTimer.Tick += async (_, _) =>
                {
                    try
                    {
                        await _maxBotPoller.RefreshUnreadCountAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex);
                    }
                };
                _maxBotBadgeTimer.Start();
            }
        }

        private void OnMaxBotUnreadChanged(object? sender, int count) =>
            Dispatcher.Invoke(() => UpdateMaxBotMenuBadge(count));

        private void UpdateMaxBotMenuBadge(int count)
        {
            if (count <= 0)
            {
                MaxBotUnreadBadge.Visibility = Visibility.Collapsed;
                return;
            }

            MaxBotUnreadBadge.Visibility = Visibility.Visible;
            MaxBotUnreadBadgeText.Text = count > 99 ? "99+" : count.ToString(_ruCulture);
        }

        private void ConfigureUserButton()
        {
            var canManageUsers = AuthorizationService.CanManageUsers(CurrentUser);
            UserButton.Content = canManageUsers
                ? $"👤  {CurrentUser!.FullName}  ·  ⚙"
                : $"👤  {CurrentUser!.FullName}";
            UserButton.Cursor = System.Windows.Input.Cursors.Hand;
            UserButton.IsEnabled = true;
            UserButton.ToolTip = canManageUsers
                ? "Администрирование — учётные записи"
                : "Мой профиль";
        }

    private async void UserButton_Click(object sender, RoutedEventArgs e)
    {
        if (AuthorizationService.CanManageUsers(CurrentUser))
        {
            var admin = new Views.AdminWindow();
            SectionHelp.AttachTopic(admin, SectionHelp.AdminTitle, SectionHelp.AdminBody);
            admin.Owner = this;
            admin.ShowDialog();
            Activate();
        }
        else
        {
            var profile = new Views.ProfileWindow { Owner = this };
            profile.ShowDialog();
            Activate();
        }
    }

        private void ApplyModuleAccess()
        {
            foreach (var button in NavigationPanel.Children.OfType<Button>())
            {
                if (button.Tag is not string tag)
                    continue;
                var module = ResolveNavModule(tag);
                if (module == null)
                    continue;

                button.Visibility = AuthorizationService.CanAccess(CurrentUser, module.Value)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static AppModule? ResolveNavModule(string tag)
        {
            if (string.Equals(tag, "Courses", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "Directions", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "Schools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "Streets", StringComparison.OrdinalIgnoreCase))
                return AppModule.Dictionaries;

            return Enum.TryParse<AppModule>(tag, ignoreCase: true, out var module) ? module : null;
        }

        private void ApplyDashboardAccess()
        {
            ApplyHomePanels(showDashboard: false);
        }

        private void ApplyHomePanels(bool showDashboard)
        {
            var paymentsHome = AuthorizationService.CanAccess(CurrentUser, AppModule.Payments);
            var canSummary = AuthorizationService.CanViewManagementDashboard(CurrentUser);

            PaymentVedomost.Visibility = paymentsHome && !showDashboard
                ? Visibility.Visible
                : Visibility.Collapsed;

            var showKpi = canSummary && showDashboard;
            DashboardScroll.Visibility = showKpi || (!paymentsHome && !showKpi)
                ? Visibility.Visible
                : Visibility.Collapsed;
            DashboardPeriodBlock.Visibility = showKpi ? Visibility.Visible : Visibility.Collapsed;
            FinanceKpiGrid.Visibility = showKpi ? Visibility.Visible : Visibility.Collapsed;
            FinanceChartsGrid.Visibility = showKpi ? Visibility.Visible : Visibility.Collapsed;
            LeadAnalyticsGrid.Visibility = showKpi ? Visibility.Visible : Visibility.Collapsed;
            WelcomeCard.Visibility = showKpi ? Visibility.Visible : Visibility.Collapsed;

            OperationalHomePanel.Visibility = !paymentsHome && !showKpi
                ? Visibility.Visible
                : Visibility.Collapsed;

            HomeExcelExportButton.Visibility = AuthorizationService.CanAccess(CurrentUser, AppModule.Reports)
                ? Visibility.Visible
                : Visibility.Collapsed;

            WelcomeHintText.Text = "Сводка по продажам — только у директора и старшего менеджера (пункт «Сводка»).";
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            _sectionShell?.GoHome();
            ShowHomeChrome();
            ApplyHomePanels(showDashboard: false);
        }

        private void SummaryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthorizationService.CanViewManagementDashboard(CurrentUser))
                return;

            _sectionShell?.GoHome();
            PageTitleText.Text = "Сводка";
            ExitButton.Visibility = Visibility.Visible;
            SetActiveNav("Dashboard");
            ApplyHomePanels(showDashboard: true);
            _ = LoadDashboardAsync();
        }

        private void ShowHomeChrome()
        {
            PageTitleText.Text = "Ведомость";
            ExitButton.Visibility = Visibility.Visible;
            SetActiveNav("Home");
        }

        private void OnSessionIdleTimeout()
        {
            UserSession.SignOut();
            MessageBox.Show(
                "Сессия завершена из‑за неактивности (30 минут).\nВойдите снова.",
                "Безопасность",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var login = new Views.LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        private async Task RefreshBellBadgeAsync()
        {
            try
            {
                NotificationSchema.Ensure();
                var count = await _notificationsRepository.CountPendingNotificationsAsync(
                    _satisfactionRepository, _renewalRepository);
                SetBellBadge(count);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                SetBellBadge(0);
            }
        }

        private void SetBellBadge(int count)
        {
            if (count <= 0)
            {
                BellBadgeText.Text = string.Empty;
                BellBadgeText.Visibility = Visibility.Collapsed;
            }
            else
            {
                BellBadgeText.Visibility = Visibility.Visible;
                BellBadgeText.Text = count > 99 ? "99+" : count.ToString(_ruCulture);
            }
            BellButton.ToolTip = "Открыть уведомления";
        }

        private async Task LoadDashboardAsync()
        {
            if (_isDashboardLoading)
                return;

            if (!AuthorizationService.CanViewManagementDashboard(CurrentUser))
                return;

            if (DateFromPicker.SelectedDate is null || DateToPicker.SelectedDate is null)
                return;

            var periodStart = DateFromPicker.SelectedDate.Value.Date;
            var periodEnd = DateToPicker.SelectedDate.Value.Date;

            if (periodEnd < periodStart)
            {
                MessageBox.Show(
                    "Дата окончания периода должна быть не раньше даты начала.",
                    "Период задан неверно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                _isDashboardLoading = true;
                var snapshot = await _dashboardRepository.GetSnapshotAsync(periodStart, periodEnd);
                FillDashboard(snapshot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed),
                    "Ошибка загрузки сводки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isDashboardLoading = false;
            }
        }

        private void FillDashboard(DashboardSnapshot snapshot)
        {
            DashboardPeriodText.Text =
                $"Ключевые показатели за период {snapshot.PeriodStart:dd.MM.yyyy} — {snapshot.PeriodEnd:dd.MM.yyyy}";

            RevenueValueText.Text = snapshot.TotalRevenueText;
            RevenueDetailsText.Text = $"Договоры: {snapshot.ContractRevenueText} • продажи: {snapshot.SalesRevenueText}";
            RevenueBadgeText.Text = FormatComparison(snapshot.TotalRevenue, snapshot.PreviousTotalRevenue, "к прошлому периоду");

            ContractsValueText.Text = snapshot.ContractsCount.ToString(_ruCulture);
            ContractsBadgeText.Text = FormatComparison(snapshot.ContractsCount, snapshot.PreviousContractsCount, "к прошлому периоду");

            LeadSourceValueText.Text = $"{snapshot.TotalLeadsCount.ToString(_ruCulture)} заявок";
            LeadSourceDetailsText.Text = snapshot.TotalLeadsCount > 0
                ? $"лидер: {snapshot.TopLeadSourceName} ({snapshot.TopLeadSourceCount})"
                : "нет заявок за период";
            LeadSourceBadgeText.Text = snapshot.TotalLeadsCount > 0
                ? $"топ: {snapshot.TopLeadSourceName}"
                : "нет данных";
            LeadSourceChartSubtitleText.Text = snapshot.TotalLeadsCount > 0
                ? $"всего {snapshot.TotalLeadsCount} заявок за период"
                : "нет заявок за выбранный период";

            RiskPieCenterValueText.Text = snapshot.TotalLeadsCount.ToString(_ruCulture);
            RiskPieCenterSubtitleText.Text = snapshot.TotalLeadsCount == 1 ? "карточка гостя" : "карточек гостей";

            var directionCount = snapshot.DirectionRevenue.Count(item => item.HasValue);
            DirectionPieCenterValueText.Text = directionCount.ToString(_ruCulture);
            DirectionPieCenterSubtitleText.Text = directionCount == 1 ? "направление" : "направлений";

            RevenueTrendItemsControl.ItemsSource = snapshot.RevenueTrend;
            RevenueStructureItemsControl.ItemsSource = snapshot.RevenueStructure;
            RiskPieSlicesControl.ItemsSource = snapshot.LeadSourceDistribution;
            RiskDistributionItemsControl.ItemsSource = snapshot.LeadSourceDistribution;
            DirectionPieSlicesControl.ItemsSource = snapshot.DirectionRevenue;
            DirectionRevenueItemsControl.ItemsSource = snapshot.DirectionRevenue;
        }

        private string FormatComparison(decimal current, decimal previous, string suffix)
        {
            if (previous == 0 && current == 0)
                return "без изменений";

            if (previous == 0)
                return "+100% " + suffix;

            var percent = (current - previous) / previous * 100m;
            return $"{percent:+0;-0;0}% {suffix}";
        }

        private bool TryOpenModule(AppModule module, Func<Window> createWindow, string? navTag = null)
        {
            if (!AuthorizationService.CanAccess(CurrentUser, module))
            {
                MessageBox.Show(
                    "У вашей роли нет доступа к этому разделу.",
                    "Доступ запрещён",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var window = createWindow();
            SectionHelp.Attach(window, module);
            OpenSection(window, navTag ?? module.ToString());
            return true;
        }

        private void OpenSection(Window sectionWindow, string? navTag = null)
        {
            _sectionShell?.Show(sectionWindow);
            PageTitleText.Text = string.IsNullOrWhiteSpace(sectionWindow.Title)
                ? "Раздел"
                : sectionWindow.Title;
            ExitButton.Visibility = Visibility.Collapsed;
            SetActiveNav(navTag);
        }

        private void OnSectionReturnedHome()
        {
            ShowHomeChrome();
            ApplyHomePanels(showDashboard: false);
            _ = LoadDashboardAsync();
            _ = RefreshBellBadgeAsync();
            _ = PaymentVedomost.ReloadAsync();
        }

        private void SetActiveNav(string? moduleTag)
        {
            var active = (Style)FindResource("MenuButtonActiveStyle");
            var normal = (Style)FindResource("MenuButtonStyle");

            foreach (var button in NavigationPanel.Children.OfType<Button>())
            {
                var isActive = string.Equals(button.Tag as string, moduleTag, StringComparison.OrdinalIgnoreCase);
                button.Style = isActive ? active : normal;
            }
        }

        private async void PeriodPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPeriodInitializing || PeriodPresetCombo.SelectedItem is not ComboBoxItem item)
                return;

            try
            {
                var tag = item.Tag?.ToString() ?? "CurrentMonth";
                if (tag != "Custom")
                {
                    ApplyPeriodPreset(tag);
                    await LoadDashboardAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(UserFacingErrors.GetMessage(ex), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isPeriodInitializing || PeriodPresetCombo is null)
                return;

            PeriodPresetCombo.SelectedIndex = 4;
        }

        private async void ApplyPeriodButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UserFacingErrors.GetMessage(ex), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyPeriodPreset(string tag)
        {
            var today = DateTime.Today;
            DateTime from;
            DateTime to;

            switch (tag)
            {
                case "PreviousMonth":
                    var firstDayOfCurrentMonth = new DateTime(today.Year, today.Month, 1);
                    to = firstDayOfCurrentMonth.AddDays(-1);
                    from = new DateTime(to.Year, to.Month, 1);
                    break;

                case "Last30Days":
                    from = today.AddDays(-29);
                    to = today;
                    break;

                case "CurrentQuarter":
                    var quarterStartMonth = ((today.Month - 1) / 3) * 3 + 1;
                    from = new DateTime(today.Year, quarterStartMonth, 1);
                    to = today;
                    break;

                default:
                    from = new DateTime(today.Year, today.Month, 1);
                    to = today;
                    break;
            }

            _isPeriodInitializing = true;
            DateFromPicker.SelectedDate = from;
            DateToPicker.SelectedDate = to;
            _isPeriodInitializing = false;
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Help, () => new Views.HelpWindow());

        private void LeadsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Leads, () => new Views.LeadsWindow());

        private void TrialLessonsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.TrialLessons, () => new Views.TrialLessonsWindow());

        private void GroupsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Groups, () => new Views.GroupsWindow());

        private void TeachersButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Teachers, () => new Views.TeachersWindow());

        private void ContractsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Contracts, () => new Views.ContractsWindow());

        private void PaymentsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Payments, () => new Views.PaymentsWindow());

        private void DebtButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Debt, () => new Views.DebtWindow());

        private void ExpensesButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Expenses, () => new Views.ExpensesWindow());

        private void ReportsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Reports, () => new Views.ReportsWindow());

        private void DictionariesButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Dictionaries, () => new Views.DictionariesWindow());

        private void CoursesButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Dictionaries, () => new Views.DictionaryEditorWindow(DictionaryKind.CoursePackage), "Courses");

        private void DirectionsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Dictionaries, () => new Views.DictionaryEditorWindow(DictionaryKind.Direction), "Directions");

        private void SchoolsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Dictionaries, () => new Views.DictionaryEditorWindow(DictionaryKind.EducationalInstitution), "Schools");

        private void StreetsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.Dictionaries, () => new Views.DictionaryEditorWindow(DictionaryKind.Street), "Streets");

        private void MaxBotButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.MaxBot, () => new Views.MaxBotWindow());

        private void PlansKpiButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.PlansKpi, () => new Views.PlansKpiWindow());

        private void AiAnalyticsButton_Click(object sender, RoutedEventArgs e) =>
            TryOpenModule(AppModule.AiAnalytics, () => new Views.AiAnalyticsWindow());

        private async void BellButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthorizationService.CanAccess(CurrentUser, AppModule.Notifications))
                return;

            var win = new Views.NotificationsWindow();
            SectionHelp.Attach(win, AppModule.Notifications);
            OpenSection(win, "Notifications");
            await RefreshBellBadgeAsync();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowMinimize.Minimize(this);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Вы действительно хотите выйти из программы?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                UserSession.SignOut();
                Application.Current.Shutdown();
            }
        }
    }
}
