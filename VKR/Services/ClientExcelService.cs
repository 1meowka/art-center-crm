#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class ClientExcelRow
{
    public string ChildFullName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string? Institution { get; set; }
    public string? SchoolShiftComment { get; set; }
    public string? Parent1Relation { get; set; }
    public string? Parent1FullName { get; set; }
    public string? Parent1Phone { get; set; }
    public short? Parent1Age { get; set; }
    public string? Parent2Relation { get; set; }
    public string? Parent2FullName { get; set; }
    public string? Parent2Phone { get; set; }
    public short? Parent2Age { get; set; }
    public string? Address { get; set; }
    public string? Source { get; set; }
    public string? Comment { get; set; }
}

public static class ClientExcelService
{
    public static IReadOnlyList<ClientExcelRow> Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var rows = new List<ClientExcelRow>();
        Dictionary<string, int>? headers = null;

        foreach (var row in sheet.RowsUsed())
        {
            if (headers == null && IsHeader(row))
            {
                headers = MapHeaders(row);
                continue;
            }

            if (headers == null)
                continue;

            var name = Cell(row, headers, "child");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            rows.Add(new ClientExcelRow
            {
                ChildFullName = name,
                BirthDate = ParseDate(Cell(row, headers, "birth"), row, headers),
                Institution = EmptyToNull(Cell(row, headers, "school")),
                SchoolShiftComment = EmptyToNull(Cell(row, headers, "shift")),
                Parent1Relation = EmptyToNull(Cell(row, headers, "rel1")),
                Parent1FullName = EmptyToNull(Cell(row, headers, "name1")),
                Parent1Phone = EmptyToNull(Cell(row, headers, "phone1")),
                Parent1Age = ParseAge(Cell(row, headers, "age1")),
                Parent2Relation = EmptyToNull(Cell(row, headers, "rel2")),
                Parent2FullName = EmptyToNull(Cell(row, headers, "name2")),
                Parent2Phone = EmptyToNull(Cell(row, headers, "phone2")),
                Parent2Age = ParseAge(Cell(row, headers, "age2")),
                Address = EmptyToNull(Cell(row, headers, "address")),
                Source = EmptyToNull(Cell(row, headers, "source")),
                Comment = EmptyToNull(Cell(row, headers, "comment"))
            });
        }

