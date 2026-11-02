#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using VKR.Models;

namespace VKR.Services;

public static class DictionaryExcelImporter
{
    public static IReadOnlyList<DictionaryEntry> Read(string path, DictionaryKind kind)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var rows = new List<DictionaryEntry>();
        var headerMap = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var headerConsumed = false;

        foreach (var row in sheet.RowsUsed())
        {
            if (!headerConsumed && IsHeaderRow(row))
            {
                headerMap = MapHeaders(row);
                headerConsumed = true;
                continue;
            }

            var entry = kind == DictionaryKind.Street
                ? ReadStreet(row)
                : ReadInstitution(row, headerMap, kind);

            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                continue;

            rows.Add(entry);
        }

        return rows
            .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static DictionaryEntry ReadStreet(IXLRow row)
    {
        return new DictionaryEntry { Name = ReadStreetName(row) };
    }

    private static DictionaryEntry? ReadInstitution(
        IXLRow row,
        IReadOnlyDictionary<string, int> headers,
        DictionaryKind kind)
    {
        var flag = Cell(row, headers, "признак", 2);
        var name = Cell(row, headers, "наименование", 3);
        if (string.IsNullOrWhiteSpace(name))
            name = Cell(row, headers, "название", 1);
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || LooksLikeIndex(name))
            return null;

        var type = ResolveInstitutionType(flag, name);
        if (kind == DictionaryKind.School && type == "детский сад")
            return null;
        if (kind == DictionaryKind.Kindergarten && type != "детский сад")
            return null;

        var number = Cell(row, headers, "номер", 0);
        if (string.IsNullOrWhiteSpace(number))
            number = ExtractInstitutionNumber(name);

        return new DictionaryEntry
        {
            Name = name,
            Extra =
            {
                ["institution_type"] = type,
                ["institution_number"] = number
            }
        };
    }

    private static string ResolveInstitutionType(string flag, string name)
    {
        var key = (flag ?? string.Empty).Trim().ToLowerInvariant();
        if (key is "д" or "с" or "дс" or "доу" or "сад")
            return "детский сад";

        var fromName = name.ToLowerInvariant();
        if (fromName.Contains("сад") || fromName.Contains("доу") || fromName.Contains("ясли"))
            return "детский сад";
        if (fromName.Contains("гимназ"))
            return "гимназия";
        if (fromName.Contains("лицей"))
            return "лицей";
        if (fromName.Contains("вуз") || fromName.Contains("университет") || fromName.Contains("институт"))
            return "вуз";
        if (fromName.Contains("колледж") || fromName.Contains("техникум"))
            return "колледж";
        return "школа";
    }

    private static string ExtractInstitutionNumber(string name)
    {
        var match = Regex.Match(name, @"№\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string Cell(IXLRow row, IReadOnlyDictionary<string, int> headers, string key, int fallback)
    {
        if (headers.TryGetValue(key, out var index))
            return row.Cell(index).GetString().Trim();
        return fallback > 0 ? row.Cell(fallback).GetString().Trim() : string.Empty;
    }

    private static Dictionary<string, int> MapHeaders(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var cell in row.CellsUsed())
        {
            var raw = cell.GetString().Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (raw.Contains("наименован") || raw is "название" or "улица")
                map["наименование"] = cell.Address.ColumnNumber;
            else if (raw.Contains("признак") || raw is "тип")
                map["признак"] = cell.Address.ColumnNumber;
            else if (raw is "номер" or "№")
                map["номер"] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static bool IsHeaderRow(IXLRow row)
    {
        foreach (var cell in row.CellsUsed())
        {
            var lower = cell.GetString().Trim().ToLowerInvariant();
            if (lower is "название" or "наименование" or "улица" or "школа" or "сад" or "name"
                or "признак" or "тип")
                return true;
            if (lower.Contains("наименован") || lower.Contains("улиц"))
                return true;
        }
        return false;
    }

    private static string ReadStreetName(IXLRow row)
    {
        var first = row.Cell(1).GetString().Trim();
        var second = row.Cell(2).GetString().Trim();
        if (LooksLikeIndex(first) && !string.IsNullOrWhiteSpace(second))
            return second;
        if (!string.IsNullOrWhiteSpace(first) && !LooksLikeIndex(first))
            return first;
        return second;
    }

    private static bool LooksLikeIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim().TrimEnd('.', ')');
        return trimmed.All(char.IsDigit);
    }
}
