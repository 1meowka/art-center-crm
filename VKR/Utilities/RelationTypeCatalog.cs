#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VKR.Models;

namespace VKR.Utilities;

/// <summary>На форме обучающегося: Мама, Папа, Другой. В карточке гостя — ещё брат, сестра, ребёнок.</summary>
public static class RelationTypeCatalog
{
    public const string Mother = "Мама";
    public const string Father = "Папа";
    public const string Brother = "Брат";
    public const string Sister = "Сестра";
    public const string Child = "Ребёнок";
    public const string Other = "Другой";

    public static readonly string[] FormNames = { Mother, Father, Other };
    public static readonly string[] GuestFormNames = { Mother, Father, Brother, Sister, Child, Other };

    public static bool IsParentLike(string? name) =>
        string.Equals(name?.Trim(), Mother, StringComparison.CurrentCultureIgnoreCase)
        || string.Equals(name?.Trim(), Father, StringComparison.CurrentCultureIgnoreCase)
        || IsOther(name);

    public static bool IsSibling(string? name) =>
        string.Equals(name?.Trim(), Brother, StringComparison.CurrentCultureIgnoreCase)
        || string.Equals(name?.Trim(), Sister, StringComparison.CurrentCultureIgnoreCase);

    public static bool IsFormName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return FormNames.Any(n => string.Equals(n, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public static bool IsOther(string? name) =>
        string.Equals(name?.Trim(), Other, StringComparison.CurrentCultureIgnoreCase);

    public static IReadOnlyList<LookupItem> ForForm(IEnumerable<LookupItem> all) =>
        FilterByNames(all, FormNames);

    public static IReadOnlyList<LookupItem> ForGuestForm(IEnumerable<LookupItem> all) =>
        FilterByNames(all, GuestFormNames);

    private static IReadOnlyList<LookupItem> FilterByNames(IEnumerable<LookupItem> all, IReadOnlyList<string> names)
    {
        var byName = all
            .Where(item => names.Any(n =>
                string.Equals(n, item.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            .GroupBy(item => item.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);

        var list = new List<LookupItem>();
        foreach (var name in names)
        {
            if (byName.TryGetValue(name, out var item))
                list.Add(item);
        }
        return list;
    }

    public static LookupItem? Resolve(IReadOnlyList<LookupItem> formItems, int? storedId, IEnumerable<LookupItem>? all = null)
    {
        if (storedId is not > 0)
            return null;

        var exact = formItems.FirstOrDefault(item => item.Id == storedId);
        if (exact != null)
            return exact;

        var storedName = all?.FirstOrDefault(item => item.Id == storedId)?.Name;
        if (!string.IsNullOrWhiteSpace(storedName))
        {
            var match = formItems.FirstOrDefault(item =>
                string.Equals(item.Name, storedName, StringComparison.CurrentCultureIgnoreCase));
            if (match != null)
                return match;
        }

        return formItems.FirstOrDefault(item => IsOther(item.Name));
    }

    public static int? SelectedId(LookupItem? item) =>
        item is { Id: > 0 } ? item.Id : null;

    public static bool HasSelection(object? selectedItem) =>
        selectedItem is LookupItem { Id: > 0 };
}
