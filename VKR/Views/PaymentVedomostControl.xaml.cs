#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class PaymentVedomostControl : UserControl
{
    private readonly PaymentRepository _payments = new();
    private readonly ObservableCollection<PaymentListItem> _items = new();
    private readonly SearchDebouncer _search = new(TimeSpan.FromMilliseconds(400));
    private bool _ready;

    public PaymentVedomostControl()
    {
        InitializeComponent();
        PaymentsGrid.ItemsSource = _items;
        _search.Debounced += async _ => await ReloadAsync();
        Loaded += PaymentVedomostControl_Loaded;
        Unloaded += (_, _) => _search.Dispose();
    }

    private async void PaymentVedomostControl_Loaded(object sender, RoutedEventArgs e)
    {
        var years = AcademicYearHelper.NearbyStarts()
            .Select(y => new LookupItem { Id = y, Name = AcademicYearHelper.Label(y) })
            .ToList();
        years.Insert(0, new LookupItem { Id = 0, Name = "Все годы" });
        YearCombo.ItemsSource = years;
        YearCombo.SelectedValue = AcademicYearHelper.CurrentStart;

        var months = new List<LookupItem> { new() { Id = 0, Name = "Все месяцы" } };
        for (var m = 1; m <= 12; m++)
            months.Add(new LookupItem { Id = m, Name = PaymentPeriodNames.Months[m - 1] });
        MonthCombo.ItemsSource = months;
        MonthCombo.SelectedValue = 0;

        var allBranch = new StudyBranch
        {
            IdBranch = 0,
            ShortName = "Все филиалы",
            AddressLine = "",
            ButtonLabel = "Все"
        };
        BranchCombo.ItemsSource = new[] { allBranch }.Concat(StudyBranch.All).ToList();
        BranchCombo.SelectedIndex = 0;

        _ready = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        try
        {
            DateTime? from = null;
            DateTime? to = null;
            var yearLabel = "все годы";
            if (YearCombo.SelectedValue is int y && y > 0)
            {
                (from, to) = AcademicYearHelper.DateRange(y);
                yearLabel = AcademicYearHelper.Label(y);
            }

            int? branchId = BranchCombo.SelectedItem is StudyBranch b && b.IdBranch > 0 ? b.IdBranch : null;
            int? month = MonthCombo.SelectedValue is int mo && mo > 0 ? mo : null;

            _items.Clear();
            foreach (var row in await _payments.GetPaymentsAsync(SearchBox?.Text, from, to, idBranch: branchId, periodMonth: month))
                _items.Add(row);

            CountText.Text = $"Всего строк: {_items.Count} · {yearLabel} · сортировка по дате";
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed),
                "Ведомость", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
            await ReloadAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;
        var text = SearchBox.Text ?? string.Empty;
        if (text.Length == 0 || text.Length >= 2)
            _search.Schedule();
    }

    private void ToggleEditor_Click(object sender, RoutedEventArgs e) => OpenPayment(null);

    private void PaymentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PaymentsGrid.SelectedItem is PaymentListItem item)
            OpenPayment(item.IdPayment);
    }

    private void OpenPayment(int? idPayment)
    {
        PaymentEditDialog.OpenFloating(
            Window.GetWindow(this),
            () => { _ = ReloadAsync(); },
            idPayment);
    }
}
