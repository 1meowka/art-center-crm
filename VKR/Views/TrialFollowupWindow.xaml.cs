#nullable enable

using System.Collections.ObjectModel;

using System.Threading.Tasks;

using System.Windows;

using VKR.Models;

using VKR.Services;



namespace VKR.Views;



public partial class TrialFollowupWindow : Window

{

    private readonly TrialFollowupRepository _repository = new();

    private readonly TrialLessonRepository _trialRepository = AppServices.GetRequiredService<TrialLessonRepository>();

    private readonly ObservableCollection<TrialFollowupDueItem> _items = new();



    public TrialFollowupWindow()

    {

        InitializeComponent();

        DueGrid.ItemsSource = _items;

        Loaded += async (_, _) => await ReloadAsync();

    }



    private async Task ReloadAsync()

    {

        AcademicSchema.Ensure();

        var rows = await _repository.GetDueAsync();

        _items.Clear();

        foreach (var row in rows)

            _items.Add(row);

    }



    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();



    private async void Complete_Click(object sender, RoutedEventArgs e)

    {

        if (DueGrid.SelectedItem is not TrialFollowupDueItem item)

        {

            MessageBox.Show("Выберите строку.", "Задачи после пробного", MessageBoxButton.OK, MessageBoxImage.Information);

            return;

        }



        var results = new[]

        {

            "понравилось",

            "заключил_договор",

            "перенесено",

            "не_понравилось",

            "не_пришёл"

        };



        var dialog = new TrialFollowupCompleteDialog(item, results) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Result == null || dialog.SelectedTrialResult == null)

            return;



        dialog.Result.IdFollowup = item.IdFollowup;

        dialog.Result.IdCompletedBy = UserSession.CurrentUserId;



        if (dialog.Result.MarkCompleted || dialog.Result.OnControl)

            await _trialRepository.UpdateTrialResultAsync(item.IdTrial, dialog.SelectedTrialResult);



        await _repository.CompleteAsync(dialog.Result);

        await ReloadAsync();

    }



    private void Back_Click(object sender, RoutedEventArgs e) => Close();

}


