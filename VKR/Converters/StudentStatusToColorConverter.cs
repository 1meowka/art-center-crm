using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VKR.Resources;

namespace VKR.Converters
{
    public class StudentStatusToColorConverter : IValueConverter
    {
        public string Mode { get; set; } = "background";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? "";
            (string bg, string fg) = status switch
            {
                "активный" => (ThemeColors.SuccessBg, ThemeColors.SuccessFg),
                "приостановлен" => (ThemeColors.WarningBg, ThemeColors.WarningFg),
                "архивный" => (ThemeColors.NeutralBg, ThemeColors.NeutralFg),
                _ => (ThemeColors.NeutralBg, ThemeColors.NeutralFg),
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Mode == "background" ? bg : fg));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}