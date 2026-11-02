#nullable enable
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;

namespace VKR.Views;

public partial class TrialFollowupCompleteDialog : Window
{
    public TrialFollowupCompleteInput? Result { get; private set; }
    public string? SelectedTrialResult { get; private set; }

    public TrialFollowupCompleteDialog(TrialFollowupDueItem item, IReadOnlyList<string> trialResults)
    {
        InitializeComponent();
        SummaryText.Text =
            $"Клиент: {item.ClientFullName}\nРебёнок: {item.ChildFullName}\nПробное: {item.ScheduledText} · {item.DirectionName}";

        ContactStatusCombo.ItemsSource = new[]
        {
            "Связались — всё обсудили",
            "Связались ранее",
            "Не дозвонились — перезвонить",
            "Клиент перезвонит сам"
        };
        ContactStatusCombo.SelectedIndex = 0;

        ResultCombo.ItemsSource = trialResults;
        if (trialResults.Count > 0)
            ResultCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var contactStatus = ContactStatusCombo.SelectedItem as string ?? "Связались — всё обсудили";
        var trialResult = ResultCombo.SelectedItem as string
            ?? (ResultCombo.ItemsSource as System.Collections.IList)?[0] as string
            ?? string.Empty;

        var completeNow = contactStatus is "Связались — всё обсудили" or "Связались ранее" or "Клиент перезвонит сам";
        var reschedule = contactStatus == "Не дозвонились — перезвонить";
        var onControl = OnControlCheckBox.IsChecked == true;

        Result = new TrialFollowupCompleteInput
        {
            FeedbackCode = MapContactCode(contactStatus),
            FeedbackText = $"[{contactStatus}] {CommentBox.Text.Trim()}",
            OnControl = onControl,
            RescheduleNextDay = reschedule,
            MarkCompleted = completeNow && !onControl && !reschedule
        };
        SelectedTrialResult = trialResult;
        DialogResult = true;
        Close();
    }

    private static string MapContactCode(string status) => status switch
    {
        "Связались — всё обсудили" => "contacted_done",
        "Связались ранее" => "contacted_before",
        "Не дозвонились — перезвонить" => "no_answer",
        "Клиент перезвонит сам" => "client_will_call",
        _ => "contacted_done"
    };

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
