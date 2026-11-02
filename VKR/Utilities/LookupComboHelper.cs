#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using VKR.Models;

namespace VKR.Utilities;

public static class LookupComboHelper
{
    public static void SetItems(ComboBox combo, IEnumerable<LookupItem> items, int? selectedId = null)
    {
        var list = items
            .OrderBy(item => item.Name, System.StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        combo.ItemsSource = list;
        SelectById(combo, selectedId);
    }

    public static void SelectById(ComboBox combo, int? id)
    {
        if (!id.HasValue || id.Value <= 0)
        {
            combo.SelectedItem = null;
            combo.SelectedValue = null;
            if (combo.IsEditable)
                combo.Text = string.Empty;
            return;
        }

        if (combo.ItemsSource is IEnumerable<LookupItem> items)
        {
            var match = items.FirstOrDefault(item => item.Id == id.Value);
            combo.SelectedItem = match;
            combo.SelectedValue = match?.Id;
            if (combo.IsEditable)
                combo.Text = match?.Name ?? string.Empty;
            return;
        }

        combo.SelectedValue = id.Value;
    }

    public static int? GetSelectedId(ComboBox combo)
    {
        if (combo.SelectedItem is LookupItem item && item.Id > 0)
            return item.Id;

        if (combo.SelectedValue is int id && id > 0)
            return id;

        return null;
    }

    public static void SelectFirstIfNone(ComboBox combo)
    {
        if (combo.SelectedItem != null || combo.Items.Count == 0)
            return;

        combo.SelectedIndex = 0;
    }

    /// <summary>Подпись договора в комбо: ФИО, затем номер как в БД (без генерации 2526-0001).</summary>
    public static string ContractLabel(string? contractNumber, string? studentFullName)
    {
        var number = (contractNumber ?? string.Empty).Trim();
        var fio = (studentFullName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(number))
            return fio;
        if (string.IsNullOrEmpty(fio))
            return number;
        return $"{fio} — {number}";
    }
}
