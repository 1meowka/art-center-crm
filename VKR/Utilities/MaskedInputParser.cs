#nullable enable
using System;
using System.Globalization;
using System.Windows.Controls;
using VKR.Models;

namespace VKR.Utilities;

public static class MaskedInputParser
{
    public static bool TryParseTime(string? text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim().Replace("_", string.Empty);
        if (cleaned.Length != 5 || cleaned[2] != ':')
            return false;

        if (!int.TryParse(cleaned.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(cleaned.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            return false;

        if (hours is < 0 or > 23 || minutes is < 0 or > 59)
            return false;

        result = new TimeSpan(hours, minutes, 0);
        return true;
    }

    public static void SelectFirstIfNone(ComboBox combo)
    {
        if (TryGetSelectedId(combo, out var id) && id > 0)
            return;

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    public static bool TryGetSelectedId(ComboBox combo, out int id)
    {
        id = 0;
        if (combo.SelectedValue is int selectedId && selectedId > 0)
        {
            id = selectedId;
            return true;
        }

        if (combo.SelectedItem is LookupItem item && item.Id > 0)
        {
            id = item.Id;
            return true;
        }

        return false;
    }
}
