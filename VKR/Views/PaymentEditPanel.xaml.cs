#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class PaymentEditPanel : UserControl
{
    private readonly PaymentRepository _repo = new();
    private readonly ContractRepository _contracts = new();
    private readonly GroupsRepository _groups = new();
    private readonly StudentRepository _students = new();

    private readonly UserRepository _users = new();
    private int? _idPayment;
    private DebtorItem? _debtor;
    private Payment? _existing;
    private int? _idStudent;
    private List<LookupItem> _contractItems = new();
    private List<LookupItem> _serviceTypes = new();
    private bool _filteringContracts;
    private bool _suppressTotals;

    public event EventHandler? Saved;
    public event EventHandler? Cancelled;

    public PaymentEditPanel()
    {
        InitializeComponent();
        TimesPerWeekCombo.ItemsSource = Enumerable.Range(1, 5).ToList();
        HasContractBox.Checked += HasContractBox_Changed;
        HasContractBox.Unchecked += HasContractBox_Changed;
        ContractCombo.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(ContractCombo_TextChanged));
    }

    public void BeginNew()
    {
        _idPayment = null;
        _debtor = null;
        _existing = null;
        _idStudent = null;
        _ = LoadAsync();
    }

    public void BeginEdit(int idPayment)
    {
        _idPayment = idPayment;
        _debtor = null;
        _ = LoadAsync();
    }

    public void BeginDebtor(DebtorItem debtor)
    {
        _idPayment = null;
        _debtor = debtor;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            var contracts = (await _repo.GetActiveContractsLookupAsync()).ToList();
            if (_debtor != null && contracts.All(c => c.Id != _debtor.IdContract))
            {
                contracts.Insert(0, new LookupItem
                {
                    Id = _debtor.IdContract,
                    Name = LookupComboHelper.ContractLabel(_debtor.IdContract.ToString(), _debtor.StudentFullName)
                });
            }

            var paymentMethods = await _repo.GetPaymentMethodsAsync();
            _serviceTypes = (await _repo.GetServiceTypesAsync()).ToList();
            var directions = await _groups.GetDirectionsAsync();
            var periods = await _repo.GetPeriodLabelsAsync();
            PeriodCombo.ItemsSource = periods;
            KindCombo.ItemsSource = PaymentKindNames.All.ToList();
            var managers = (await _users.GetUsersAsync(onlyActive: true))
                .Select(u => new LookupItem { Id = u.IdUser, Name = u.FullName })
                .ToList();
            LookupComboHelper.SetItems(ManagerCombo, managers, UserSession.CurrentUserId);

            LookupComboHelper.SetItems(DirectionCombo, directions);
            FillServices();
            if (TimesPerWeekCombo.SelectedItem == null && TimesPerWeekCombo.Items.Count > 0)
                TimesPerWeekCombo.SelectedIndex = 0;

            if (_idPayment.HasValue)
            {
                _existing = await _repo.GetByIdAsync(_idPayment.Value);
                if (_existing == null)
                {
                    ShowError("Оплата не найдена.");
                    return;
                }

                PaymentDatePicker.SelectedDate = _existing.PaymentDate;
                ActualDatePicker.SelectedDate = _existing.ActualDate ?? _existing.PaymentDate;
                KindCombo.Text = string.IsNullOrWhiteSpace(_existing.PaymentKind)
                    ? (_existing.IdContract is > 0 ? PaymentKindNames.Contract : PaymentKindNames.OneOff)
                    : _existing.PaymentKind;
                HasContractBox.IsChecked = _existing.IdContract is > 0
                    || PaymentKindNames.UsesContract(KindCombo.Text);
                _suppressTotals = true;
                AmountBox.Text = (_existing.AmountBase ?? _existing.Amount).ToString("0.##", CultureInfo.InvariantCulture);
                DiscountBox.Text = _existing.DiscountPercent is > 0
                    ? _existing.DiscountPercent.Value.ToString("0.##", CultureInfo.InvariantCulture)
                    : string.Empty;
                _suppressTotals = false;
                RecalcTotal();
                BindContracts(contracts, _existing.IdContract);
                LookupComboHelper.SetItems(PaymentMethodCombo, paymentMethods, _existing.IdPaymentMethod);
                CashReceiptBox.IsChecked = _existing.CashReceiptIssued;
                CommentBox.Text = _existing.Comment ?? string.Empty;
                EventBox.Text = _existing.EventText ?? string.Empty;
                PeriodCombo.Text = _existing.PeriodLabel ?? string.Empty;
                if (_existing.IdManager is > 0)
                    LookupComboHelper.SelectById(ManagerCombo, _existing.IdManager);
                _idStudent = _existing.IdStudent;
                SelectService(_existing.IdServiceType);
                await ApplyContractAsync(_existing.IdContract);
                if (_idStudent is > 0 && string.IsNullOrWhiteSpace(ClientBox.Text))
                    await FillClientNameAsync(_idStudent.Value);
            }
            else
            {
                PaymentDatePicker.SelectedDate = DateTime.Today;
                ActualDatePicker.SelectedDate = DateTime.Today;
                KindCombo.Text = PaymentKindNames.Contract;
                HasContractBox.IsChecked = true;
                BindContracts(contracts, _debtor?.IdContract);
                LookupComboHelper.SetItems(PaymentMethodCombo, paymentMethods);
                LookupComboHelper.SelectFirstIfNone(PaymentMethodCombo);
                AmountBox.Text = string.Empty;
                DiscountBox.Text = string.Empty;
                TotalBox.Text = string.Empty;
                CommentBox.Text = string.Empty;
                EventBox.Text = string.Empty;
                PeriodCombo.Text = string.Empty;
                ClientBox.Text = string.Empty;
                _idStudent = null;
                CashReceiptBox.IsChecked = false;

                if (_debtor != null)
                {
                    HasContractBox.IsChecked = true;
                    HasContractBox.IsEnabled = false;
                    ContractCombo.IsEnabled = false;
                    AmountBox.Text = _debtor.Outstanding.ToString("0.##", CultureInfo.InvariantCulture);
                    CommentBox.Text = $"Погашение задолженности по договору {_debtor.ContractNumber}";
                    ClientBox.Text = _debtor.StudentFullName;
                    _idStudent = _debtor.IdStudent;
                    await ApplyContractAsync(_debtor.IdContract);
                }
                else
                {
                    HasContractBox.IsEnabled = true;
                    ContractCombo.IsEnabled = true;
                }

                RecalcTotal();
            }

            ApplyContractVisibility();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.LoadFailed));
        }
    }

    private void FillServices()
    {
        var names = OneOffPaymentServices.Names.ToList();
        foreach (var item in _serviceTypes)
        {
            if (!string.IsNullOrWhiteSpace(item.Name)
                && names.All(n => !n.Equals(item.Name, StringComparison.CurrentCultureIgnoreCase)))
                names.Add(item.Name);
        }

        ServiceCombo.ItemsSource = names;
        if (ServiceCombo.SelectedItem == null && names.Count > 0)
            ServiceCombo.SelectedIndex = 0;
    }

    private void SelectService(int idServiceType)
    {
        var match = _serviceTypes.FirstOrDefault(s => s.Id == idServiceType);
        if (match != null)
            ServiceCombo.Text = match.Name;
        else if (ServiceCombo.Items.Count > 0)
            ServiceCombo.SelectedIndex = 0;
    }

    private void BindContracts(IReadOnlyList<LookupItem> items, int? selectedId)
    {
        _filteringContracts = true;
        _contractItems = items.ToList();
        LookupComboHelper.SetItems(ContractCombo, _contractItems, selectedId);
        _filteringContracts = false;
    }

    private static bool MatchesContract(LookupItem item, string query)
    {
        if (item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            return true;
        if (item.Id.ToString().Contains(query, StringComparison.Ordinal))
            return true;
        var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1
            && parts.All(part => item.Name.Contains(part, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool TrySyncSelectedContractText()
    {
        if (ContractCombo.SelectedItem is not LookupItem selected)
            return false;

        var text = ContractCombo.Text ?? string.Empty;
        if (string.Equals(selected.Name, text, StringComparison.CurrentCultureIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(text))
        {
            _filteringContracts = true;
            ContractCombo.Text = selected.Name;
            _filteringContracts = false;
            return true;
        }

        return false;
    }

    private void ContractCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_filteringContracts || _contractItems.Count == 0)
            return;

        if (TrySyncSelectedContractText())
        {
            ClearError();
            return;
        }

        var query = (ContractCombo.Text ?? string.Empty).Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _contractItems
            : _contractItems.Where(item => MatchesContract(item, query)).ToList();

        var typed = ContractCombo.Text;
        var keepId = LookupComboHelper.GetSelectedId(ContractCombo);
        _filteringContracts = true;
        ContractCombo.ItemsSource = filtered;
        ContractCombo.DisplayMemberPath = "Name";
        ContractCombo.SelectedValuePath = "Id";
        if (keepId is > 0 && filtered.Any(item => item.Id == keepId))
            LookupComboHelper.SelectById(ContractCombo, keepId);
        else
            ContractCombo.Text = typed;
        _filteringContracts = false;

        if (filtered.Count == 0 && !string.IsNullOrEmpty(query))
            ShowError("Договор не найден по ФИО. Нажмите «Найти» у клиента или снимите «Наличие договора».");
        else
            ClearError();

        if (filtered.Count > 0 && !string.IsNullOrEmpty(query))
            ContractCombo.IsDropDownOpen = true;
    }

    private async Task ApplyContractAsync(int? idContract)
    {
        if (idContract is not > 0)
            return;

        var contract = await _contracts.GetByIdAsync(idContract.Value);
        if (contract == null)
            return;

        _idStudent = contract.IdStudent;
        if (string.IsNullOrWhiteSpace(ClientBox.Text))
            await FillClientNameAsync(contract.IdStudent);

        if (contract.IdDirection is > 0)
            DirectionCombo.SelectedValue = contract.IdDirection.Value;
        if (contract.TimesPerWeek > 0)
            TimesPerWeekCombo.SelectedItem = (int)contract.TimesPerWeek;
    }

    private async Task FillClientNameAsync(int idStudent)
    {
        var all = await _students.GetStudentsAsync(null);
        var student = all.FirstOrDefault(s => s.IdStudent == idStudent);
        ClientBox.Text = student?.FullName ?? $"Клиент #{idStudent}";
    }

    private async void FindClient_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var picker = new StudentPickerDialog(_students) { Owner = owner };
        if (picker.ShowDialog() != true || picker.SelectedStudent == null)
            return;

        _idStudent = picker.SelectedStudent.IdStudent;
        ClientBox.Text = picker.SelectedStudent.FullName;

        var contracts = (await _contracts.GetContractsAsync())
            .Where(c => c.IdStudent == _idStudent)
            .Select(c => new LookupItem
            {
                Id = c.IdContract,
                Name = LookupComboHelper.ContractLabel(c.IdContract.ToString(), c.StudentFullName)
            })
            .ToList();
        BindContracts(contracts, contracts.FirstOrDefault()?.Id);
        if (contracts.Count == 0)
        {
            HasContractBox.IsChecked = false;
            ClearError();
        }
        else
        {
            HasContractBox.IsChecked = true;
            ClearError();
            await ApplyContractAsync(LookupComboHelper.GetSelectedId(ContractCombo));
        }

        ApplyContractVisibility();
    }

    private async void ContractCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filteringContracts)
            return;
        TrySyncSelectedContractText();
        await ApplyContractAsync(LookupComboHelper.GetSelectedId(ContractCombo));
    }

    private void ContractCombo_DropDownClosed(object sender, EventArgs e) => TrySyncSelectedContractText();

    private void HasContractBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        ApplyContractVisibility();
    }

    private void ApplyContractVisibility()
    {
        var has = HasContractBox.IsChecked == true;
        var vis = has ? Visibility.Visible : Visibility.Collapsed;
        ContractFields.Visibility = vis;
        DirectionFields.Visibility = vis;
        TimesFields.Visibility = vis;
        ServiceFields.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        EventFields.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    private void KindCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        var kind = KindCombo.Text ?? KindCombo.SelectedItem as string;
        if (PaymentKindNames.UsesContract(kind))
            HasContractBox.IsChecked = true;
        else
            HasContractBox.IsChecked = false;
        ApplyContractVisibility();
    }

    private void Totals_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_suppressTotals)
            RecalcTotal();
    }

    private void RecalcTotal()
    {
        if (!TryParseDecimal(AmountBox.Text, out var amount) || amount < 0)
        {
            TotalBox.Text = string.Empty;
            return;
        }

        var discount = 0m;
        if (!string.IsNullOrWhiteSpace(DiscountBox.Text)
            && (!TryParseDecimal(DiscountBox.Text, out discount) || discount < 0 || discount > 100))
        {
            TotalBox.Text = string.Empty;
            return;
        }

        var total = Math.Round(amount * (1 - discount / 100m), 2, MidpointRounding.AwayFromZero);
        TotalBox.Text = total.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        return decimal.TryParse((text ?? string.Empty).Replace(',', '.').Trim(),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearError();
            RecalcTotal();

            if (!TryParseDecimal(AmountBox.Text, out var baseAmount) || baseAmount <= 0)
            {
                ShowError("Укажите сумму оплаты.");
                return;
            }

            var discount = 0m;
            if (!string.IsNullOrWhiteSpace(DiscountBox.Text))
            {
                if (!TryParseDecimal(DiscountBox.Text, out discount) || discount < 0 || discount > 100)
                {
                    ShowError("Скидка — число от 0 до 100 %.");
                    return;
                }
            }

            if (!TryParseDecimal(TotalBox.Text, out var total) || total <= 0)
            {
                ShowError("Итоговая сумма должна быть больше нуля.");
                return;
            }

            if (_debtor != null && total > _debtor.Outstanding)
            {
                ShowError($"Сумма не может превышать остаток задолженности ({_debtor.OutstandingText}).");
                return;
            }

            if (_idStudent is not > 0)
            {
                ShowError("Выберите клиента через «Найти».");
                return;
            }

            var hasContract = HasContractBox.IsChecked == true;
            int? idContract = null;
            if (hasContract)
            {
                idContract = LookupComboHelper.GetSelectedId(ContractCombo);
                if (idContract is not > 0)
                {
                    ShowError("Выберите договор или снимите «Наличие договора».");
                    return;
                }
            }

            var methodName = (PaymentMethodCombo.Text ?? string.Empty).Trim();
            if (PaymentMethodCombo.SelectedItem is LookupItem selectedMethod)
                methodName = selectedMethod.Name;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                ShowError("Укажите тип оплаты (нал, терминал, QR-код, перевод — или свой вариант).");
                return;
            }

            var methodId = await _repo.GetOrCreatePaymentMethodAsync(methodName);

            var serviceName = hasContract
                ? (_serviceTypes.FirstOrDefault()?.Name ?? "Обучение")
                : (ServiceCombo.Text ?? string.Empty).Trim();
            if (!hasContract && string.IsNullOrWhiteSpace(serviceName) && string.IsNullOrWhiteSpace(EventBox.Text))
            {
                ShowError("Укажите услугу или текст события.");
                return;
            }

            var serviceId = ResolveServiceTypeId(serviceName);

            var payment = _existing ?? new Payment();
            payment.PaymentDate = PaymentDatePicker.SelectedDate ?? DateTime.Today;
            payment.ActualDate = ActualDatePicker.SelectedDate ?? payment.PaymentDate;
            if (payment.ActualDate > DateTime.Today.AddYears(1) || payment.PaymentDate > DateTime.Today.AddYears(1))
            {
                ShowError("Проверьте дату — она выглядит некорректной.");
                return;
            }

            payment.AmountBase = baseAmount;
            payment.DiscountPercent = discount > 0 ? discount : null;
            payment.Amount = total;
            payment.IdContract = idContract;
            payment.IdStudent = _idStudent;
            payment.IdPaymentMethod = methodId;
            payment.IdServiceType = serviceId;
            payment.CashReceiptIssued = CashReceiptBox.IsChecked == true;
            payment.PaymentKind = string.IsNullOrWhiteSpace(KindCombo.Text)
                ? (hasContract ? PaymentKindNames.Contract : PaymentKindNames.OneOff)
                : KindCombo.Text.Trim();
            payment.EventText = string.IsNullOrWhiteSpace(EventBox.Text) ? null : EventBox.Text.Trim();
            payment.PeriodLabel = string.IsNullOrWhiteSpace(PeriodCombo.Text) ? null : PeriodCombo.Text.Trim();
            payment.PeriodMonth = PaymentPeriodNames.TryMonthNumber(payment.PeriodLabel);
            payment.PeriodYear = payment.PeriodMonth is > 0 ? (short)payment.PaymentDate.Year : null;
            payment.IdManager = LookupComboHelper.GetSelectedId(ManagerCombo);
            payment.Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();

            if (idContract is > 0)
                await TryUpdateContractDirectionAndTimesAsync(idContract.Value);

            if (_idPayment.HasValue)
            {
                await _repo.UpdateAsync(payment);
            }
            else if (_debtor != null)
            {
                payment.IdCreatedBy = UserSession.CurrentUserId;
                await _contracts.RepayDebtAsync(payment, _debtor.IdDebt);
            }
            else
            {
                payment.IdCreatedBy = UserSession.CurrentUserId;
                await _repo.CreateAsync(payment);
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, UserFacingErrors.SaveFailed));
        }
    }

    private int ResolveServiceTypeId(string serviceName)
    {
        var match = _serviceTypes.FirstOrDefault(s =>
            s.Name.Equals(serviceName, StringComparison.CurrentCultureIgnoreCase));
        if (match != null)
            return match.Id;
        return _serviceTypes.FirstOrDefault()?.Id ?? 1;
    }

    private async Task TryUpdateContractDirectionAndTimesAsync(int idContract)
    {
        var contract = await _contracts.GetByIdAsync(idContract);
        if (contract == null)
            return;

        var idDirection = LookupComboHelper.GetSelectedId(DirectionCombo);
        contract.IdDirection = idDirection is > 0 ? idDirection : contract.IdDirection;
        if (DirectionCombo.SelectedItem is LookupItem dir)
            contract.DirectionNameSnapshot = dir.Name;
        if (TimesPerWeekCombo.SelectedItem is int times && times > 0)
            contract.TimesPerWeek = (short)times;
        await _contracts.UpdateAsync(contract);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);

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
