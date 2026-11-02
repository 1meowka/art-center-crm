#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class AdminReportRepository
{
    private static readonly int[] AcademicMonths = [8, 9, 10, 11, 12, 1, 2, 3, 4, 5];
    private static readonly string[] AcademicMonthShort = ["авг", "сен", "окт", "ноя", "дек", "янв", "фев", "мар", "апр", "май"];

    public static string[] AcademicMonthLabels => AcademicMonthShort;

    public async Task<AdminReportData> GetReportDataAsync(AdminReportRequest request)
    {
        request.PeriodStart = request.PeriodStart.Date;
        request.PeriodEnd = request.PeriodEnd.Date;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var data = new AdminReportData { Request = request };
        data.LeadSources = await GetLeadSourceStatsAsync(connection, request.PeriodStart, request.PeriodEnd);
        data.DirectionGroups = await GetDirectionGroupReportsAsync(connection, request.AcademicYearStart);
        data.CampStudents = await GetCampStudentsAsync(connection, request.PeriodStart, request.PeriodEnd);
        return data;
    }

    private static async Task<List<LeadSourceStatRow>> GetLeadSourceStatsAsync(
        NpgsqlConnection connection, DateTime start, DateTime end)
    {
        const string sql = @"
WITH lead_stats AS (
    SELECT
        COALESCE(so.source_name, 'Не указан') AS source_name,
        COUNT(*)::int AS leads_count,
        COUNT(*) FILTER (WHERE lr.status = 'конвертирована')::int AS converted_count
    FROM public.lead_request lr
    LEFT JOIN public.source_of_arrival so ON so.id_source = lr.id_source
    WHERE lr.created_date::date BETWEEN @start AND @end
    GROUP BY COALESCE(so.source_name, 'Не указан')
),
student_stats AS (
    SELECT
        COALESCE(src.source_name, 'Не указан') AS source_name,
        COUNT(DISTINCT s.id_student)::int AS new_students
    FROM public.student s
    LEFT JOIN LATERAL (
        SELECT so.source_name
        FROM public.lead_request lr
        LEFT JOIN public.source_of_arrival so ON so.id_source = lr.id_source
        WHERE LOWER(TRIM(COALESCE(lr.child_last_name, '') || ' ' || lr.child_first_name))
              = LOWER(TRIM(s.last_name || ' ' || s.first_name))
        ORDER BY lr.created_date DESC
        LIMIT 1
    ) src ON TRUE
    WHERE s.arrival_date::date BETWEEN @start AND @end
    GROUP BY COALESCE(src.source_name, 'Не указан')
),
contract_stats AS (
    SELECT
        COALESCE(so.source_name, 'Не указан') AS source_name,
        COUNT(DISTINCT c.id_contract)::int AS contracts_count,
        COALESCE(SUM(c.total_amount), 0)::numeric(12,2) AS revenue
    FROM public.contract c
    LEFT JOIN LATERAL (
        SELECT lr.id_source
        FROM public.lead_request lr
        JOIN public.student s ON s.id_student = c.id_student
        WHERE LOWER(TRIM(COALESCE(lr.child_last_name, '') || ' ' || lr.child_first_name))
              = LOWER(TRIM(s.last_name || ' ' || s.first_name))
        ORDER BY lr.created_date DESC
        LIMIT 1
    ) matched ON TRUE
    LEFT JOIN public.source_of_arrival so ON so.id_source = matched.id_source
    WHERE c.contract_date::date BETWEEN @start AND @end
    GROUP BY COALESCE(so.source_name, 'Не указан')
),
all_sources AS (
    SELECT source_name FROM lead_stats
    UNION SELECT source_name FROM student_stats
    UNION SELECT source_name FROM contract_stats
)
SELECT
    a.source_name,
    COALESCE(ls.leads_count, 0) AS leads_count,
    COALESCE(ls.converted_count, 0) AS converted_count,
    COALESCE(ss.new_students, 0) AS new_students,
    COALESCE(cs.contracts_count, 0) AS contracts_count,
    COALESCE(cs.revenue, 0) AS revenue
FROM all_sources a
LEFT JOIN lead_stats ls ON ls.source_name = a.source_name
LEFT JOIN student_stats ss ON ss.source_name = a.source_name
LEFT JOIN contract_stats cs ON cs.source_name = a.source_name
ORDER BY COALESCE(ls.leads_count, 0) DESC, a.source_name;";

        var result = new List<LeadSourceStatRow>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("start", NpgsqlDbType.Date).Value = start;
        cmd.Parameters.Add("end", NpgsqlDbType.Date).Value = end;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LeadSourceStatRow
            {
                SourceName = reader.GetString(0),
                LeadsCount = reader.GetInt32(1),
                ConvertedLeadsCount = reader.GetInt32(2),
                NewStudentsCount = reader.GetInt32(3),
                ContractsCount = reader.GetInt32(4),
                Revenue = reader.GetDecimal(5)
            });
        }

        return result;
    }

    public async Task<List<LeadSourceStatRow>> GetLeadSourceStatsForDashboardAsync(DateTime start, DateTime end)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        return await GetLeadSourceStatsAsync(connection, start.Date, end.Date);
    }

    private static async Task<List<DirectionGroupReport>> GetDirectionGroupReportsAsync(
        NpgsqlConnection connection, int academicYearStart)
    {
        var yearEnd = academicYearStart + 1;
        var groups = new List<(int IdGroup, string DirectionName, short GroupNumber, string TeacherName, string ScheduleText)>();

        const string groupsSql = @"
SELECT
    g.id_group,
    d.direction_name,
    g.group_number,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    COALESCE((
        SELECT string_agg(
            CASE sc.day_of_week
                WHEN 1 THEN 'пн' WHEN 2 THEN 'вт' WHEN 3 THEN 'ср'
                WHEN 4 THEN 'чт' WHEN 5 THEN 'пт' WHEN 6 THEN 'сб' WHEN 7 THEN 'вс'
            END || ' ' || TO_CHAR(sc.time_start, 'HH24:MI') || '-' || TO_CHAR(sc.time_end, 'HH24:MI'),
            ', ' ORDER BY sc.day_of_week, sc.time_start)
        FROM public.schedule sc
        WHERE sc.id_group = g.id_group AND sc.is_active = TRUE
    ), '') AS schedule_text
FROM public.study_group g
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.teacher t ON t.id_teacher = g.id_teacher
WHERE g.is_active = TRUE
  AND d.direction_name NOT ILIKE '%лагер%'
ORDER BY d.direction_name, g.group_number;";

        await using (var cmd = new NpgsqlCommand(groupsSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                groups.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt16(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }
        }

        var paymentsByStudent = await LoadMonthlyPaymentsAsync(connection, academicYearStart, yearEnd);
        var reports = new Dictionary<string, DirectionGroupReport>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (!reports.TryGetValue(group.DirectionName, out var directionReport))
            {
                directionReport = new DirectionGroupReport { DirectionName = group.DirectionName };
                reports[group.DirectionName] = directionReport;
            }

            var section = new GroupReportSection
            {
                IdGroup = group.IdGroup,
                TeacherName = group.TeacherName,
                ScheduleText = group.ScheduleText,
                GroupTitle = $"гр. №{group.GroupNumber} · преп. {group.TeacherName}"
            };

            section.Students = await LoadGroupStudentsAsync(connection, group.IdGroup, paymentsByStudent);
            for (var i = 0; i < 10; i++)
                section.MonthlyTotals[i] = section.Students.Sum(s => s.MonthlyPayments[i]);

            directionReport.Groups.Add(section);
        }

        return reports.Values.OrderBy(x => x.DirectionName).ToList();
    }

    private static async Task<Dictionary<int, decimal[]>> LoadMonthlyPaymentsAsync(
        NpgsqlConnection connection, int yearStart, int yearEnd)
    {
        const string sql = @"
SELECT
    c.id_student,
    COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) AS pay_month,
    SUM(p.amount)::numeric(12,2) AS amount
