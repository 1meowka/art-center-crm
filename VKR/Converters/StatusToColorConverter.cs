using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VKR.Resources;

namespace VKR.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public string Mode { get; set; } = "background"; // "background" или "foreground"

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? "";
            (string bg, string fg) = status switch
            {
                "новая" => (ThemeColors.InfoBg, ThemeColors.InfoFg),
                "подтверждена" => (ThemeColors.WarningBg, ThemeColors.WarningFg),
                "посещено" => (ThemeColors.SelectedRow, ThemeColors.SelectedRowText),
                "конвертирована" => (ThemeColors.SuccessBg, ThemeColors.SuccessFg),
                "отказ" => (ThemeColors.DangerBg, ThemeColors.DangerFg),
                _ => (ThemeColors.NeutralBg, ThemeColors.NeutralFg),
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Mode == "background" ? bg : fg));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}