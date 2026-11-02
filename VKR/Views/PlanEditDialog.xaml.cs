#nullable enable
using System;
using System.Windows;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class PlanEditDialog : Window
{
    public KpiPlan? Plan { get; private set; }

    public PlanEditDialog(KpiPlan? existingPlan = null)
    {
        InitializeComponent();
        
        if (existingPlan != null)
        {
            Title = "Редактировать план KPI";
            PlanNameInput.Text = existingPlan.PlanName;
            StartDateInput.SelectedDate = existingPlan.PeriodStart;
            EndDateInput.SelectedDate = existingPlan.PeriodEnd;
            PlannedLeadsInput.Text = existingPlan.PlannedLeads.ToString();
            PlannedStudentsInput.Text = existingPlan.PlannedNewStudents.ToString();
            PlannedContractsInput.Text = existingPlan.PlannedContracts.ToString();
            PlannedRevenueInput.Text = existingPlan.PlannedRevenue.ToString("N2");
            PlannedAttendanceInput.Text = existingPlan.PlannedAttendancePercent.ToString("N2");
            PlannedMaxChurnInput.Text = existingPlan.PlannedMaxChurn.ToString();
            
            Plan = existingPlan;
        }
        else
        {
            Title = "Добавить новый план KPI";
            StartDateInput.SelectedDate = DateTime.Now;
            EndDateInput.SelectedDate = DateTime.Now.AddMonths(1);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = "";

            if (!int.TryParse(PlannedLeadsInput.Text, out var leads) || leads < 0) leads = 0;
            if (!int.TryParse(PlannedStudentsInput.Text, out var students) || students < 0) students = 0;
            if (!int.TryParse(PlannedContractsInput.Text, out var contracts) || contracts < 0) contracts = 0;
            if (!decimal.TryParse(PlannedRevenueInput.Text, out var revenue) || revenue < 0) revenue = 0;
            if (!decimal.TryParse(PlannedAttendanceInput.Text, out var attendance) || attendance < 0 || attendance > 100) attendance = 0;
            if (!int.TryParse(PlannedMaxChurnInput.Text, out var churn) || churn < 0) churn = 0;

            var plan = new KpiPlan
            {
                IdKpiPlan = Plan?.IdKpiPlan ?? 0,
                PlanName = OptionalFieldDefaults.DictionaryNameOrDefault(PlanNameInput.Text),
                PeriodStart = StartDateInput.SelectedDate ?? DateTime.Today,
                PeriodEnd = EndDateInput.SelectedDate ?? DateTime.Today.AddMonths(1),
                PlannedLeads = leads,
                PlannedNewStudents = students,
                PlannedContracts = contracts,
                PlannedRevenue = revenue,
                PlannedAttendancePercent = attendance,
                PlannedMaxChurn = churn,
                CreatedAt = Plan?.CreatedAt ?? DateTime.Now
            };

            Plan = plan;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = $"❌ {message}";
    }
}
