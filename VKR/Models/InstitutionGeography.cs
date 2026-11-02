#nullable enable

namespace VKR.Models;

public sealed class InstitutionGeographyRow
{
    public string Category { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public int StudentCount { get; set; }

    public string CategoryLabel => string.IsNullOrWhiteSpace(Category) ? "—" : Category;
    public string PlaceLabel => string.IsNullOrWhiteSpace(PlaceName) ? "—" : PlaceName;
}

public sealed class InstitutionGeographySummary
{
    public int PreschoolCount { get; set; }
    public int SchoolCount { get; set; }
    public int OtherCount { get; set; }
    public int TotalCount { get; set; }
}