FROM public.payment p
JOIN public.contract c ON c.id_contract = p.id_contract
WHERE p.payment_date IS NOT NULL
  AND (
        (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @year_start
         AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 8 AND 12)
     OR (COALESCE(p.period_year, EXTRACT(YEAR FROM p.payment_date)::int) = @year_end
         AND COALESCE(p.period_month, EXTRACT(MONTH FROM p.payment_date)::int) BETWEEN 1 AND 5)
      )
GROUP BY c.id_student, pay_month;";

        var map = new Dictionary<int, decimal[]>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("year_start", NpgsqlDbType.Integer).Value = yearStart;
        cmd.Parameters.Add("year_end", NpgsqlDbType.Integer).Value = yearEnd;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var studentId = reader.GetInt32(0);
            var month = reader.GetInt32(1);
            var amount = reader.GetDecimal(2);
            if (!map.TryGetValue(studentId, out var arr))
            {
                arr = new decimal[10];
                map[studentId] = arr;
            }

            var idx = Array.IndexOf(AcademicMonths, month);
            if (idx >= 0)
                arr[idx] += amount;
        }

        return map;
    }

    private static async Task<List<GroupStudentReportRow>> LoadGroupStudentsAsync(
        NpgsqlConnection connection,
        int idGroup,
        IReadOnlyDictionary<int, decimal[]> paymentsByStudent)
    {
        const string sql = @"
SELECT
    s.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS full_name,
    c.discount_percent,
    c.total_amount,
    s.email,
    s.residence_address,
    s.birth_date,
    s.birth_certificate,
    s.arrival_date,
    gm.expulsion_date,
    DATE_PART('year', AGE(CURRENT_DATE, s.birth_date))::int AS full_years,
    COALESCE(ei.institution_name, '') AS institution_name,
    COALESCE(s.school_number, '') AS school_number,
    COALESCE(s.school_shift::text, '') AS school_shift,
    COALESCE(st.type_name, '') AS student_type,
    COALESCE(mother.full_name, '') AS mother_name,
    COALESCE(mother.phone, '') AS mother_phone,
    COALESCE(father.full_name, '') AS father_name,
    COALESCE(father.phone, '') AS father_phone,
    COALESCE(payer.full_name, '') AS payer_name,
    COALESCE(src.source_name, '') AS source_name
FROM public.group_membership gm
JOIN public.student s ON s.id_student = gm.id_student
LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
LEFT JOIN public.educational_institution ei ON ei.id_institution = s.id_institution
LEFT JOIN LATERAL (
    SELECT c.discount_percent, c.total_amount
    FROM public.contract c
    WHERE c.id_student = s.id_student AND c.id_group = gm.id_group
      AND c.status NOT IN ('расторгнут', 'аннулирован')
    ORDER BY c.contract_date DESC
    LIMIT 1
) c ON TRUE
LEFT JOIN LATERAL (
    SELECT TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name, r.phone
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    JOIN public.relation_type rt ON rt.id_relation_type = sr.id_relation_type
    WHERE sr.id_student = s.id_student
      AND (LOWER(rt.relation_name) LIKE '%мать%' OR LOWER(rt.relation_name) LIKE '%мама%')
    ORDER BY sr.is_main_payer DESC
    LIMIT 1
) mother ON TRUE
LEFT JOIN LATERAL (
    SELECT TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name, r.phone
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    JOIN public.relation_type rt ON rt.id_relation_type = sr.id_relation_type
    WHERE sr.id_student = s.id_student
      AND (LOWER(rt.relation_name) LIKE '%отец%' OR LOWER(rt.relation_name) LIKE '%папа%')
    ORDER BY sr.is_main_payer DESC
    LIMIT 1
) father ON TRUE
LEFT JOIN LATERAL (
    SELECT TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    WHERE sr.id_student = s.id_student AND sr.is_main_payer = TRUE
    LIMIT 1
) payer ON TRUE
LEFT JOIN LATERAL (
    SELECT so.source_name
    FROM public.lead_request lr
    LEFT JOIN public.source_of_arrival so ON so.id_source = lr.id_source
    WHERE LOWER(TRIM(COALESCE(lr.child_last_name, '') || ' ' || lr.child_first_name))
          = LOWER(TRIM(s.last_name || ' ' || s.first_name))
    ORDER BY lr.created_date DESC
    LIMIT 1
) src ON TRUE
WHERE gm.id_group = @id_group
  AND gm.expulsion_date IS NULL
ORDER BY s.last_name, s.first_name, s.middle_name;";

        var rows = new List<GroupStudentReportRow>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id_group", NpgsqlDbType.Integer).Value = idGroup;
        await using var reader = await cmd.ExecuteReaderAsync();
        var rowNum = 0;
        while (await reader.ReadAsync())
        {
            rowNum++;
            var studentId = reader.GetInt32(0);
            var schoolNumber = reader.GetString(12);
            var schoolShift = reader.GetString(13);
            var institution = reader.GetString(11);
            var schoolInfo = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(institution) ? null : institution,
                string.IsNullOrWhiteSpace(schoolNumber) ? null : "№" + schoolNumber,
                string.IsNullOrWhiteSpace(schoolShift) ? null : "смена " + schoolShift
            }.Where(x => x != null));

            rows.Add(new GroupStudentReportRow
            {
                RowNumber = rowNum,
                FullName = reader.GetString(1),
                DiscountPercent = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                ContractAmount = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                Email = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ResidenceAddress = reader.IsDBNull(5) ? "" : reader.GetString(5),
                BirthDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                BirthCertificate = reader.IsDBNull(7) ? "" : reader.GetString(7),
                ArrivalDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                DepartureDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                FullYears = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                SchoolInfo = schoolInfo,
                AgeGroup = reader.GetString(14),
                MotherName = reader.GetString(15),
                MotherPhone = reader.GetString(16),
                FatherName = reader.GetString(17),
                FatherPhone = reader.GetString(18),
                PayerName = reader.GetString(19),
                SourceName = reader.GetString(20),
                MonthlyPayments = paymentsByStudent.TryGetValue(studentId, out var pays)
                    ? (decimal[])pays.Clone()
                    : new decimal[10]
            });
        }

        return rows;
    }

    private static async Task<List<CampStudentRow>> GetCampStudentsAsync(
        NpgsqlConnection connection, DateTime start, DateTime end)
    {
        const string sql = @"
WITH camp_students AS (
    SELECT DISTINCT s.id_student
    FROM public.student s
    LEFT JOIN public.group_membership gm ON gm.id_student = s.id_student AND gm.expulsion_date IS NULL
    LEFT JOIN public.study_group g ON g.id_group = gm.id_group
    LEFT JOIN public.direction d ON d.id_direction = g.id_direction
    LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
    WHERE s.arrival_date::date BETWEEN @start AND @end
      AND (
            d.direction_name ILIKE '%лагер%'
         OR st.type_name ILIKE '%лагер%'
         OR COALESCE(s.comment, '') ILIKE '%лагер%'
      )
)
SELECT
    COALESCE(src.source_name, 'Не указан') AS source_name,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS full_name,
    COALESCE(d.direction_name, st.type_name, 'лагерь') AS shift_info,
    COALESCE(s.phone, '') AS phone,
    COALESCE(rep1.full_name, '') AS rep1_name,
    COALESCE(rep1.phone, '') AS rep1_phone,
    COALESCE(rep2.full_name, '') AS rep2_name,
    COALESCE(rep2.phone, '') AS rep2_phone,
    COALESCE(s.comment, '') AS comment,
    COALESCE(s.birth_certificate, '') AS birth_certificate,
    s.birth_date
FROM camp_students cs
JOIN public.student s ON s.id_student = cs.id_student
LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
LEFT JOIN LATERAL (
    SELECT d.direction_name
    FROM public.group_membership gm
    JOIN public.study_group g ON g.id_group = gm.id_group
    JOIN public.direction d ON d.id_direction = g.id_direction
    WHERE gm.id_student = s.id_student AND gm.expulsion_date IS NULL
    ORDER BY gm.enrollment_date DESC
    LIMIT 1
) d ON TRUE
LEFT JOIN LATERAL (
    SELECT so.source_name
    FROM public.lead_request lr
    LEFT JOIN public.source_of_arrival so ON so.id_source = lr.id_source
    WHERE LOWER(TRIM(COALESCE(lr.child_last_name, '') || ' ' || lr.child_first_name))
          = LOWER(TRIM(s.last_name || ' ' || s.first_name))
    ORDER BY lr.created_date DESC
    LIMIT 1
) src ON TRUE
LEFT JOIN LATERAL (
    SELECT TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name, r.phone
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    WHERE sr.id_student = s.id_student
    ORDER BY sr.is_main_payer DESC, sr.id_student_representative
    LIMIT 1
) rep1 ON TRUE
LEFT JOIN LATERAL (
    SELECT TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name, r.phone
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    WHERE sr.id_student = s.id_student
    ORDER BY sr.is_main_payer DESC, sr.id_student_representative
    OFFSET 1 LIMIT 1
) rep2 ON TRUE
ORDER BY source_name, s.last_name, s.first_name;";

        var result = new List<CampStudentRow>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("start", NpgsqlDbType.Date).Value = start;
        cmd.Parameters.Add("end", NpgsqlDbType.Date).Value = end;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new CampStudentRow
            {
                SourceName = reader.GetString(0),
                FullName = reader.GetString(1),
                ShiftInfo = reader.GetString(2),
                Phone = reader.GetString(3),
                Representative1Name = reader.GetString(4),
                Representative1Phone = reader.GetString(5),
                Representative2Name = reader.GetString(6),
                Representative2Phone = reader.GetString(7),
                Comment = reader.GetString(8),
                BirthCertificate = reader.GetString(9),
                BirthDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            });
        }

        return result;
    }

    public static int ResolveDefaultAcademicYearStart()
    {
        var today = DateTime.Today;
        return today.Month >= 8 ? today.Year : today.Year - 1;
    }
}
