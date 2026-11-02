using System.Windows;
using System.Windows.Media;

namespace VKR.Resources;

/// <summary>Цвета текущей темы — читаются из Application.Resources при каждом обращении.</summary>
public static class ThemeColors
{
    public static string Bg => GetHex("ThemeBgColor");
    public static string Surface => GetHex("ThemeSurfaceColor");
    public static string Primary => GetHex("ThemePrimaryColor");
    public static string PrimaryHover => GetHex("ThemePrimaryHoverColor");
    public static string Text => GetHex("ThemeTextColor");
    public static string TextMuted => GetHex("ThemeTextMutedColor");
    public static string TextSubtle => GetHex("ThemeTextSubtleColor");
    public static string TextSecondary => GetHex("ThemeTextSecondaryColor");
    public static string Border => GetHex("ThemeBorderColor");
    public static string BorderHover => GetHex("ThemeBorderHoverColor");
    public static string HoverSurface => GetHex("ThemeHoverSurfaceColor");
    public static string AltRow => GetHex("ThemeAltRowColor");
    public static string SelectedRow => GetHex("ThemeSelectedRowColor");
    public static string SelectedRowText => GetHex("ThemeSelectedRowTextColor");
    public static string AccentOrange => GetHex("ThemeAccentOrangeColor");
    public static string AccentTeal => GetHex("ThemeAccentTealColor");
    public static string AccentPink => GetHex("ThemeAccentPinkColor");
    public static string AccentYellow => GetHex("ThemeAccentYellowColor");
    public static string AccentBlue => GetHex("ThemeAccentBlueColor");
    public static string AccentViolet => GetHex("ThemeAccentVioletColor");
    public static string SuccessBg => GetHex("ThemeSuccessBgColor");
    public static string SuccessFg => GetHex("ThemeSuccessFgColor");
    public static string WarningBg => GetHex("ThemeWarningBgColor");
    public static string WarningFg => GetHex("ThemeWarningFgColor");
    public static string DangerBg => GetHex("ThemeDangerBgColor");
    public static string DangerFg => GetHex("ThemeDangerFgColor");
    public static string InfoBg => GetHex("ThemeInfoBgColor");
    public static string InfoFg => GetHex("ThemeInfoFgColor");
    public static string NeutralBg => GetHex("ThemeHoverSurfaceColor");
    public static string NeutralFg => GetHex("ThemeTextSecondaryColor");

    public static Color[] ChartPalette =>
    [
        GetColor("ThemeAccentVioletColor"),
        GetColor("ThemeAccentOrangeColor"),
        GetColor("ThemeAccentTealColor"),
        GetColor("ThemeAccentPinkColor"),
        GetColor("ThemeAccentYellowColor"),
        GetColor("ThemeAccentBlueColor"),
    ];

    public static Color GetColor(string key)
    {
        if (Application.Current?.TryFindResource(key) is Color color)
            return color;

        return Colors.SlateGray;
    }

    public static string GetHex(string key)
    {
        var color = GetColor(key);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static SolidColorBrush ToBrush(string hex) => new(ToColor(hex));

    public static SolidColorBrush ToBrushFromKey(string key) => new(GetColor(key));

    public static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    public static SolidColorBrush TintedBadge(Color color) =>
        new(Color.FromArgb(64, color.R, color.G, color.B));
}
