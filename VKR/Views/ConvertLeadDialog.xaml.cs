using System;

using System.Collections.Generic;

using System.Globalization;

using System.Linq;

using System.Text.RegularExpressions;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using VKR.Models;

using VKR.Services;

using VKR.Utilities;



namespace VKR.Views

{

    public partial class ConvertLeadDialog : Window

    {

        private readonly LeadRepository _repo = AppServices.GetRequiredService<LeadRepository>();

        private Lead _lead;

        private List<LeadEnrolleeCandidate> _allCandidates = new();

        private LeadEnrolleeCandidate? _selectedCandidate;



        public Student? Result { get; private set; }



        public ConvertLeadDialog(Lead lead)

        {

            InitializeComponent();

            _lead = lead;

            Loaded += async (_, _) => await InitializeAsync();

        }



        private async Task InitializeAsync()

        {

            var fresh = await _repo.GetByIdAsync(_lead.IdLead);

            if (fresh != null)

                _lead = fresh;



            var relations = await _repo.GetRelationsAsync(_lead.IdLead);

            var children = await _repo.GetChildrenAsync(_lead.IdLead);

            var additionalReps = await _repo.GetAdditionalRepresentativesAsync(_lead.IdLead);



            _allCandidates = LeadEnrolleeHelper.BuildCandidates(_lead, relations, children, additionalReps);

            ClientPayerText.Text = LeadEnrolleeHelper.FormatClientPayerLine(_lead, relations);



            StudentTypeComboBox.ItemsSource = await _repo.GetStudentTypesAsync();

            StudentTypeComboBox.SelectedItem = (StudentTypeComboBox.ItemsSource as List<StudentTypeRef>)

                ?.FirstOrDefault(x => x.IdStudentType == 1);



            ApplyCandidateFilter();

            var defaultCandidate = LeadEnrolleeHelper.PickDefaultStudent(_allCandidates);

            if (defaultCandidate != null)

            {

                CandidatesGrid.SelectedItem = _allCandidates.FirstOrDefault(c =>

                    string.Equals(c.FullName, defaultCandidate.FullName, StringComparison.OrdinalIgnoreCase));

            }



            ArrivalDateMaskedBox.Text = DateTime.Today.ToString("dd.MM.yyyy");

        }



        private void ApplyCandidateFilter()

        {

            var query = (CandidateSearchBox?.Text ?? string.Empty).Trim().ToLowerInvariant();

            IEnumerable<LeadEnrolleeCandidate> filtered = _allCandidates;

            if (!string.IsNullOrWhiteSpace(query))

                filtered = _allCandidates.Where(c => c.SearchText.Contains(query));



            var list = filtered.ToList();

            CandidatesGrid.ItemsSource = list;

            if (_selectedCandidate != null)

            {

                var match = list.FirstOrDefault(c =>

                    string.Equals(c.FullName, _selectedCandidate.FullName, StringComparison.OrdinalIgnoreCase));

                if (match != null)

                    CandidatesGrid.SelectedItem = match;

            }

            else if (list.Count > 0 && CandidatesGrid.SelectedItem == null)

            {

                CandidatesGrid.SelectedIndex = 0;

            }

        }



        private void CandidateSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>

            ApplyCandidateFilter();



        private void CandidatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (CandidatesGrid.SelectedItem is not LeadEnrolleeCandidate candidate)

                return;



            _selectedCandidate = candidate;

            ApplyCandidateToForm(candidate);

        }



        private void ApplyCandidateToForm(LeadEnrolleeCandidate candidate)

