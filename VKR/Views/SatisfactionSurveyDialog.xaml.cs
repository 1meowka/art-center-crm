#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class SatisfactionSurveyDialog : Window
{
    private readonly StudentSatisfactionRepository _repository = AppServices.GetRequiredService<StudentSatisfactionRepository>();
    private readonly SatisfactionDueItem _item;

    public SatisfactionSurveyDialog(SatisfactionDueItem item)
    {
        InitializeComponent();
        _item = item;
        TitleText.Text = _item.StudentName;
        SubtitleText.Text =
            $"Договор {_item.ContractNumber} · {_item.DirectionName} · {_item.PeriodText} · срок опроса {_item.DueDateText}";
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var levels = await _repository.GetActiveLevelsAsync();
            LevelComboBox.ItemsSource = levels;

            var reasons = await _repository.GetActiveReasonsAsync();
            ReasonComboBox.ItemsSource = reasons;
            if (reasons.Count > 0)
                ReasonComboBox.SelectedIndex = 0;

            var alternates = await _repository.GetAlternateDirectionsAsync(_item.IdStudent, _item.IdDirection);
            AlternateDirectionComboBox.ItemsSource = alternates;
            if (alternates.Count > 0)
                AlternateDirectionComboBox.SelectedIndex = 0;
            else
            {
                AlternateDirectionComboBox.IsEnabled = false;
                AlternateDirectionComboBox.ToolTip = "В заявке нет других направлений для этого ребёнка";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void LevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelComboBox.SelectedItem is not SatisfactionLevelRef level)
        {
            NegativePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var showNegative = !level.IsPositive || level.Code is "dislike" or "mostly_no";
        NegativePanel.Visibility = showNegative ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var level = LevelComboBox.SelectedItem as SatisfactionLevelRef
            ?? (LevelComboBox.ItemsSource as System.Collections.Generic.IList<SatisfactionLevelRef>)?.FirstOrDefault();
        if (level == null)
            return;

        var needsNegative = !level.IsPositive || level.Code is "dislike" or "mostly_no";
        int? reasonId = null;
        int? suggestedDirId = null;

        if (needsNegative)
        {
            if (ReasonComboBox.SelectedItem is DissatisfactionReasonRef reason)
                reasonId = reason.Id;

            if (AlternateDirectionComboBox.SelectedItem is DirectionRef alt)
                suggestedDirId = alt.IdDirection;
        }

        try
        {
            await _repository.SaveSurveyAsync(new SatisfactionSurveyInput
            {
                IdStudent = _item.IdStudent,
                IdContract = _item.IdContract,
                IdDirection = _item.IdDirection,
                DueDate = _item.DueDate,
                PeriodNumber = _item.PeriodNumber,
                IdSatisfactionLevel = level.Id,
                IdDissatisfactionReason = reasonId,
                SuggestedDirectionId = suggestedDirId,
                Comment = CommentTextBox.Text,
                IdCompletedBy = UserSession.CurrentUserId,
            });

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось сохранить: " + ex.Message);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }
}
