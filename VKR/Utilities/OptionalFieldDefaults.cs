#nullable enable
using System;

namespace VKR.Utilities;

/// <summary>Подстановки для необязательных полей форм (пустое → допустимое значение в БД).</summary>
public static class OptionalFieldDefaults
{
    public const string EmptyName = "—";
    public const string EmptyPhone = "0000000000";

    public static string NameOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? EmptyName : value.Trim();

    public static string? NameOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static DateTime BirthDateOrDefault(DateTime? value) =>
        value is { } d && d.Date <= DateTime.Today ? d.Date : new DateTime(2012, 1, 1);

    public static DateTime ArrivalDateOrDefault(DateTime? value) =>
        value?.Date ?? DateTime.Today;

    public static string? PhoneOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = System.Text.RegularExpressions.Regex.Replace(value, @"[^\d]", "");
        return digits.Length >= 10 ? value.Trim() : null;
    }

    public static int StudentTypeOrDefault(int value, int fallback = 3) =>
        value > 0 ? value : fallback;

    public static int? DirectionOrNull(int value) =>
        value > 0 ? value : null;

    public static int? DirectionOrNull(int? value) =>
        value is > 0 ? value : null;

    public static int TeacherOrDefault(int value) =>
        value > 0 ? value : 1;

    public static short GroupNumberOrDefault(short value) =>
        value > 0 ? value : (short)1;

    public static string ContractNumberOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? $"Д-{DateTime.Today:yyyyMMdd}-{Random.Shared.Next(100, 999)}" : value.Trim();

    public static string DictionaryNameOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Без названия" : value.Trim();
}