        return rows;
    }

    public static void Write(string path, IEnumerable<Lead> leads)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Клиенты");
        var headers = new[]
        {
            "ФИ ребенка", "дата рождения", "посещение школы/дс", "коммент (смена в школе)",
            "принадлежность", "ФИО родителя", "Телефон родителя", "возраст",
            "принадлежность", "ФИО родителя", "Телефон родителя", "возраст",
            "адрес", "откуда узнали", "большой коммент"
        };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        var r = 2;
        foreach (var lead in leads)
        {
            lead.SyncParentBlocks();
            sheet.Cell(r, 1).Value = lead.ChildFullName;
            if (lead.ChildBirthDate.HasValue)
                sheet.Cell(r, 2).Value = lead.ChildBirthDate.Value.ToString("dd.MM.yyyy");
            sheet.Cell(r, 3).Value = lead.InstitutionName ?? "";
            sheet.Cell(r, 4).Value = lead.SchoolShiftComment ?? "";
            sheet.Cell(r, 5).Value = lead.Parent1Relation ?? "";
            sheet.Cell(r, 6).Value = lead.Parent1FullName ?? "";
            sheet.Cell(r, 7).Value = lead.Parent1Phone ?? "";
            if (lead.Parent1Age.HasValue)
                sheet.Cell(r, 8).Value = lead.Parent1Age.Value;
            sheet.Cell(r, 9).Value = lead.Parent2Relation ?? "";
            sheet.Cell(r, 10).Value = lead.Parent2FullName ?? "";
            sheet.Cell(r, 11).Value = lead.Parent2Phone ?? "";
            if (lead.Parent2Age.HasValue)
                sheet.Cell(r, 12).Value = lead.Parent2Age.Value;
            sheet.Cell(r, 13).Value = lead.ChildResidenceAddress ?? "";
            sheet.Cell(r, 14).Value = lead.SourceName ?? "";
            sheet.Cell(r, 15).Value = lead.Comment ?? "";
            r++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    public static async System.Threading.Tasks.Task<(int added, int skipped, List<string> errors)> ImportAsync(
        string path,
        LeadRepository leads,
        StudentRepository students,
        DictionariesRepository dictionaries)
    {
        var rows = Read(path);
        var existing = await leads.GetAllAsync();
        var sources = await leads.GetSourcesAsync();
        var institutions = await dictionaries.GetEntriesAsync(DictionaryKind.EducationalInstitution);
        var added = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            try
            {
                var key = row.ChildFullName.Trim();
                if (existing.Any(x => string.Equals(x.ChildFullName, key, StringComparison.CurrentCultureIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                var fio = FioHelper.Parse(key);
                var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
                var firstStored = string.IsNullOrWhiteSpace(fio.MiddleName)
                    ? firstName
                    : $"{firstName} {fio.MiddleName}".Trim();

                int? sourceId = null;
                if (!string.IsNullOrWhiteSpace(row.Source))
                {
                    var src = sources.FirstOrDefault(s =>
                        s.Name.Equals(row.Source.Trim(), StringComparison.CurrentCultureIgnoreCase));
                    sourceId = src?.IdSource;
                }

                int? institutionId = null;
                if (!string.IsNullOrWhiteSpace(row.Institution))
                {
                    var inst = institutions.FirstOrDefault(s =>
                        s.Name.Equals(row.Institution.Trim(), StringComparison.CurrentCultureIgnoreCase));
                    institutionId = inst?.Id;
                }

                var lead = new Lead
                {
                    CreatedDate = DateTime.Now,
                    ChildLastName = lastName,
                    ChildFirstName = string.IsNullOrWhiteSpace(firstStored) ? lastName : firstStored,
                    ChildBirthDate = row.BirthDate,
                    ChildResidenceAddress = row.Address,
                    SchoolShiftComment = row.SchoolShiftComment,
                    Parent1Relation = row.Parent1Relation,
                    Parent1FullName = row.Parent1FullName,
                    Parent1Phone = row.Parent1Phone,
                    Parent1Age = row.Parent1Age,
                    Parent2Relation = row.Parent2Relation,
                    Parent2FullName = row.Parent2FullName,
                    Parent2Phone = row.Parent2Phone,
                    Parent2Age = row.Parent2Age,
                    Comment = row.Comment,
                    IdSource = sourceId,
                    IdInstitution = institutionId,
                    Status = "новая",
                    ConsentPersonalData = false
                };
                lead.SyncParentBlocks();
                await leads.SaveWithRepresentativeAsync(lead, students);
                existing.Add(lead);
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.ChildFullName}: {ex.Message}");
            }
        }

        return (added, skipped, errors);
    }

    private static bool IsHeader(IXLRow row)
    {
        var text = string.Join(" ", row.CellsUsed().Select(c => c.GetString().Trim().ToLowerInvariant()));
        return text.Contains("ребен") || text.Contains("ребён") || text.Contains("фио");
    }

    private static Dictionary<string, int> MapHeaders(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var relSeen = 0;
        var nameSeen = 0;
        var phoneSeen = 0;
        var ageSeen = 0;
        foreach (var cell in row.CellsUsed())
        {
            var raw = cell.GetString().Trim().ToLowerInvariant();
            var col = cell.Address.ColumnNumber;
            if (raw.Contains("ребен") || raw.Contains("ребён") || raw.Contains("фи ребенка"))
                map["child"] = col;
            else if (raw.Contains("рождени"))
                map["birth"] = col;
            else if (raw.Contains("школ") || raw.Contains("посещен"))
                map["school"] = col;
            else if (raw.Contains("смена"))
                map["shift"] = col;
            else if (raw.Contains("принадлеж"))
            {
                relSeen++;
                map[relSeen == 1 ? "rel1" : "rel2"] = col;
            }
            else if (raw.Contains("фио родит"))
            {
                nameSeen++;
                map[nameSeen == 1 ? "name1" : "name2"] = col;
            }
            else if (raw.Contains("телефон"))
            {
                phoneSeen++;
                map[phoneSeen == 1 ? "phone1" : "phone2"] = col;
            }
            else if (raw.Contains("возраст"))
            {
                ageSeen++;
                map[ageSeen == 1 ? "age1" : "age2"] = col;
            }
            else if (raw.Contains("адрес"))
                map["address"] = col;
            else if (raw.Contains("откуда") || raw.Contains("узнал"))
                map["source"] = col;
            else if (raw.Contains("коммент") && !raw.Contains("смена"))
                map["comment"] = col;
        }

        if (!map.ContainsKey("child"))
            map["child"] = 3;
        return map;
    }

    private static string Cell(IXLRow row, IReadOnlyDictionary<string, int> headers, string key)
    {
        return headers.TryGetValue(key, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;
    }

    private static string? EmptyToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static short? ParseAge(string text)
    {
        return short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : null;
    }

    private static DateTime? ParseDate(string text, IXLRow row, IReadOnlyDictionary<string, int> headers)
    {
        if (headers.TryGetValue("birth", out var col))
        {
            var cell = row.Cell(col);
            if (cell.TryGetValue(out DateTime dt))
                return dt.Date;
        }

        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var parsed))
            return parsed.Date;
        if (DateTime.TryParseExact(text, new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed.Date;
        return null;
    }
}
