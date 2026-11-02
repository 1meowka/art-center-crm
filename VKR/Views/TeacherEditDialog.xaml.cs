#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class TeacherEditDialog : Window
{
    private readonly TeachersRepository _repository;
    private readonly int? _idTeacher;

    public TeacherEditDialog(TeachersRepository repository, int? idTeacher = null)
    {
        InitializeComponent();
        _repository = repository;
        _idTeacher = idTeacher;
        HireDateBox.Text = DateTime.Today.ToString("dd.MM.yyyy");
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            HideError();

            // Загружаем направления
            var directions = await _repository.GetDirectionsAsync();
            IReadOnlyList<int> teacherDirIds = _idTeacher.HasValue
                ? await _repository.GetTeacherDirectionIdsAsync(_idTeacher.Value)
                : Array.Empty<int>();

            DirectionsPanel.Children.Clear();
            foreach (var dir in directions)
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = dir.Name,
                    Tag = dir.Id,
                    IsChecked = teacherDirIds.Contains(dir.Id),
                    Foreground = VKR.Resources.ThemeColors.ToBrush(VKR.Resources.ThemeColors.Text),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                DirectionsPanel.Children.Add(cb);
            }

            if (_idTeacher.HasValue)
            {
                DialogTitleText.Text = "Редактирование преподавателя";
                DeleteButton.Visibility = Visibility.Visible;

                var all = await _repository.GetTeachersAsync();
                var current = all.FirstOrDefault(x => x.IdTeacher == _idTeacher.Value);
                if (current != null)
                {
                    LastNameBox.Text = current.LastName;
                    FirstNameBox.Text = current.FirstName;
                    MiddleNameBox.Text = current.MiddleName ?? string.Empty;
                    PhoneBox.Text = current.Phone;
                    EmailBox.Text = current.Email ?? string.Empty;
                    HireDateBox.Text = current.HireDate.ToString("dd.MM.yyyy");
                    RateBox.Text = current.BaseLessonRate?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
                    IsActiveCheck.IsChecked = current.IsActive;
                    DialogTitleText.Text = $"Редактирование: {current.FullName}";
                }
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideError();

            var phone = OptionalFieldDefaults.PhoneOrNull(PhoneBox.Text) ?? OptionalFieldDefaults.EmptyPhone;

            DateTime hire = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(HireDateBox.Text) && !HireDateBox.Text.Contains('_')
                && DateTime.TryParseExact(HireDateBox.Text.Trim(), "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedHire))
                hire = parsedHire;

            decimal? rate = null;
            if (!string.IsNullOrWhiteSpace(RateBox.Text))
            {
                var rateText = RateBox.Text.Trim().Replace(',', '.');
                if (decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) && r >= 0)
                    rate = r;
            }

            var model = new TeacherEditModel
            {
                IdTeacher = _idTeacher ?? 0,
                LastName = OptionalFieldDefaults.NameOrDefault(LastNameBox.Text),
                FirstName = OptionalFieldDefaults.NameOrDefault(FirstNameBox.Text),
                MiddleName = string.IsNullOrWhiteSpace(MiddleNameBox.Text) ? null : MiddleNameBox.Text.Trim(),
                Phone = phone,
                Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim(),
                HireDate = hire,
                BaseLessonRate = rate,
                IsActive = IsActiveCheck.IsChecked == true
            };

            var savedId = await _repository.SaveTeacherAsync(model);

            // Сохраняем выбранные направления
            var selectedDirs = new List<int>();
            foreach (var child in DirectionsPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true && cb.Tag is int dirId)
                    selectedDirs.Add(dirId);
            }
            await _repository.SaveTeacherDirectionsAsync(savedId, selectedDirs);

            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!_idTeacher.HasValue) return;
        var r = MessageBox.Show("Удалить преподавателя? Если у него есть группы — удаление может не пройти.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            await _repository.DeleteTeacherAsync(_idTeacher.Value);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

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
