#nullable enable

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class InstitutionGeographyWindow : Window
{
    private readonly InstitutionGeographyRepository _repository = new();
    private readonly ObservableCollection<InstitutionGeographyRow> _rows = new();

    public InstitutionGeographyWindow()
    {
        InitializeComponent();
        RowsGrid.ItemsSource = _rows;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var summary = await _repository.GetSummaryAsync();
            PreschoolCountText.Text = summary.PreschoolCount.ToString("N0");
            SchoolCountText.Text = summary.SchoolCount.ToString("N0");
            TotalCountText.Text = summary.TotalCount.ToString("N0");

            _rows.Clear();
            foreach (var row in await _repository.GetRowsAsync())
                _rows.Add(row);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(UserFacingErrors.GetMessage(ex), "География обучающихся",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
