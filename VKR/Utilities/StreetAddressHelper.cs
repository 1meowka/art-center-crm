#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VKR.Models;

namespace VKR.Utilities;

public readonly record struct StreetAddressParts(string Street, string House, string Apartment);

public static class StreetAddressHelper
{
    public static StreetAddressParts Split(string? address, IReadOnlyList<LookupItem> streets)
    {
        var text = (address ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
            return new StreetAddressParts(string.Empty, string.Empty, string.Empty);

        var match = streets
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .OrderByDescending(item => item.Name.Length)
            .FirstOrDefault(item =>
                text.StartsWith(item.Name.Trim(), StringComparison.CurrentCultureIgnoreCase));

        string street;
        string rest;
        if (match == null)
        {
            street = string.Empty;
            rest = text;
        }
        else
        {
            street = match.Name;
            rest = text[match.Name.Trim().Length..].Trim().TrimStart(',', '.', ';', ' ');
        }

        var (house, apartment) = SplitHouseApartment(rest);
        return new StreetAddressParts(street, house, apartment);
    }

    public static string? Combine(string? street, string? house, string? apartment)
    {
        street = street?.Trim();
        house = house?.Trim();
        apartment = apartment?.Trim();
        if (string.IsNullOrWhiteSpace(street)
            && string.IsNullOrWhiteSpace(house)
            && string.IsNullOrWhiteSpace(apartment))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street))
            parts.Add(street);
        if (!string.IsNullOrWhiteSpace(house))
            parts.Add("д. " + house);
        if (!string.IsNullOrWhiteSpace(apartment))
            parts.Add("кв. " + apartment);
        return string.Join(", ", parts);
    }

    private static (string House, string Apartment) SplitHouseApartment(string rest)
    {
        rest = rest.Trim().TrimStart(',', ';').Trim();
        if (string.IsNullOrEmpty(rest))
            return (string.Empty, string.Empty);

        var aptMatch = Regex.Match(rest, @"кв\.?\s*(.+)$", RegexOptions.IgnoreCase);
        var apartment = aptMatch.Success ? aptMatch.Groups[1].Value.Trim().TrimEnd(',', ';') : string.Empty;
        var withoutApt = aptMatch.Success
            ? rest[..aptMatch.Index].Trim().TrimEnd(',', ';')
            : rest;

        var house = Regex.Replace(withoutApt, @"^(д(?:ом)?\.?\s*)", string.Empty, RegexOptions.IgnoreCase)
            .Trim()
            .TrimEnd(',', ';');
        return (house, apartment);
    }
}
