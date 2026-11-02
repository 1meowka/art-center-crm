#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VKR.Models;
using VKR.Services;

namespace VKR.Views;

public partial class ContractRenewalDialog : Window
{
    private readonly ContractRenewalRepository _repository = AppServices.GetRequiredService<ContractRenewalRepository>();
    private readonly ContractRenewalDueItem _item;

    public ContractRenewalDialog(ContractRenewalDueItem item)
    {
        InitializeComponent();
        _item = item;
        TitleText.Text = _item.StudentName;
        SubtitleText.Text =
            $"Договор {_item.ContractNumber} · {_item.DirectionName} · окончание {_item.EndDateText} (осталось {_item.DaysLeftText})";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool? willRenew = (DecisionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
            {
                "Да, продлит договор" => true,
                "Нет, не продлит" => false,
                _ => null
            };

            await _repository.SaveReminderAsync(new ContractRenewalInput
            {
                IdContract = _item.IdContract,
                IdStudent = _item.IdStudent,
                ContractEndDate = _item.ContractEndDate,
                WillRenew = willRenew,
                Comment = CommentTextBox.Text,
                IdCompletedBy = UserSession.CurrentUserId,
            });

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Не удалось сохранить: " + ex.Message;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
