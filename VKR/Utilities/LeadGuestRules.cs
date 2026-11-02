#nullable enable
using System;
using VKR.Models;

namespace VKR.Utilities;

public static class LeadGuestRules
{
    public static int? AgeYears(DateTime? birthDate)
    {
        if (!birthDate.HasValue)
            return null;
        var age = DateTime.Today.Year - birthDate.Value.Year;
        if (birthDate.Value.Date > DateTime.Today.AddYears(-age))
            age--;
        return age;
    }

    /// <summary>18+ — взрослый. Без даты рождения считаем ребёнком.</summary>
    public static bool IsAdult(DateTime? birthDate)
    {
        var age = AgeYears(birthDate);
        return age >= 18;
    }

    public static bool RequiresParent(DateTime? birthDate) => !IsAdult(birthDate);

    public static string InvertRelationName(string? relationName)
    {
        if (RelationTypeCatalog.IsParentLike(relationName))
            return RelationTypeCatalog.Child;
        if (string.Equals(relationName?.Trim(), RelationTypeCatalog.Child, StringComparison.CurrentCultureIgnoreCase))
            return RelationTypeCatalog.Other;
        return string.IsNullOrWhiteSpace(relationName) ? "родственник" : relationName.Trim();
    }
}
