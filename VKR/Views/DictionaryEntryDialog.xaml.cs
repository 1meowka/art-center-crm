#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class DictionaryEntryDialog : Window
{
    private readonly DictionariesRepository _repository;
    private readonly DictionaryKind _kind;
    private readonly DictionaryEntry _entry;

    // Ссылки на динамические поля
    private TextBox? _priceBox;
    private TextBox? _descriptionBox;
    private CheckBox? _isActiveCheck;
    private TextBox? _capacityBox;
    private TextBox? _codeBox;
    private TextBox? _stockBox;
    private ComboBox? _typeCombo;
    private ComboBox? _branchCombo;
    private ComboBox? _parentDirectionCombo;
    private ComboBox? _courseDirectionCombo;
    private CheckBox? _isPositiveCheck;
    private TextBox? _sortOrderBox;
    private TextBox? _durationMonthsBox;
    private TextBox? _numberBox;
    private TextBox? _addressBox;

    public bool Saved { get; private set; }
    public int SavedId { get; private set; }

    public DictionaryEntryDialog(DictionariesRepository repository, DictionaryKind kind, DictionaryEntry? existing)
    {
        InitializeComponent();
        _repository = repository;
        _kind = kind;
        _entry = existing ?? new DictionaryEntry();

        if (_kind == DictionaryKind.Direction)
        {
            DialogTitleText.Text = _entry.Id > 0 ? "Редактирование направления" : "Новое направление";
            Title = DialogTitleText.Text;
            NameLabel.Text = "НАЗВАНИЕ НАПРАВЛЕНИЯ";
            NameBox.ToolTip = "Например: ИЗО, Музыка, Лепка";
        }
        else if (_entry.Id > 0)
            DialogTitleText.Text = "Редактирование записи";

        if (_entry.Id > 0)
            DeleteButton.Visibility = Visibility.Visible;

        NameBox.Text = _entry.Name;
        BuildExtraFields();
        if (_kind == DictionaryKind.Direction)
            Loaded += async (_, _) => await PopulateParentDirectionsAsync();
        if (_kind == DictionaryKind.CoursePackage)
            Loaded += async (_, _) => await PopulateCourseDirectionsAsync();
    }

    private async Task PopulateCourseDirectionsAsync(int? selectId = null)
    {
        if (_courseDirectionCombo == null) return;
        var directions = await _repository.GetEntriesAsync(DictionaryKind.Direction);
        _courseDirectionCombo.Items.Clear();
        _courseDirectionCombo.Items.Add(new LookupItem { Id = 0, Name = "— для всех направлений —" });
        foreach (var item in directions)
            _courseDirectionCombo.Items.Add(new LookupItem { Id = item.Id, Name = item.Name });

        var dirId = selectId is > 0
            ? selectId.Value
            : _entry.Extra.TryGetValue("id_direction", out var dir) && dir is int di && di > 0 ? di : 0;
        _courseDirectionCombo.SelectedValue = dirId;
        if (_courseDirectionCombo.SelectedItem == null && _courseDirectionCombo.Items.Count > 0)
            _courseDirectionCombo.SelectedIndex = 0;
    }

    private Button CreateAddDirectionButton()
    {
        var button = new Button
        {
            Content = "➕  Добавить направление",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(0, -6, 0, 14),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#EEF2FF")!,
            Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#3730A3")!,
            BorderThickness = new Thickness(0)
        };
        button.Click += AddDirection_Click;
        return button;
    }

    private async void AddDirection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DictionaryEntryDialog(_repository, DictionaryKind.Direction, null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Saved && dialog.SavedId > 0)
        {
            _entry.Extra["id_direction"] = dialog.SavedId;
            await PopulateCourseDirectionsAsync(dialog.SavedId);
        }
    }

    private async Task PopulateParentDirectionsAsync()
    {
        if (_parentDirectionCombo == null) return;
        try
        {
            var roots = await _repository.GetRootDirectionsAsync(_entry.Id > 0 ? _entry.Id : null);
            _parentDirectionCombo.Items.Clear();
            _parentDirectionCombo.Items.Add(new LookupItem { Id = 0, Name = "— корневое направление —" });
            foreach (var item in roots)
                _parentDirectionCombo.Items.Add(item);

            var parentId = _entry.Extra.TryGetValue("parent_id_direction", out var pid) && pid is int pi && pi > 0
                ? pi
                : 0;
            _parentDirectionCombo.SelectedValue = parentId;
            if (_parentDirectionCombo.SelectedItem == null && _parentDirectionCombo.Items.Count > 0)
                _parentDirectionCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex, "Не удалось загрузить список направлений."));
        }
    }

    private void BuildExtraFields()
    {
        ExtraFieldsPanel.Children.Clear();

        switch (_kind)
        {
            case DictionaryKind.Direction:
                _parentDirectionCombo = AddLookupComboField("РОДИТЕЛЬСКОЕ НАПРАВЛЕНИЕ", 0);
                _priceBox = AddField("ЦЕНА ЗА ЗАНЯТИЕ (₽)",
                    _entry.Extra.TryGetValue("price", out var p) && p != null
                        ? Convert.ToDecimal(p).ToString("0.##", CultureInfo.InvariantCulture) : "");
                _descriptionBox = AddField("ОПИСАНИЕ",
                    _entry.Extra.TryGetValue("description", out var d) ? d as string ?? "" : "");
                _isActiveCheck = AddCheckBox("направление активно",
                    _entry.IsActive ?? true);
                _sortOrderBox = AddField("ПОРЯДОК СОРТИРОВКИ",
                    _entry.Extra.TryGetValue("sort_order", out var so) && so != null
                        ? Convert.ToInt32(so).ToString() : "0");
                break;

            case DictionaryKind.Classroom:
                _capacityBox = AddField("ВМЕСТИМОСТЬ",
                    _entry.Extra.TryGetValue("capacity", out var c) && c != null
                        ? Convert.ToInt32(c).ToString() : "");
                _descriptionBox = AddField("ОПИСАНИЕ",
                    _entry.Extra.TryGetValue("description", out var cd) ? cd as string ?? "" : "");
                var currentBranchId = _entry.Extra.TryGetValue("id_branch", out var br) && br != null
                    ? Convert.ToInt32(br)
                    : StudyBranch.Central.IdBranch;
                _branchCombo = AddBranchComboField("ФИЛИАЛ", currentBranchId);
                break;

            case DictionaryKind.EducationalInstitution:
            case DictionaryKind.School:
            case DictionaryKind.Kindergarten:
                {
                    var defaultType = "школа";
                    var currentType = _entry.Extra.TryGetValue("institution_type", out var t)
                        ? t as string ?? defaultType : defaultType;
                    var types = new[] { "школа", "гимназия", "лицей", "детский сад" };
                    _typeCombo = AddComboField("ТИП УЧРЕЖДЕНИЯ", types, currentType);
                    _numberBox = AddField("НОМЕР",
                        _entry.Extra.TryGetValue("institution_number", out var num) ? num as string ?? "" : "");
                    break;
                }

            case DictionaryKind.StudyBranch:
                _addressBox = AddField("АДРЕС",
                    _entry.Extra.TryGetValue("address_line", out var addr) ? addr as string ?? "" : "");
                _descriptionBox = AddField("ПОДПИСЬ НА КНОПКЕ",
                    _entry.Extra.TryGetValue("button_label", out var btn) ? btn as string ?? "" : "");
                _isActiveCheck = AddCheckBox("филиал используется", _entry.IsActive ?? true);
                break;

            case DictionaryKind.AcademicYear:
                _codeBox = AddField("ГОД НАЧАЛА (например 2025)",
                    _entry.Extra.TryGetValue("start_year", out var sy) && sy != null
                        ? Convert.ToInt32(sy).ToString() : AcademicYearHelper.CurrentStart.ToString());
                _isActiveCheck = AddCheckBox("год активен", _entry.IsActive ?? true);
                break;

            case DictionaryKind.StudentType:
                _codeBox = AddField("НОМЕР (1, 2, 3…)",
                    _entry.Extra.TryGetValue("type_code", out var tc) ? tc as string ?? "" : "");
                break;

            case DictionaryKind.PaymentMethod:
                _codeBox = AddField("КОД СПОСОБА",
                    _entry.Extra.TryGetValue("method_code", out var mc) ? mc as string ?? "" : "");
                break;

            case DictionaryKind.ServiceType:
                _codeBox = AddField("КОД УСЛУГИ",
                    _entry.Extra.TryGetValue("service_code", out var sc) ? sc as string ?? "" : "");
                break;

            case DictionaryKind.ShopProduct:
                _priceBox = AddField("ЦЕНА (₽)",
                    _entry.Extra.TryGetValue("price", out var pp) && pp != null
                        ? Convert.ToDecimal(pp).ToString("0.##", CultureInfo.InvariantCulture) : "");
                _stockBox = AddField("ОСТАТОК НА СКЛАДЕ",
                    _entry.Extra.TryGetValue("stock_qty", out var sq) && sq != null
                        ? Convert.ToInt32(sq).ToString() : "");
                _isActiveCheck = AddCheckBox("товар активен", _entry.IsActive ?? true);
                break;

            case DictionaryKind.CoursePackage:
                _courseDirectionCombo = AddLookupComboField("НАПРАВЛЕНИЕ", 0);
                ExtraFieldsPanel.Children.Add(CreateAddDirectionButton());
                _durationMonthsBox = AddField("ДЛИТЕЛЬНОСТЬ (МЕС., можно 1.5)",
                    _entry.Extra.TryGetValue("duration_months", out var dm) && dm != null
                        ? Convert.ToDecimal(dm).ToString("0.##", CultureInfo.InvariantCulture) : "9");
                _capacityBox = AddField("ЗАНЯТИЙ",
                    _entry.Extra.TryGetValue("lessons_count", out var lessonsCount) && lessonsCount != null
                        ? Convert.ToInt32(lessonsCount).ToString() : "");
                _sortOrderBox = AddField("РАЗ В НЕДЕЛЮ",
                    _entry.Extra.TryGetValue("times_per_week", out var tw) && tw != null
                        ? Convert.ToInt32(tw).ToString() : "1");
                _isActiveCheck = AddCheckBox("курс активен", _entry.IsActive ?? true);
                break;

            case DictionaryKind.Role:
                _codeBox = AddField("КОД РОЛИ",
                    _entry.Extra.TryGetValue("role_code", out var rc) ? rc as string ?? "" : "");
                if (_entry.Id > 0)
                    _codeBox.IsReadOnly = true;
                _descriptionBox = AddField("ОПИСАНИЕ",
                    _entry.Extra.TryGetValue("description", out var rd) ? rd as string ?? "" : "");
                break;

            case DictionaryKind.RelationType:
            case DictionaryKind.SourceOfArrival:
            case DictionaryKind.ExpenseType:
            case DictionaryKind.EventType:
                break;

            case DictionaryKind.SatisfactionLevel:
                _codeBox = AddField("КОД ОЦЕНКИ",
                    _entry.Extra.TryGetValue("level_code", out var lc) ? lc as string ?? "" : "");
                _isPositiveCheck = AddCheckBox("положительная оценка (нравится)",
                    _entry.Extra.TryGetValue("is_positive", out var pos) && pos is bool pb && pb);
                _sortOrderBox = AddField("ПОРЯДОК СОРТИРОВКИ",
                    _entry.Extra.TryGetValue("sort_order", out var slo) && slo != null
                        ? Convert.ToInt32(slo).ToString() : "0");
                _isActiveCheck = AddCheckBox("запись активна", _entry.IsActive ?? true);
                break;

            case DictionaryKind.DissatisfactionReason:
                _sortOrderBox = AddField("ПОРЯДОК СОРТИРОВКИ",
                    _entry.Extra.TryGetValue("sort_order", out var dro) && dro != null
                        ? Convert.ToInt32(dro).ToString() : "0");
                _isActiveCheck = AddCheckBox("запись активна", _entry.IsActive ?? true);
                break;
        }
    }

    private TextBox AddField(string label, string value)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#94A3B8")!,
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(lbl);

        var border = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            CornerRadius = new CornerRadius(12),
            BorderBrush = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            Height = 42
        };
        var textBox = new TextBox
        {
            Padding = new Thickness(14, 0, 14, 0),
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 13,
            Background = System.Windows.Media.Brushes.Transparent,
            Text = value
        };
        border.Child = textBox;
        stack.Children.Add(border);

        ExtraFieldsPanel.Children.Add(stack);
        return textBox;
    }

    private ComboBox AddComboField(string label, string[] options, string selectedValue)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#94A3B8")!,
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(lbl);

        var comboBox = new ComboBox
        {
            Height = 42,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        foreach (var option in options)
            comboBox.Items.Add(option);
        comboBox.SelectedItem = selectedValue;
        if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;

        stack.Children.Add(comboBox);
        ExtraFieldsPanel.Children.Add(stack);
        return comboBox;
    }

    private ComboBox AddLookupComboField(string label, int selectedId)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#94A3B8")!,
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(lbl);

        var comboBox = new ComboBox
        {
            Height = 42,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id"
        };
        comboBox.SelectedValue = selectedId;

        stack.Children.Add(comboBox);
        ExtraFieldsPanel.Children.Add(stack);
        return comboBox;
    }

    private ComboBox AddBranchComboField(string label, int selectedBranchId)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.Black,
            Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#94A3B8")!,
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(lbl);

        var comboBox = new ComboBox
        {
            Height = 42,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            DisplayMemberPath = "ButtonLabel",
            SelectedValuePath = "IdBranch"
        };
        foreach (var branch in StudyBranch.All)
            comboBox.Items.Add(branch);

        comboBox.SelectedValue = selectedBranchId;
        if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;

        stack.Children.Add(comboBox);
        ExtraFieldsPanel.Children.Add(stack);
        return comboBox;
    }

    private CheckBox AddCheckBox(string label, bool isChecked)
    {
        var border = new Border
        {
            Background = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#F8FAFC")!,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            BorderBrush = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0")!,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
            Foreground = VKR.Resources.ThemeColors.ToBrush(VKR.Resources.ThemeColors.Text),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        border.Child = cb;
        ExtraFieldsPanel.Children.Add(border);
        return cb;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var entry = new DictionaryEntry
            {
                Id = _entry.Id,
                Name = OptionalFieldDefaults.DictionaryNameOrDefault(NameBox.Text),
                Extra = new Dictionary<string, object?>()
            };

            switch (_kind)
            {
                case DictionaryKind.Direction:
                    decimal price = 1;
                    if (!string.IsNullOrWhiteSpace(_priceBox?.Text))
                    {
                        var pt = _priceBox!.Text.Trim().Replace(',', '.');
                        if (decimal.TryParse(pt, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPrice) && parsedPrice > 0)
                            price = parsedPrice;
                    }
                    entry.Extra["price"] = price;
                    entry.Extra["description"] = string.IsNullOrWhiteSpace(_descriptionBox?.Text) ? null : _descriptionBox!.Text.Trim();
                    entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                    var selectedParent = _parentDirectionCombo?.SelectedValue is int sp && sp > 0 ? sp : (int?)null;
                    entry.Extra["parent_id_direction"] = selectedParent;
                    if (!short.TryParse(_sortOrderBox?.Text?.Trim(), out var directionSortOrder))
                        directionSortOrder = 0;
                    entry.Extra["sort_order"] = directionSortOrder;
                    break;

                case DictionaryKind.Classroom:
                    int.TryParse(_capacityBox?.Text?.Trim(), out var capacity);
                    if (capacity <= 0) capacity = 20;
                    entry.Extra["capacity"] = capacity;
                    entry.Extra["description"] = string.IsNullOrWhiteSpace(_descriptionBox?.Text) ? null : _descriptionBox!.Text.Trim();
                    entry.Extra["id_branch"] = _branchCombo?.SelectedValue is int branchId
                        ? branchId
                        : StudyBranch.Central.IdBranch;
                    break;

                case DictionaryKind.EducationalInstitution:
                case DictionaryKind.School:
                case DictionaryKind.Kindergarten:
                    entry.Extra["institution_type"] = _typeCombo?.SelectedItem as string
                        ?? (_kind == DictionaryKind.Kindergarten ? "детский сад" : "школа");
                    entry.Extra["institution_number"] = string.IsNullOrWhiteSpace(_numberBox?.Text)
                        ? null : _numberBox!.Text.Trim();
                    break;

                case DictionaryKind.StudyBranch:
                    entry.Extra["address_line"] = string.IsNullOrWhiteSpace(_addressBox?.Text)
                        ? entry.Name : _addressBox!.Text.Trim();
                    entry.Extra["button_label"] = string.IsNullOrWhiteSpace(_descriptionBox?.Text)
                        ? entry.Name : _descriptionBox!.Text.Trim();
                    entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                    break;

                case DictionaryKind.AcademicYear:
                    if (!int.TryParse(_codeBox?.Text?.Trim(), out var startYear) || startYear < 2000)
                        startYear = AcademicYearHelper.CurrentStart;
                    entry.Extra["start_year"] = startYear;
                    if (string.IsNullOrWhiteSpace(entry.Name))
                        entry.Name = AcademicYearHelper.Label(startYear);
                    entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                    break;

                case DictionaryKind.StudentType:
                    {
                        entry.Extra["type_code"] = string.IsNullOrWhiteSpace(_codeBox?.Text)
                            ? entry.Name.Trim().ToLowerInvariant()
                            : _codeBox!.Text.Trim();
                        break;
                    }

                case DictionaryKind.PaymentMethod:
                case DictionaryKind.ServiceType:
                case DictionaryKind.Role:
                    {
                        var codeKey = _kind switch
                        {
                            DictionaryKind.PaymentMethod => "method_code",
                            DictionaryKind.ServiceType => "service_code",
                            _ => "role_code"
                        };
                        entry.Extra[codeKey] = string.IsNullOrWhiteSpace(_codeBox?.Text)
                            ? entry.Name.ToLowerInvariant()
                            : _codeBox!.Text.Trim();
                        if (_kind == DictionaryKind.Role)
                            entry.Extra["description"] = string.IsNullOrWhiteSpace(_descriptionBox?.Text) ? null : _descriptionBox!.Text.Trim();
                        break;
                    }

                case DictionaryKind.ShopProduct:
                    {
                        decimal productPrice = 0;
                        if (!string.IsNullOrWhiteSpace(_priceBox?.Text))
                        {
                            var pt = _priceBox!.Text.Trim().Replace(',', '.');
                            decimal.TryParse(pt, NumberStyles.Number, CultureInfo.InvariantCulture, out productPrice);
                        }
                        int.TryParse(_stockBox?.Text?.Trim(), out var stock);
                        if (stock < 0) stock = 0;
                        entry.Extra["price"] = productPrice;
                        entry.Extra["stock_qty"] = stock;
                        entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                        break;
                    }

                case DictionaryKind.CoursePackage:
                    {
                        decimal duration = 9;
                        decimal.TryParse(_durationMonthsBox?.Text?.Trim().Replace(',', '.'),
                            NumberStyles.Number, CultureInfo.InvariantCulture, out duration);
                        if (duration <= 0) duration = 9;
                        int? selectedDir = _courseDirectionCombo?.SelectedValue is int dir && dir > 0 ? dir : null;
                        int? lessons = null;
                        if (int.TryParse(_capacityBox?.Text?.Trim(), out var parsedLessons) && parsedLessons >= 0)
                            lessons = parsedLessons;
                        short timesPerWeek = 1;
                        if (short.TryParse(_sortOrderBox?.Text?.Trim(), out var parsedTpw) && parsedTpw >= 1)
                            timesPerWeek = parsedTpw;
                        entry.Extra["duration_months"] = duration;
                        entry.Extra["lessons_count"] = lessons;
                        entry.Extra["times_per_week"] = timesPerWeek;
                        entry.Extra["id_direction"] = selectedDir;
                        entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                        break;
                    }

                case DictionaryKind.SatisfactionLevel:
                    {
                        entry.Extra["level_code"] = string.IsNullOrWhiteSpace(_codeBox?.Text)
                            ? entry.Name.ToLowerInvariant()
                            : _codeBox!.Text.Trim();
                        if (!short.TryParse(_sortOrderBox?.Text?.Trim(), out var sortOrder))
                            sortOrder = 0;
                        entry.Extra["level_code"] = string.IsNullOrWhiteSpace(_codeBox?.Text)
                            ? entry.Name.ToLowerInvariant()
                            : _codeBox!.Text.Trim();
                        entry.Extra["is_positive"] = _isPositiveCheck?.IsChecked ?? false;
                        entry.Extra["sort_order"] = sortOrder;
                        entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                        break;
                    }

                case DictionaryKind.DissatisfactionReason:
                    {
                        if (!short.TryParse(_sortOrderBox?.Text?.Trim(), out var sortOrder))
                            sortOrder = 0;
                        entry.Extra["sort_order"] = sortOrder;
                        entry.IsActive = _isActiveCheck?.IsChecked ?? true;
                        break;
                    }
            }

            SavedId = await _repository.SaveEntryAsync(_kind, entry);
            Finish(saved: true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_entry.Id <= 0) return;
        var r = MessageBox.Show(
            "Удалить запись? Если она используется в других таблицах — удаление может не пройти.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            await _repository.DeleteEntryAsync(_kind, _entry.Id);
            Finish(saved: true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(saved: false);

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Finish(bool saved)
    {
        Saved = saved;
        try
        {
            DialogResult = saved;
        }
        catch (InvalidOperationException)
        {
            Close();
            return;
        }
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }
}
