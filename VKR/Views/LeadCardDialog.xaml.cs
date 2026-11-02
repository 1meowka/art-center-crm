using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using VKR.Models;
using VKR.Resources;
using VKR.Services;

namespace VKR.Views
{
    public partial class LeadCardDialog : Window
    {
        private readonly Lead _lead;
        private readonly LeadRepository _repo = AppServices.GetRequiredService<LeadRepository>();

        public LeadCardAction Action { get; private set; } = LeadCardAction.None;

        public LeadCardDialog(Lead lead)
        {
            InitializeComponent();
            _lead = lead;
            Loaded += async (_, _) => await FillCardAsync();
        }

        private async System.Threading.Tasks.Task FillCardAsync()
        {
            var lead = await _repo.GetByIdAsync(_lead.IdLead) ?? _lead;
            lead.AllDirectionsText = _lead.AllDirectionsText;
            lead.DirectionName = string.IsNullOrWhiteSpace(lead.DirectionName) ? _lead.DirectionName : lead.DirectionName;

            LeadIdText.Text = $"КАРТОЧКА ГОСТЯ №{lead.IdLead} · {lead.GuestKindText}";
            ChildFullNameText.Text = string.IsNullOrWhiteSpace(lead.ChildFullName)
                                       ? lead.ChildFirstName : lead.ChildFullName;
            CreatedDateText.Text = lead.CreatedDate.ToString("dd.MM.yyyy HH:mm");
            AgeText.Text = lead.ChildAgeText;
            BirthDateText.Text = lead.ChildBirthDate?.ToString("dd.MM.yyyy") ?? "—";
            PhoneLabelText.Text = lead.IsAdultGuest ? "ТЕЛЕФОН" : "ТЕЛЕФОН ГОСТЯ";
            PhoneText.Text = string.IsNullOrWhiteSpace(lead.DisplayPhone) ? "—" : lead.DisplayPhone;
            PlaceLabelText.Text = lead.IsAdultGuest ? "МЕСТО РАБОТЫ" : "УЧЕБНОЕ УЧРЕЖДЕНИЕ";
            PlaceText.Text = lead.InstitutionOrWorkplaceText;
            ResidenceAddressText.Text = string.IsNullOrWhiteSpace(lead.ChildResidenceAddress)
                ? "—"
                : lead.ChildResidenceAddress;
            DirectionText.Text = string.IsNullOrWhiteSpace(lead.AllDirectionsText)
                ? (string.IsNullOrWhiteSpace(lead.DirectionName) ? "—" : lead.DirectionName)
                : lead.AllDirectionsText;
            SourceText.Text = lead.SourceName ?? _lead.SourceName ?? "—";
            ChildNoteText.Text = string.IsNullOrWhiteSpace(lead.Comment) ? "—" : lead.Comment;
            ContractNoteText.Text = string.IsNullOrWhiteSpace(lead.ContractNote) ? "—" : lead.ContractNote;

            var lines = new List<string>();
            void AddParent(string? rel, string? name, string? phone, short? age)
            {
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
                    return;
                var label = string.IsNullOrWhiteSpace(rel) ? "Родитель" : rel.Trim();
                var ageText = age.HasValue ? $" · {age} лет" : "";
                lines.Add($"{label}: {name ?? "—"} · {phone ?? "—"}{ageText}");
            }
            AddParent(lead.Parent1Relation, lead.Parent1FullName, lead.Parent1Phone, lead.Parent1Age);
            AddParent(lead.Parent2Relation, lead.Parent2FullName, lead.Parent2Phone, lead.Parent2Age);
            AddParent(lead.Parent3Relation, lead.Parent3FullName, lead.Parent3Phone, lead.Parent3Age);
            RelativesText.Text = lines.Count == 0 ? "—" : string.Join("\n", lines);

            StatusText.Text = lead.Status;
            (string bg, string fg) = lead.Status switch
            {
                "новая" => (ThemeColors.InfoBg, ThemeColors.InfoFg),
                "подтверждена" => (ThemeColors.WarningBg, ThemeColors.WarningFg),
                "посещено" => (ThemeColors.SelectedRow, ThemeColors.SelectedRowText),
                "конвертирована" => (ThemeColors.SuccessBg, ThemeColors.SuccessFg),
                "отказ" => (ThemeColors.DangerBg, ThemeColors.DangerFg),
                _ => (ThemeColors.NeutralBg, ThemeColors.NeutralFg),
            };
            StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Action = LeadCardAction.None;
            DialogResult = true;
            Close();
        }

        private void NotesButton_Click(object sender, RoutedEventArgs e)
        {
            var name = string.IsNullOrWhiteSpace(_lead.ChildFullName)
                ? _lead.ChildFirstName
                : _lead.ChildFullName;
            ClientNotesWindow.OpenForLead(this, _lead.IdLead, name);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            Action = LeadCardAction.Edit;
            DialogResult = true;
            Close();
        }
    }

    public enum LeadCardAction
    {
        None,
        Edit,
        Convert
    }
}
