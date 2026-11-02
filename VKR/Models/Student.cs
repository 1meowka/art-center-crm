#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VKR.Models;

public sealed class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class Student
{
    public int IdStudent { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public DateTime BirthDate { get; set; } = DateTime.Today.AddYears(-7);
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ResidenceAddress { get; set; }
    public string? BirthCertificate { get; set; }
    public string Status { get; set; } = "активный";
    public int? IdRepresentative { get; set; }
    public int? IdInstitution { get; set; }
    public string? SchoolNumber { get; set; }
    public short? SchoolShift { get; set; }
    public int IdStudentType { get; set; }
    public DateTime ArrivalDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; }
    public string? Comment { get; set; }

    public string FullName => FormatFio(LastName, FirstName, MiddleName);

    public static string FormatFio(string? lastName, string? firstName, string? middleName)
    {
        return string.Join(" ", new[] { lastName, firstName, middleName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}

public sealed class StudentListItem
{
    public int IdStudent { get; set; }
    public int? IdLead { get; set; }
    public bool IsTrialProspect => IdLead.HasValue && IdLead.Value > 0 && IdStudent <= 0;
    public string FullName { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public int Age { get; set; }
    public string AgeText => Age <= 0 ? "—" : $"{Age}";
    public bool IsMinor => Age < 18;
    public string MinorText => IsMinor ? "несовершеннолетний" : "18+";
    public string Phone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StudentType { get; set; } = string.Empty;
    public string GroupDisplay { get; set; } = "—";
    public decimal DebtAmount { get; set; }
    public string DebtText => DebtAmount <= 0 ? "нет" : $"{DebtAmount:N0} ₽";
    public decimal? AttendancePercent { get; set; }
    public string AttendanceText => AttendancePercent.HasValue ? $"{AttendancePercent.Value:P0}" : "—";
    public int RepresentativeCount { get; set; }
    public string Representatives { get; set; } = "—";
    public string MainPayer { get; set; } = "—";
    public string RepresentativeState => RepresentativeCount == 0 ? "нет" : $"{RepresentativeCount}";
    public DateTime ArrivalDate { get; set; }
    public string ArrivalDateText => ArrivalDate == default ? "—" : ArrivalDate.ToString("dd.MM.yyyy");
    public string ArrivalPeriodText { get; set; } = "—";
    public string BirthDateText => BirthDate == default ? "—" : BirthDate.ToString("dd.MM.yyyy");
    public string ResidenceAddress { get; set; } = "—";
    public string Comment { get; set; } = "";
    public string SchoolNumber { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string MotherText { get; set; } = "—";
    public string FatherText { get; set; } = "—";
    public string InstitutionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstitutionName) && string.IsNullOrWhiteSpace(SchoolNumber))
                return "—";
            if (string.IsNullOrWhiteSpace(SchoolNumber))
                return InstitutionName;
            if (string.IsNullOrWhiteSpace(InstitutionName))
                return $"№ {SchoolNumber}";
            return $"{InstitutionName} № {SchoolNumber}";
        }
    }
    public string CommentText => string.IsNullOrWhiteSpace(Comment) ? "—" : Comment;
    public string AddressText => string.IsNullOrWhiteSpace(ResidenceAddress) ? "—" : ResidenceAddress;
}

public sealed class StudentDetails
{
    public Student Student { get; set; } = new();
    public string StudentTypeName { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = "—";
    public int Age { get; set; }
    public bool IsMinor => Age < 18;
    public ObservableCollection<StudentRepresentativeLink> Representatives { get; } = new();
    public ObservableCollection<StudentContractInfo> Contracts { get; } = new();
    public ObservableCollection<ContactHistoryItem> Contacts { get; } = new();
    public ObservableCollection<StudentGroupMembershipItem> Groups { get; } = new();
}

// Строка для вкладки «Группы» в карточке обучающегося.
// Период берём из договора (start_date — end_date), как просит заказчик;
// если у membership нет договора, используем даты самой записи в group_membership.
public sealed class StudentGroupMembershipItem
{
    public int IdMembership { get; set; }
    public int IdGroup { get; set; }
    public int IdDirection { get; set; }
    public string GroupDisplay { get; set; } = string.Empty;
    public string DirectionName { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;

    public DateTime EnrollmentDate { get; set; }
    public DateTime? ExpulsionDate { get; set; }

    // Период по договору (приоритетно показываем именно его)
    public DateTime? ContractStartDate { get; set; }
    public DateTime? ContractEndDate { get; set; }
    public string ContractNumber { get; set; } = string.Empty;

    public bool IsActive => ExpulsionDate == null;
    public string StatusText => IsActive ? "активный" : "завершён";

    // Что показываем в колонке «Период обучения по договору»
    public string PeriodText
    {
        get
        {
            // Если по группе нашёлся договор — используем его даты (заказчик просил «по договору»)
            if (ContractStartDate.HasValue && ContractEndDate.HasValue)
                return $"{ContractStartDate.Value:dd.MM.yyyy} — {ContractEndDate.Value:dd.MM.yyyy}";

            // Иначе fallback на даты зачисления/отчисления, чтобы не было пустого места
            var from = EnrollmentDate.ToString("dd.MM.yyyy");
            var to = ExpulsionDate?.ToString("dd.MM.yyyy") ?? "по наст. время";
            return $"{from} — {to}";
        }
    }

    public string ContractNumberText => string.IsNullOrWhiteSpace(ContractNumber) ? "—" : ContractNumber;
}

// Вариант группы для выбора в диалоге перевода.
public sealed class GroupTransferOption
{
    public int IdGroup { get; set; }
    public int IdDirection { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public string GroupDisplay { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public int MembersCount { get; set; }
    public short? AgeMin { get; set; }
    public short? AgeMax { get; set; }
    public string? AgeBandLabel { get; set; }

    public string AgeRangeText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AgeBandLabel))
                return AgeBandLabel!;
            if (AgeMin.HasValue && AgeMax.HasValue)
                return $"{AgeMin}–{AgeMax} лет";
            if (AgeMin.HasValue)
                return $"от {AgeMin} лет";
            if (AgeMax.HasValue)
                return $"до {AgeMax} лет";
            return "возраст не указан";
        }
    }

