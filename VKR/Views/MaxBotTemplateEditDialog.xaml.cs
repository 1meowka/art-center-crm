#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using VKR.Models;
using VKR.Services;
using VKR.Utilities;

namespace VKR.Views;

public partial class MaxBotTemplateEditDialog : Window
{
    private readonly MaxBotRepository _repository = AppServices.GetRequiredService<MaxBotRepository>();
    private readonly int _idFolder;
    private readonly int? _idTemplate;

    public MaxBotTemplateEditDialog(int idFolder)
    {
        _idFolder = idFolder;
        InitializeComponent();
    }

    public MaxBotTemplateEditDialog(MaxBotTemplate existing)
    {
        _idFolder = existing.IdFolder;
        _idTemplate = existing.IdTemplate;
        InitializeComponent();
        TitleBox.Text = existing.Title;
        BodyBox.Text = existing.Body;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var title = TitleBox.Text?.Trim() ?? string.Empty;
            var body = BodyBox.Text?.Trim() ?? string.Empty;



            if (_idTemplate.HasValue)
            {
                await _repository.UpdateTemplateAsync(new MaxBotTemplate
                {
                    IdTemplate = _idTemplate.Value,
                    IdFolder = _idFolder,
                    Title = title,
                    Body = body
                });
            }
            else
            {
                await _repository.InsertTemplateAsync(new MaxBotTemplate
                {
                    IdFolder = _idFolder,
                    Title = title,
                    Body = body
                });
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(UserFacingErrors.GetMessage(ex));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
