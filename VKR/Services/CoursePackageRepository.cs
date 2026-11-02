#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class CoursePackageRepository
{
    public async Task<IReadOnlyList<CoursePackageLookupItem>> GetActiveLookupAsync(int? idDirection = null)
    {
        var result = new List<CoursePackageLookupItem>();

        FinanceSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT cp.id_course_package, cp.course_name, cp.id_direction, cp.duration_months,
       cp.lessons_count, cp.times_per_week, COALESCE(d.direction_name, '')
FROM public.course_package cp
LEFT JOIN public.direction d ON d.id_direction = cp.id_direction
WHERE cp.is_active = true
  AND (
      @id_direction IS NULL
      OR cp.id_direction IS NULL
      OR cp.id_direction = @id_direction
  )
ORDER BY cp.times_per_week, cp.duration_months DESC, cp.course_name;", connection);
        command.Parameters.Add("id_direction", NpgsqlDbType.Integer).Value =
            idDirection.HasValue ? idDirection.Value : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new CoursePackageLookupItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                IdDirection = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DurationMonths = reader.GetDecimal(3),
                LessonsCount = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                TimesPerWeek = reader.GetInt16(5),
                DirectionName = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return result;
    }
}
