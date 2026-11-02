#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using VKR.Models;

namespace VKR.Services;

public sealed class InstitutionGeographyRepository
{
    public async Task<IReadOnlyList<InstitutionGeographyRow>> GetRowsAsync()
    {
        var result = new List<InstitutionGeographyRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    CASE
        WHEN LOWER(COALESCE(st.type_name, '')) LIKE '%дошколь%' THEN 'Детский сад'
        WHEN LOWER(COALESCE(st.type_name, '')) LIKE '%школ%' THEN 'Школа'
        WHEN LOWER(COALESCE(ei.institution_type, '')) LIKE '%детск%' THEN 'Детский сад'
        WHEN LOWER(COALESCE(ei.institution_type, '')) IN ('школа', 'гимназия', 'лицей') THEN 'Школа'
        ELSE COALESCE(NULLIF(st.type_name, ''), 'Не указано')
    END AS category,
    COALESCE(NULLIF(ei.institution_name, ''), NULLIF(s.school_number, ''), 'Не указано') AS place_name,
    COUNT(*)::int AS student_count
FROM public.student s
LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
LEFT JOIN public.educational_institution ei ON ei.id_institution = s.id_institution
GROUP BY 1, 2
ORDER BY student_count DESC, place_name;", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new InstitutionGeographyRow
            {
                Category = reader.GetString(0),
                PlaceName = reader.GetString(1),
                StudentCount = reader.GetInt32(2)
            });
        }

        return result;
    }

    public async Task<InstitutionGeographySummary> GetSummaryAsync()
    {
        var rows = await GetRowsAsync();
        var summary = new InstitutionGeographySummary();
        foreach (var row in rows)
        {
            summary.TotalCount += row.StudentCount;
            if (row.Category.Contains("Детский", StringComparison.OrdinalIgnoreCase))
                summary.PreschoolCount += row.StudentCount;
            else if (row.Category.Contains("Школа", StringComparison.OrdinalIgnoreCase))
                summary.SchoolCount += row.StudentCount;
            else
                summary.OtherCount += row.StudentCount;
        }

        return summary;
    }
}
