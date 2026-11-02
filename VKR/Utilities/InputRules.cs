using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VKR.Utilities;

public static class InputRules
{
    public static readonly DependencyProperty AutoCapitalizeProperty =
        DependencyProperty.RegisterAttached(
            "AutoCapitalize",
            typeof(bool),
            typeof(InputRules),
            new PropertyMetadata(false, OnAutoCapitalizeChanged));

    public static void SetAutoCapitalize(DependencyObject element, bool value) =>
        element.SetValue(AutoCapitalizeProperty, value);

    public static bool GetAutoCapitalize(DependencyObject element) =>
        (bool)element.GetValue(AutoCapitalizeProperty);

    private static void OnAutoCapitalizeChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.LostFocus -= TextBox_LostFocus;
            textBox.LostFocus += TextBox_LostFocus;
        }
        else
            textBox.LostFocus -= TextBox_LostFocus;
    }

    private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (ShouldSkip(textBox))
            return;

        var value = textBox.Text;
        if (string.IsNullOrWhiteSpace(value))
            return;

        textBox.Text = CapitalizeFirstLetter(value);
    }

    public static string CapitalizeFirstLetter(string value)
    {
        value = value.TrimStart();
        if (value.Length == 0)
            return value;

        var chars = value.ToCharArray();
        chars[0] = char.ToUpper(chars[0], CultureInfo.GetCultureInfo("ru-RU"));
        return new string(chars);
    }

    private static bool ShouldSkip(TextBox textBox)
    {
        var name = textBox.Name ?? string.Empty;
        if (name.Contains("Email", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Search", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Phone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Login", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Password", StringComparison.OrdinalIgnoreCase))
            return true;

        return textBox.Text.Contains('@') || textBox.Text.All(char.IsDigit);
    }
}