        {

            LastNameTextBox.Text = candidate.LastName;

            FirstNameTextBox.Text = candidate.FirstName;

            MiddleNameTextBox.Text = candidate.MiddleName ?? string.Empty;



            if (candidate.BirthDate.HasValue)

                BirthDateMaskedBox.Text = candidate.BirthDate.Value.ToString("dd.MM.yyyy");

            else

                BirthDateMaskedBox.Text = string.Empty;



            if (!string.IsNullOrWhiteSpace(candidate.Phone))

                PhoneMaskedBox.Text = candidate.Phone;

            else

                PhoneMaskedBox.Text = string.Empty;



            var prefilled = !string.IsNullOrWhiteSpace(candidate.LastName) && !string.IsNullOrWhiteSpace(candidate.FirstName);

            LastNameBorder.Style = (Style)FindResource(prefilled ? "PrefilledBorderStyle" : "InputBorderStyle");

            FirstNameBorder.Style = (Style)FindResource(prefilled ? "PrefilledBorderStyle" : "InputBorderStyle");

            BirthDateBorder.Style = (Style)FindResource(candidate.BirthDate.HasValue ? "PrefilledBorderStyle" : "InputBorderStyle");

            PhoneBorder.Style = (Style)FindResource(string.IsNullOrWhiteSpace(candidate.Phone) ? "InputBorderStyle" : "PrefilledBorderStyle");

        }



        private async void ConvertButton_Click(object sender, RoutedEventArgs e)

        {

            HideError();

            var convertButton = sender as Button;

            DateTime? birthDate = null;
            string birthRaw = BirthDateMaskedBox.Text ?? "";
            if (!birthRaw.Contains("_") && !string.IsNullOrWhiteSpace(birthRaw)
                && DateTime.TryParseExact(birthRaw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedBirth))
                birthDate = parsedBirth;

            DateTime arrivalDate = DateTime.Today;
            string arrivalRaw = ArrivalDateMaskedBox.Text ?? "";
            if (!arrivalRaw.Contains("_") && !string.IsNullOrWhiteSpace(arrivalRaw)
                && DateTime.TryParseExact(arrivalRaw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedArrival))
                arrivalDate = parsedArrival;

            var studentTypeId = StudentTypeComboBox.SelectedItem is StudentTypeRef studentType
                ? studentType.IdStudentType
                : OptionalFieldDefaults.StudentTypeOrDefault(0);

            string? phone = OptionalFieldDefaults.PhoneOrNull(PhoneMaskedBox.Text);

            string status = (StatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "активный";



            var residence = _selectedCandidate?.ResidenceAddress

                            ?? _lead.ChildResidenceAddress

                            ?? _lead.RepResidenceAddress;



            var student = new Student

            {

                LastName = OptionalFieldDefaults.NameOrDefault(LastNameTextBox.Text),

                FirstName = OptionalFieldDefaults.NameOrDefault(FirstNameTextBox.Text),

                MiddleName = string.IsNullOrWhiteSpace(MiddleNameTextBox.Text) ? null : MiddleNameTextBox.Text.Trim(),

                BirthDate = OptionalFieldDefaults.BirthDateOrDefault(birthDate),

                Phone = phone,

                ResidenceAddress = string.IsNullOrWhiteSpace(residence) ? null : residence.Trim(),

                Status = status,

                IdStudentType = studentTypeId,

                ArrivalDate = arrivalDate,

                IdInstitution = _selectedCandidate?.IdInstitution ?? _lead.IdInstitution,

                Comment = string.IsNullOrWhiteSpace(_lead.Comment) ? null : _lead.Comment.Trim()

            };



            var payerRepId = _lead.IdRepresentative;

            var payerRelationId = _lead.RepRelationTypeId;



            if (_selectedCandidate?.IsClient == true && _selectedCandidate.IdRepresentative is > 0)

            {

                payerRepId = _selectedCandidate.IdRepresentative;

                payerRelationId = _selectedCandidate.RelationTypeId ?? payerRelationId;

            }



            try

            {

                if (convertButton != null)

                    convertButton.IsEnabled = false;

                await _repo.ConvertLeadAsync(

                    _lead.IdLead,

                    student,

                    payerRepId,

                    payerRelationId);

                Result = student;

                DialogResult = true;

                Close();

            }

            catch (Exception ex)

            {

                ShowError(UserFacingErrors.GetMessage(ex, "Не удалось зачислить обучающегося."));

            }

            finally

            {

                if (convertButton != null)

                    convertButton.IsEnabled = true;

            }

        }



        private void HideError()

        {

            ErrorText.Text = string.Empty;

            ErrorBorder.Visibility = Visibility.Collapsed;

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

}


