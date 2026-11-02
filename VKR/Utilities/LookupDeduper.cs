#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VKR.Models;

namespace VKR.Utilities;

public static class LookupDeduper
{
    public static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    public static List<LookupItem> ByName(IEnumerable<LookupItem> items) =>
        items
            .GroupBy(item => NormalizeKey(item.Name))
            .Select(group => group.MinBy(item => item.Id)!)
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static List<RoleItem> ByRoleCode(IEnumerable<RoleItem> items) =>
        items
            .GroupBy(item => NormalizeKey(item.RoleCode))
            .Select(group => group.MinBy(item => item.IdRole)!)
            .OrderBy(item => item.RoleName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static List<T> ByName<T>(IEnumerable<T> items, Func<T, string> nameSelector, Func<T, int> idSelector) =>
        items
            .GroupBy(item => NormalizeKey(nameSelector(item)))
            .Select(group => group.MinBy(idSelector)!)
            .OrderBy(item => nameSelector(item), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
