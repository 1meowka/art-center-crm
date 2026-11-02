using System;
using System.Collections.Generic;
using System.Linq;
using VKR.Utilities;

namespace VKR.Models
{
    public class Lead
    {
        public int IdLead { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ChildFirstName { get; set; } = string.Empty;
        public string? ChildLastName { get; set; }
        public DateTime? ChildBirthDate { get; set; }
        public string ContactPhone { get; set; } = string.Empty;
        public string? ChildPhone { get; set; }
        public string? ChildResidenceAddress { get; set; }
        public int IdDirection { get; set; }
        public string DirectionName { get; set; } = string.Empty;
        /// <summary>Все выбранные направления (из junction-таблицы).</summary>
        public List<int> DirectionIds { get; set; } = new();
        public string AllDirectionsText { get; set; } = string.Empty;
        public int? IdSource { get; set; }
        public string? SourceName { get; set; }
        public bool ConsentPersonalData { get; set; }
        public string Status { get; set; } = "новая";
        public string? Comment { get; set; }
        public string? ContractNote { get; set; }

        public int? IdRepresentative { get; set; }
        public string RepLastName { get; set; } = string.Empty;
        public string RepFirstName { get; set; } = string.Empty;
        public string? RepMiddleName { get; set; }
        public string? RepEmail { get; set; }
        public string? RepResidenceAddress { get; set; }
        public int? RepRelationTypeId { get; set; }
        public string? RepresentativeFullName { get; set; }

        public int? IdInstitution { get; set; }
        public string? InstitutionName { get; set; }
        public string? Workplace { get; set; }
        public bool IsAdultRecord { get; set; }
        public string? MainPayerName { get; set; }
        public string? MainPayerPhone { get; set; }
        public string? MotherFullName { get; set; }
        public string? MotherPhone { get; set; }
        public string? FatherFullName { get; set; }
        public string? FatherPhone { get; set; }
        public string? SchoolShiftComment { get; set; }
        public string? Parent1Relation { get; set; }
        public string? Parent1FullName { get; set; }
        public string? Parent1Phone { get; set; }
        public short? Parent1Age { get; set; }
        public string? Parent2Relation { get; set; }
        public string? Parent2FullName { get; set; }
        public string? Parent2Phone { get; set; }
        public short? Parent2Age { get; set; }
        public string? Parent3Relation { get; set; }
        public string? Parent3FullName { get; set; }
        public string? Parent3Phone { get; set; }
        public short? Parent3Age { get; set; }
        public string RelativesSummary { get; set; } = string.Empty;

        public string ChildFullName =>
            string.IsNullOrWhiteSpace(ChildLastName)
                ? ChildFirstName.Trim()
                : $"{ChildLastName} {ChildFirstName}".Trim();
        public string RepresentativeDisplayName =>
            !string.IsNullOrWhiteSpace(RepresentativeFullName)
                ? RepresentativeFullName.Trim()
                : !string.IsNullOrWhiteSpace(MainPayerName)
                    ? MainPayerName.Trim()
                    : $"{RepLastName} {RepFirstName} {RepMiddleName}".Trim();

        public string ChildAgeText
        {
            get
            {
                var age = LeadGuestRules.AgeYears(ChildBirthDate);
                return age == null ? "—" : $"{age} лет";
            }
        }

        /// <summary>Несовершеннолетний или дата рождения не указана (считаем несовершеннолетним).</summary>
        public bool IsAdultGuest => IsAdultRecord || LeadGuestRules.IsAdult(ChildBirthDate);
        public bool IsMinor => !IsAdultGuest;
        public string GuestKindText => IsAdultGuest ? "взрослый" : "ребёнок";

        public bool RequiresRepresentative => IsMinor;
        public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
        public bool HasContractNote => !string.IsNullOrWhiteSpace(ContractNote);
        public bool HasManagerComment => HasComment;

        public void SyncParentBlocks()
        {
            if (string.IsNullOrWhiteSpace(Parent1FullName) && !string.IsNullOrWhiteSpace(MotherFullName))
            {
                Parent1Relation = string.IsNullOrWhiteSpace(Parent1Relation) ? "мама" : Parent1Relation;
                Parent1FullName = MotherFullName;
                Parent1Phone = MotherPhone;
            }

            if (string.IsNullOrWhiteSpace(Parent2FullName) && !string.IsNullOrWhiteSpace(FatherFullName))
            {
                Parent2Relation = string.IsNullOrWhiteSpace(Parent2Relation) ? "папа" : Parent2Relation;
                Parent2FullName = FatherFullName;
                Parent2Phone = FatherPhone;
            }

            MotherFullName = null;
            MotherPhone = null;
            FatherFullName = null;
            FatherPhone = null;
            ApplyParent(Parent1Relation, Parent1FullName, Parent1Phone);
            ApplyParent(Parent2Relation, Parent2FullName, Parent2Phone);
            ApplyParent(Parent3Relation, Parent3FullName, Parent3Phone);

            RelativesSummary = string.Join(", ",
                new[]
                {
                    FormatParent(Parent1Relation, Parent1FullName),
                    FormatParent(Parent2Relation, Parent2FullName),
                    FormatParent(Parent3Relation, Parent3FullName)
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private void ApplyParent(string? relation, string? name, string? phone)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            var rel = (relation ?? string.Empty).Trim().ToLowerInvariant();
            if (rel.Contains("мам") || rel.Contains("мать"))
            {
                MotherFullName ??= name;
                MotherPhone ??= phone;
            }
            else if (rel.Contains("пап") || rel.Contains("отец") || rel.Contains("отц"))
            {
                FatherFullName ??= name;
                FatherPhone ??= phone;
            }
            else if (string.IsNullOrWhiteSpace(MotherFullName))
            {
                MotherFullName = name;
                MotherPhone = phone;
            }
            else if (string.IsNullOrWhiteSpace(FatherFullName))
            {
                FatherFullName = name;
                FatherPhone = phone;
            }
        }

        private static string? FormatParent(string? relation, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            return string.IsNullOrWhiteSpace(relation) ? name.Trim() : $"{relation.Trim()} {name.Trim()}";
        }

        public string FilledFieldsText
        {
            get
            {
                var lines = new List<string>();
                void Add(string label, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "—")
                        lines.Add($"{label}: {value.Trim()}");
                }

                Add("ФИО", ChildFullName);
                Add("Дата рождения", ChildBirthDate?.ToString("dd.MM.yyyy"));
                Add("Телефон", DisplayPhone);
                Add("Учреждение", InstitutionName);
                Add("Смена", SchoolShiftComment);
                Add("Адрес", ChildResidenceAddress);
                Add("Источник", SourceName);
                Add("Направления", AllDirectionsText);
                Add("Комментарий", Comment);
                Add("Блок 1", FormatParent(Parent1Relation, Parent1FullName));
                Add("Тел. 1", Parent1Phone);
                Add("Блок 2", FormatParent(Parent2Relation, Parent2FullName));
                Add("Тел. 2", Parent2Phone);
                Add("Блок 3", FormatParent(Parent3Relation, Parent3FullName));
                Add("Тел. 3", Parent3Phone);
                return string.Join(" · ", lines);
            }
        }

        public string SearchBlob => string.Join(" ", new[]
        {
            ChildFullName, DisplayPhone, RelativesSummary, InstitutionName, Workplace,
            ChildResidenceAddress, SourceName, Comment, ContractNote, SchoolShiftComment,
            Parent1FullName, Parent1Phone, Parent2FullName, Parent2Phone, Parent3FullName, Parent3Phone,
            AllDirectionsText, Status
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        public string OwnPhone =>
            !string.IsNullOrWhiteSpace(ContactPhone) ? ContactPhone
            : (string.IsNullOrWhiteSpace(ChildPhone) ? string.Empty : ChildPhone!);

        public string DisplayPhone =>
            !string.IsNullOrWhiteSpace(OwnPhone) ? OwnPhone
            : (MainPayerPhone ?? string.Empty);

        public string InstitutionOrWorkplaceText
        {
            get
            {
                if (IsAdultGuest)
                    return string.IsNullOrWhiteSpace(Workplace) ? "—" : Workplace!;
                return string.IsNullOrWhiteSpace(InstitutionName) ? "—" : InstitutionName!;
            }
        }

        /// <summary>Дополнительные представители (мама, папа, бабушка и т.д.), кроме основного.</summary>
        public List<LeadRepresentativeItem> AdditionalRepresentatives { get; set; } = new();

        /// <summary>Дети клиента (основная связь — от взрослого к ребёнку).</summary>
        public List<LeadChildItem> Children { get; set; } = new();

        /// <summary>Связи с другими карточками гостей (родители, дети, братья/сёстры).</summary>
        public List<LeadRelationItem> Relations { get; set; } = new();

        public string ClientDisplayName =>
            !string.IsNullOrWhiteSpace(RepresentativeDisplayName)
                ? RepresentativeDisplayName
                : ChildFullName;
        public string ChildrenSummary =>
            Relations.Count == 0
                ? (string.IsNullOrWhiteSpace(RelativesSummary) ? "—" : RelativesSummary)
                : string.Join(", ", Relations.Select(c => c.FullName).Where(x => !string.IsNullOrWhiteSpace(x)));

        public string CommentPreview
        {
            get
            {
                if (!HasComment) return "—";
                var clean = Comment!.Replace("\r", " ").Replace("\n", " ").Trim();
                return clean.Length <= 30 ? clean : clean.Substring(0, 30) + "...";
            }
        }

        public string ContractNotePreview
        {
            get
            {
                if (!HasContractNote) return "—";
                var clean = ContractNote!.Replace("\r", " ").Replace("\n", " ").Trim();
                return clean.Length <= 30 ? clean : clean.Substring(0, 30) + "...";
            }
        }
    }

    // Справочник для выпадающих списков
    public class DirectionRef
    {
        public int IdDirection { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public class SourceRef
    {
        public int IdSource { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public class StudentTypeRef
    {
        public int IdStudentType { get; set; }
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    public sealed class LeadRepresentativeItem
    {
        public int IdRepresentative { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? ResidenceAddress { get; set; }
        public int? RelationTypeId { get; set; }
        public string RelationName { get; set; } = string.Empty;

        public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();
        public string DisplayLine => string.IsNullOrWhiteSpace(RelationName)
            ? $"{FullName} · {Phone}"
            : $"{FullName} ({RelationName}) · {Phone}";
    }

    public sealed class LeadChildItem
    {
        public int IdLeadChild { get; set; }
        public int SortOrder { get; set; }
        public string FullName { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? ResidenceAddress { get; set; }

        public string BirthDateText => BirthDate?.ToString("dd.MM.yyyy") ?? "—";
        public string PhoneText => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone!;
        public string AgeText
        {
            get
            {
                var age = LeadGuestRules.AgeYears(BirthDate);
                return age == null ? "—" : $"{age} лет";
            }
        }
    }

    public sealed class LeadDirectionTag
    {
        public int IdDirection { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class LeadRelationItem
    {
        public int IdLeadRelation { get; set; }
        public int IdRelatedLead { get; set; }
        public bool IsIncoming { get; set; }
        public bool IsAdult { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? ResidenceAddress { get; set; }
        public int? IdInstitution { get; set; }
        public string? InstitutionName { get; set; }
        public string? Workplace { get; set; }
        public int? RelationTypeId { get; set; }
        public string RelationName { get; set; } = string.Empty;
        public bool IsMainPayer { get; set; }
        public bool IsLegalRepresentative { get; set; }
        public int SortOrder { get; set; }

        public bool IsNew => IdRelatedLead <= 0;
        public string FullName => FioHelper.FormatFullName(LastName, FirstName, MiddleName);
        public string KindText => IsAdult ? "взрослый" : "ребёнок";
        public string PhoneText => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone!;
        public string RelationText => string.IsNullOrWhiteSpace(RelationName) ? "родственник" : RelationName;
        public string RolesText
        {
            get
            {
                var parts = new List<string>();
                if (IsMainPayer) parts.Add("плательщик");
                if (IsLegalRepresentative) parts.Add("законный представитель");
                return parts.Count == 0 ? "—" : string.Join(", ", parts);
            }
        }
    }
}
