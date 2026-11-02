#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace VKR.Models;

/// <summary>Кандидат на зачисление из карточки гостя (клиент, ребёнок или родственник).</summary>
public sealed class LeadEnrolleeCandidate
{
    public string RoleLabel { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? ResidenceAddress { get; set; }
    public int? IdLead { get; set; }
    public int? IdInstitution { get; set; }
    public bool IsClient { get; set; }
    public int? IdRepresentative { get; set; }
    public int? RelationTypeId { get; set; }

    public string FullName =>
        string.IsNullOrWhiteSpace(LastName)
            ? $"{FirstName} {MiddleName}".Trim()
            : $"{LastName} {FirstName} {MiddleName}".Trim();

    public string DisplayLine
    {
        get
        {
            var parts = new List<string> { FullName, RoleLabel };
            if (BirthDate.HasValue)
                parts.Add(BirthDate.Value.ToString("dd.MM.yyyy"));
            if (!string.IsNullOrWhiteSpace(Phone))
                parts.Add(Phone);
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string SearchText => $"{FullName} {RoleLabel} {Phone}".ToLowerInvariant();
}
