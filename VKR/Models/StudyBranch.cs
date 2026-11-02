#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace VKR.Models;

/// <summary>Учебные филиалы центра (расписание, ведомость, бот MAX).</summary>
public sealed class StudyBranch
{
    public int IdBranch { get; init; }
    public string ShortName { get; init; } = string.Empty;
    public string AddressLine { get; init; } = string.Empty;
    public string ButtonLabel { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;

    public static StudyBranch Central => Find(1) ?? Fallback[0];
    public static StudyBranch Park => Find(2) ?? Fallback[1];

    public static IReadOnlyList<StudyBranch> All
    {
        get
        {
            var source = Catalog.Count > 0 ? Catalog : Fallback;
            return source.Where(b => b.IsActive).ToList();
        }
    }

    public static StudyBranch? Find(int idBranch)
    {
        var fromCatalog = Catalog.FirstOrDefault(b => b.IdBranch == idBranch);
        if (fromCatalog != null)
            return fromCatalog;
        return Fallback.FirstOrDefault(b => b.IdBranch == idBranch);
    }

    internal static List<StudyBranch> Catalog { get; } = new();

    public static void ReplaceCatalog(IEnumerable<StudyBranch> items)
    {
        Catalog.Clear();
        Catalog.AddRange(items);
    }

    public static IReadOnlyList<StudyBranch> Fallback { get; } =
    [
        new()
        {
            IdBranch = 1,
            ShortName = "Центральная 1",
            AddressLine = "ул. Центральная, 1",
            ButtonLabel = "Центральная, 1"
        },
        new()
        {
            IdBranch = 2,
            ShortName = "Парковая 10",
            AddressLine = "ул. Парковая, 10",
            ButtonLabel = "Парковая, 10"
        },
        new()
        {
            IdBranch = 3,
            ShortName = "Школьная 5",
            AddressLine = "ул. Школьная, 5",
            ButtonLabel = "Школьная, 5"
        },
        new()
        {
            IdBranch = 4,
            ShortName = "Садовая 3",
            AddressLine = "ул. Садовая, 3",
            ButtonLabel = "Садовая, 3"
        }
    ];
}
