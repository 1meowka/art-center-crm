#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VKR.Models;

namespace VKR.Utilities;

public static class LeadEnrolleeHelper
{
    public static List<LeadEnrolleeCandidate> BuildCandidates(
        Lead lead,
        IReadOnlyList<LeadRelationItem> relations,
        IReadOnlyList<LeadChildItem>? children = null,
        IReadOnlyList<LeadRepresentativeItem>? additionalReps = null)
    {
        var result = new List<LeadEnrolleeCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(LeadEnrolleeCandidate candidate)
        {
            var key = candidate.FullName.Trim();
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                return;
            result.Add(candidate);
        }

        var selfFio = FioHelper.Parse(lead.ChildFullName);
        var (selfLast, selfFirst) = FioHelper.NormalizeStoredNames(
            string.IsNullOrWhiteSpace(lead.ChildLastName) ? selfFio.LastName : lead.ChildLastName,
            string.IsNullOrWhiteSpace(selfFio.FirstName) ? lead.ChildFirstName : selfFio.FirstName);
        AddCandidate(new LeadEnrolleeCandidate
        {
            RoleLabel = lead.IsAdultGuest ? "Взрослый гость" : "Ребёнок",
            LastName = selfLast,
            FirstName = selfFirst,
            MiddleName = selfFio.MiddleName,
            BirthDate = lead.ChildBirthDate,
            Phone = string.IsNullOrWhiteSpace(lead.OwnPhone) ? null : lead.OwnPhone,
            ResidenceAddress = lead.ChildResidenceAddress,
            IdLead = lead.IdLead,
            IdInstitution = lead.IdInstitution
        });

        foreach (var rel in relations.Where(r => !string.IsNullOrWhiteSpace(r.FullName)))
        {
            AddCandidate(new LeadEnrolleeCandidate
            {
                RoleLabel = rel.RelationText,
                LastName = rel.LastName,
                FirstName = rel.FirstName,
                MiddleName = rel.MiddleName,
                BirthDate = rel.BirthDate,
                Phone = rel.Phone,
                ResidenceAddress = rel.ResidenceAddress,
                IdLead = rel.IdRelatedLead > 0 ? rel.IdRelatedLead : null,
                IdInstitution = rel.IdInstitution
            });
        }

        if (children != null)
        {
            foreach (var child in children.Where(c => !string.IsNullOrWhiteSpace(c.FullName)))
            {
                var fio = FioHelper.Parse(child.FullName);
                var (ln, fn) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
                AddCandidate(new LeadEnrolleeCandidate
                {
                    RoleLabel = "Ребёнок",
                    LastName = ln,
                    FirstName = fn,
                    MiddleName = fio.MiddleName,
                    BirthDate = child.BirthDate,
                    Phone = child.Phone,
                    ResidenceAddress = child.ResidenceAddress
                });
            }
        }

        if (additionalReps != null)
        {
            foreach (var rep in additionalReps)
            {
                if (string.IsNullOrWhiteSpace(rep.LastName) && string.IsNullOrWhiteSpace(rep.FirstName))
                    continue;
                var (ln, fn) = FioHelper.NormalizeStoredNames(rep.LastName, rep.FirstName);
                AddCandidate(new LeadEnrolleeCandidate
                {
                    RoleLabel = string.IsNullOrWhiteSpace(rep.RelationName) ? "Родственник" : rep.RelationName,
                    LastName = ln,
                    FirstName = fn,
                    MiddleName = rep.MiddleName,
                    Phone = string.IsNullOrWhiteSpace(rep.Phone) ? null : rep.Phone,
                    ResidenceAddress = rep.ResidenceAddress,
                    IdRepresentative = rep.IdRepresentative > 0 ? rep.IdRepresentative : null,
                    RelationTypeId = rep.RelationTypeId
                });
            }
        }

        return result;
    }

    public static LeadEnrolleeCandidate? PickDefaultStudent(IReadOnlyList<LeadEnrolleeCandidate> candidates)
    {
        if (candidates.Count == 0)
            return null;
        return candidates.FirstOrDefault(c => c.RoleLabel is "Ребёнок" or "Взрослый гость") ?? candidates[0];
    }

    public static string FormatClientPayerLine(Lead lead, IReadOnlyList<LeadRelationItem> relations)
    {
        var payer = relations.FirstOrDefault(r => r.IsMainPayer)
                    ?? relations.FirstOrDefault(r => r.IsAdult);
        if (payer == null)
        {
            if (lead.IsAdultGuest)
                return $"Гость: {lead.ChildFullName}" +
                       (string.IsNullOrWhiteSpace(lead.OwnPhone) ? "" : $", тел. {lead.OwnPhone}");
            return "Плательщик в карточке не указан — договор оформляется от выбранного обучающегося.";
        }

        var phone = string.IsNullOrWhiteSpace(payer.Phone) ? "" : $", тел. {payer.Phone}";
        return $"Основной плательщик: {payer.FullName}{phone}";
    }
}
