#nullable enable
using System.Threading.Tasks;
using System.Windows;
using VKR.Services;

namespace VKR.Views;

public partial class AcademicHistoryWindow : Window
{
    private readonly AcademicHistoryRepository _repository = new();

    public AcademicHistoryWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        AcademicSchema.Ensure();
        TeacherHistoryGrid.ItemsSource = await _repository.GetTeacherHistoryAsync();
        TransferHistoryGrid.ItemsSource = await _repository.GetStudentTransferHistoryAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