    // Отображение в ComboBox — должно быть осмысленным и узнаваемым.
    public string DisplayName => $"{GroupDisplay} · {AgeRangeText} · преп. {TeacherName} · в группе: {MembersCount}";
    public override string ToString() => DisplayName;
}

public sealed class StudentEditModel
{
    public int IdStudent { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public DateTime BirthDate { get; set; } = DateTime.Today.AddYears(-7);
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ResidenceAddress { get; set; }
    public string? BirthCertificate { get; set; }
    public string Status { get; set; } = "активный";
    public int? IdInstitution { get; set; }
    public string? SchoolNumber { get; set; }
    public short? SchoolShift { get; set; }
    public int IdStudentType { get; set; }
    public DateTime ArrivalDate { get; set; } = DateTime.Today;
    public string? Comment { get; set; }
}

public sealed class Representative
{
    public int IdRepresentative { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ResidenceAddress { get; set; }
    public int? Age { get; set; }
    public string? Workplace { get; set; }
    public bool IsMainPayerLegacy { get; set; }
    public int? IdRelationTypeLegacy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Comment { get; set; }

    public string FullName => Student.FormatFio(LastName, FirstName, MiddleName);
}

public sealed class RepresentativeListItem
{
    public int IdRepresentative { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmailText => string.IsNullOrWhiteSpace(Email) ? "—" : Email;
    public int? Age { get; set; }
    public string AgeText => Age.HasValue ? Age.Value.ToString() : "—";
    public string Workplace { get; set; } = "—";
    public int ChildrenCount { get; set; }
    public int PayerCount { get; set; }
    public int LegalCount { get; set; }
    public string ChildrenText => ChildrenCount == 0 ? "нет связей" : $"{ChildrenCount}";
    public string PayerBadge => PayerCount > 0 ? $"✓ ({PayerCount})" : "—";
    public string LegalBadge => LegalCount > 0 ? $"✓ ({LegalCount})" : "—";
    public string ChildrenNames { get; set; } = "—";
    public string MainPayerFor { get; set; } = "—";
}

public sealed class RepresentativeDetails
{
    public Representative Representative { get; set; } = new();
    public ObservableCollection<RepresentativeChildItem> Children { get; } = new();
}

public sealed class RepresentativeChildItem
{
    public int IdStudent { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public DateTime BirthDate { get; set; }
    public int Age { get; set; }
    public string RelationName { get; set; } = string.Empty;
    public bool IsMainPayer { get; set; }
    public bool IsLegalRepresentative { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MainPayerText => IsMainPayer ? "основной плательщик" : "не плательщик";
    public string LegalText => IsLegalRepresentative ? "законный представитель" : "контактное лицо";
    public string RepresentativeRoleText => string.IsNullOrWhiteSpace(RelationName)
        ? "представитель ребёнка"
        : $"{RelationName.ToLowerInvariant()} ребёнка";
}

public sealed class RepresentativeEditModel
{
    public int IdRepresentative { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ResidenceAddress { get; set; }
    public int? Age { get; set; }
    public string? Workplace { get; set; }
    public string? Comment { get; set; }
    public int? FallbackRelationTypeId { get; set; }
}

public sealed class StudentRepresentativeLink : INotifyPropertyChanged
{
    private bool _isMainPayer;
    private bool _isLegalRepresentative = true;

    public int IdStudentRepresentative { get; set; }
    public int IdStudent { get; set; }
    public int IdRepresentative { get; set; }
    public string RepresentativeFullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ResidenceAddress { get; set; }
    public int IdRelationType { get; set; }
    public string RelationName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string AgeText => Age is > 0 ? Age.Value.ToString() : "—";

    public bool IsMainPayer
    {
        get => _isMainPayer;
        set
        {
            if (_isMainPayer == value) return;
            _isMainPayer = value;
            OnChanged();
            OnChanged(nameof(MainPayerText));
        }
    }

    public bool IsLegalRepresentative
    {
        get => _isLegalRepresentative;
        set
        {
            if (_isLegalRepresentative == value) return;
            _isLegalRepresentative = value;
            OnChanged();
            OnChanged(nameof(LegalText));
        }
    }

    public string? Note { get; set; }
    public string MainPayerText => IsMainPayer ? "Да" : "Нет";
    public string LegalText => IsLegalRepresentative ? "Да" : "Нет";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class StudentRepresentativeEditModel
{
    public int IdStudent { get; set; }
    public int IdRepresentative { get; set; }
    public int IdRelationType { get; set; }
    public bool IsMainPayer { get; set; }
    public bool IsLegalRepresentative { get; set; } = true;
    public string? Note { get; set; }
}

public sealed class StudentContractInfo
{
    public int IdContract { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime ContractDate { get; set; }
    public string DirectionName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime EndDate { get; set; }
    public string AmountText => $"{TotalAmount:N0} ₽";
}

public sealed class ContactHistoryItem
{
    public int IdContact { get; set; }
    public DateTime ContactDate { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public DateTime? NextContactAt { get; set; }
    public string? Comment { get; set; }
    public string NextContactText => NextContactAt.HasValue ? NextContactAt.Value.ToString("dd.MM.yyyy HH:mm") : "—";
}
