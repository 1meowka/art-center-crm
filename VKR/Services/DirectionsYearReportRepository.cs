#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class DirectionsYearReportRepository
{
    public async Task<DirectionsYearReportData> GetReportDataAsync(int academicYearStart)
    {
        var yearEnd = academicYearStart + 1;
        var directions = await GetDirectionsAsync();
        var stats = await GetStatsAsync(academicYearStart, yearEnd);
        var totals = await GetTotalsAsync(academicYearStart, yearEnd);

        return new DirectionsYearReportData
        {
            AcademicYearStart = academicYearStart,
            Directions = directions,
            Stats = stats,
            Totals = totals
        };
    }

    private static async Task<IReadOnlyList<DirectionYearReportDirection>> GetDirectionsAsync()
    {
        var result = new List<DirectionYearReportDirection>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_direction, direction_name
FROM public.direction
WHERE is_active = TRUE
ORDER BY direction_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DirectionYearReportDirection
            {
                IdDirection = reader.GetInt32(0),
                DirectionName = reader.GetString(1)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DirectionMonthStat>> GetStatsAsync(int yearStart, int yearEnd)
    {
        var result = new List<DirectionMonthStat>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    d.id_direction,
    COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) AS fact_month,
    COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) AS fact_year,
    SUM(p.amount) AS total_amount,
    COUNT(DISTINCT c.id_student) AS people_count
FROM public.payment p
INNER JOIN public.contract c ON c.id_contract = p.id_contract
INNER JOIN public.direction d ON d.id_direction = c.id_direction
WHERE (
    (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @yearStart
     AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 8 AND 12)
    OR
    (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @yearEnd
     AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 1 AND 5)
)
GROUP BY d.id_direction, fact_month, fact_year
ORDER BY d.id_direction, fact_year, fact_month;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("yearStart", NpgsqlDbType.Integer).Value = yearStart;
        cmd.Parameters.Add("yearEnd", NpgsqlDbType.Integer).Value = yearEnd;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DirectionMonthStat
            {
                IdDirection = reader.GetInt32(0),
                Month = reader.GetInt32(1),
                Year = reader.GetInt32(2),
                Amount = reader.GetDecimal(3),
                PeopleCount = reader.GetInt32(4)
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<DirectionMonthTotal>> GetTotalsAsync(int yearStart, int yearEnd)
    {
        var result = new List<DirectionMonthTotal>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) AS fact_month,
    COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) AS fact_year,
    SUM(p.amount) AS total_amount,
    COUNT(DISTINCT c.id_student) AS people_count
FROM public.payment p
INNER JOIN public.contract c ON c.id_contract = p.id_contract
INNER JOIN public.direction d ON d.id_direction = c.id_direction
WHERE (
    (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @yearStart
     AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 8 AND 12)
    OR
    (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @yearEnd
     AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 1 AND 5)
)
GROUP BY fact_month, fact_year
ORDER BY fact_year, fact_month;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("yearStart", NpgsqlDbType.Integer).Value = yearStart;
        cmd.Parameters.Add("yearEnd", NpgsqlDbType.Integer).Value = yearEnd;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DirectionMonthTotal
            {
                Month = reader.GetInt32(0),
                Year = reader.GetInt32(1),
                Amount = reader.GetDecimal(2),
                PeopleCount = reader.GetInt32(3)
            });
        }

        return result;
    }
}
