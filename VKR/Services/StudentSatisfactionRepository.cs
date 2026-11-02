#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class StudentSatisfactionRepository
{
    public async Task<int> CountDueSurveysAsync()
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(CountDueSql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<SatisfactionDueItem>> GetDueSurveysAsync()
    {
        var list = new List<SatisfactionDueItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(ListDueSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SatisfactionDueItem
            {
                IdStudent = ReadInt(reader, "id_student"),
                IdContract = ReadInt(reader, "id_contract"),
                IdDirection = ReadInt(reader, "id_direction"),
                StudentName = ReadString(reader, "student_name"),
                ContractNumber = ReadString(reader, "contract_number"),
                DirectionName = ReadString(reader, "direction_name"),
                ContractStart = ReadDateTime(reader, "contract_start"),
                DueDate = ReadDateTime(reader, "due_date"),
                PeriodNumber = ReadInt(reader, "period_number"),
                DaysOverdue = ReadInt(reader, "days_overdue"),
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<SatisfactionLevelRef>> GetActiveLevelsAsync()
    {
        var list = new List<SatisfactionLevelRef>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
SELECT id_satisfaction_level, level_name, level_code, is_positive
FROM public.satisfaction_level
WHERE is_active = TRUE
ORDER BY sort_order, level_name;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SatisfactionLevelRef
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Code = reader.GetString(2),
                IsPositive = reader.GetBoolean(3),
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<DissatisfactionReasonRef>> GetActiveReasonsAsync()
    {
        var list = new List<DissatisfactionReasonRef>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
SELECT id_reason, reason_name
FROM public.dissatisfaction_reason
WHERE is_active = TRUE
ORDER BY sort_order, reason_name;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DissatisfactionReasonRef
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
            });
        }
        return list;
    }

    /// <summary>Направления из заявки ребёнка, кроме текущего по договору.</summary>
    public async Task<IReadOnlyList<DirectionRef>> GetAlternateDirectionsAsync(int idStudent, int excludeDirectionId)
    {
        var list = new List<DirectionRef>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(AlternateDirectionsSql, connection);
        cmd.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        cmd.Parameters.Add("exclude_dir", NpgsqlDbType.Integer).Value = excludeDirectionId;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DirectionRef
            {
                IdDirection = reader.GetInt32(0),
                Name = reader.GetString(1),
            });
        }
        return list;
    }

    public async Task SaveSurveyAsync(SatisfactionSurveyInput input)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
INSERT INTO public.student_satisfaction_survey (
    id_student, id_contract, id_direction, due_date, period_number,
    id_satisfaction_level, id_dissatisfaction_reason, suggested_direction_id,
    comment, completed_at, id_completed_by)
VALUES (
    @id_student, @id_contract, @id_direction, @due_date, @period_number,
    @id_level, @id_reason, @suggested_dir,
    @comment, CURRENT_TIMESTAMP, @id_user)
ON CONFLICT (id_contract, period_number)
DO UPDATE SET
    id_satisfaction_level     = EXCLUDED.id_satisfaction_level,
    id_dissatisfaction_reason = EXCLUDED.id_dissatisfaction_reason,
    suggested_direction_id    = EXCLUDED.suggested_direction_id,
    comment                   = EXCLUDED.comment,
    completed_at              = EXCLUDED.completed_at,
    id_completed_by           = EXCLUDED.id_completed_by;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = input.IdStudent;
        cmd.Parameters.Add("id_contract", NpgsqlDbType.Integer).Value = input.IdContract;
        cmd.Parameters.Add("id_direction", NpgsqlDbType.Integer).Value = input.IdDirection;
        cmd.Parameters.Add("due_date", NpgsqlDbType.Date).Value = input.DueDate.Date;
        cmd.Parameters.Add("period_number", NpgsqlDbType.Integer).Value = input.PeriodNumber;
        cmd.Parameters.Add("id_level", NpgsqlDbType.Integer).Value = input.IdSatisfactionLevel;
        cmd.Parameters.Add("id_reason", NpgsqlDbType.Integer).Value =
            input.IdDissatisfactionReason.HasValue ? input.IdDissatisfactionReason.Value : DBNull.Value;
        cmd.Parameters.Add("suggested_dir", NpgsqlDbType.Integer).Value =
            input.SuggestedDirectionId.HasValue ? input.SuggestedDirectionId.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(input.Comment) ? DBNull.Value : input.Comment.Trim();
        cmd.Parameters.Add("id_user", NpgsqlDbType.Integer).Value =
            input.IdCompletedBy.HasValue ? input.IdCompletedBy.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<SatisfactionStatsSummary> GetStatsSummaryAsync(DateTime fromDate, DateTime toDate)
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(SummarySql, connection);
        cmd.Parameters.Add("from_date", NpgsqlDbType.Date).Value = fromDate.Date;
        cmd.Parameters.Add("to_date", NpgsqlDbType.Date).Value = toDate.Date;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new SatisfactionStatsSummary();

        return new SatisfactionStatsSummary
        {
            Total = ReadInt(reader, "total"),
            PositiveCount = ReadInt(reader, "positive_count"),
            NegativeCount = ReadInt(reader, "negative_count"),
            NeutralCount = ReadInt(reader, "neutral_count"),
        };
    }

    public async Task<IReadOnlyList<SatisfactionHistoryItem>> GetHistoryAsync(DateTime fromDate, DateTime toDate)
    {
        NotificationSchema.Ensure();
        var list = new List<SatisfactionHistoryItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(HistorySql, connection);
        cmd.Parameters.Add("from_date", NpgsqlDbType.Date).Value = fromDate.Date;
        cmd.Parameters.Add("to_date", NpgsqlDbType.Date).Value = toDate.Date;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SatisfactionHistoryItem
            {
                StudentName = ReadString(reader, "student_name"),
                ContractNumber = ReadString(reader, "contract_number"),
                DirectionName = ReadString(reader, "direction_name"),
                PeriodNumber = ReadInt(reader, "period_number"),
                LevelName = ReadString(reader, "level_name"),
                IsPositive = ReadBool(reader, "is_positive"),
                ReasonName = ReadString(reader, "reason_name"),
                SuggestedDirectionName = ReadString(reader, "suggested_direction_name"),
                Comment = ReadString(reader, "comment"),
                CompletedAt = ReadDateTime(reader, "completed_at"),
                CompletedByName = ReadString(reader, "completed_by_name"),
            });
        }
        return list;
    }

    private const string SummarySql = @"
SELECT
    COUNT(*)::int AS total,
    COUNT(*) FILTER (WHERE sl.is_positive = TRUE)::int AS positive_count,
    COUNT(*) FILTER (WHERE sl.is_positive = FALSE AND COALESCE(sl.level_code, '') <> 'neutral')::int AS negative_count,
    COUNT(*) FILTER (WHERE COALESCE(sl.level_code, '') = 'neutral' OR ss.id_satisfaction_level IS NULL)::int AS neutral_count
FROM public.student_satisfaction_survey ss
LEFT JOIN public.satisfaction_level sl ON sl.id_satisfaction_level = ss.id_satisfaction_level
WHERE ss.completed_at IS NOT NULL
  AND ss.completed_at::date >= @from_date
  AND ss.completed_at::date <= @to_date;";

    private const string HistorySql = @"
SELECT
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_name,
    c.contract_number,
    d.direction_name,
    ss.period_number,
    COALESCE(sl.level_name, '—') AS level_name,
    COALESCE(sl.is_positive, FALSE) AS is_positive,
    COALESCE(dr.reason_name, '') AS reason_name,
    COALESCE(sd.direction_name, '') AS suggested_direction_name,
    COALESCE(ss.comment, '') AS comment,
    ss.completed_at,
    COALESCE(TRIM(u.last_name || ' ' || u.first_name), '') AS completed_by_name
FROM public.student_satisfaction_survey ss
INNER JOIN public.student s ON s.id_student = ss.id_student
INNER JOIN public.contract c ON c.id_contract = ss.id_contract
INNER JOIN public.direction d ON d.id_direction = ss.id_direction
LEFT JOIN public.satisfaction_level sl ON sl.id_satisfaction_level = ss.id_satisfaction_level
LEFT JOIN public.dissatisfaction_reason dr ON dr.id_reason = ss.id_dissatisfaction_reason
LEFT JOIN public.direction sd ON sd.id_direction = ss.suggested_direction_id
LEFT JOIN public.app_user u ON u.id_user = ss.id_completed_by
WHERE ss.completed_at IS NOT NULL
  AND ss.completed_at::date >= @from_date
  AND ss.completed_at::date <= @to_date
ORDER BY ss.completed_at DESC, ss.id_survey DESC;";

    private const string CountDueSql = @"
SELECT COUNT(*)::int
FROM (" + DuePeriodsCte + @") dp;";

    private const string ListDueSql = @"
SELECT
    dp.id_student,
    dp.id_contract,
    dp.id_direction,
    dp.student_name,
    dp.contract_number,
    dp.direction_name,
    dp.contract_start,
    dp.due_date,
    dp.period_number,
    (CURRENT_DATE - dp.due_date)::int AS days_overdue
FROM (" + DuePeriodsCte + @") dp
ORDER BY dp.due_date, dp.student_name;";

    private const string DuePeriodsCte = @"
WITH active_contracts AS (
    SELECT
        c.id_contract,
        c.id_student,
        c.id_direction,
        c.contract_number,
        COALESCE(c.start_date, c.contract_date) AS contract_start,
        TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_name,
        d.direction_name
    FROM public.contract c
    INNER JOIN public.student s ON s.id_student = c.id_student
    INNER JOIN public.direction d ON d.id_direction = c.id_direction
    WHERE c.status = 'активен'
      AND s.status = 'активный'
      AND (c.end_date IS NULL OR c.end_date >= CURRENT_DATE)
      AND COALESCE(c.start_date, c.contract_date) <= CURRENT_DATE
),
due_periods AS (
    SELECT
        ac.id_contract,
        ac.id_student,
        ac.id_direction,
        ac.contract_number,
        ac.contract_start,
        ac.student_name,
        ac.direction_name,
        gs.n AS period_number,
        (ac.contract_start + (gs.n || ' months')::interval)::date AS due_date
    FROM active_contracts ac
    CROSS JOIN generate_series(1, 120) AS gs(n)
    WHERE (ac.contract_start + (gs.n || ' months')::interval)::date <= CURRENT_DATE
)
SELECT dp.*
FROM due_periods dp
WHERE NOT EXISTS (
    SELECT 1
    FROM public.student_satisfaction_survey ss
    WHERE ss.id_contract = dp.id_contract
      AND ss.period_number = dp.period_number
      AND ss.completed_at IS NOT NULL
)
AND NOT EXISTS (
    SELECT 1 FROM public.notification_snooze ns
    WHERE ns.task_key = 'sat:' || dp.id_contract || ':' || dp.period_number
      AND ns.snoozed_until >= CURRENT_DATE
)";

    private const string AlternateDirectionsSql = @"
SELECT DISTINCT d.id_direction, d.direction_name
FROM public.student s
LEFT JOIN public.lead_request lr_direct ON lr_direct.id_lead = s.id_source_lead
LEFT JOIN public.lead_request lr_match ON (
        lr_match.id_lead IS DISTINCT FROM lr_direct.id_lead
    AND lower(trim(s.first_name)) = lower(trim(lr_match.child_first_name))
    AND (
        s.last_name IS NULL OR lr_match.child_last_name IS NULL
        OR lower(trim(s.last_name)) = lower(trim(lr_match.child_last_name))
    )
    AND (
        s.birth_date IS NULL OR lr_match.child_birth_date IS NULL
        OR s.birth_date = lr_match.child_birth_date
    )
)
JOIN public.lead_request_direction lrd ON lrd.id_lead = COALESCE(lr_direct.id_lead, lr_match.id_lead)
JOIN public.direction d ON d.id_direction = lrd.id_direction
WHERE s.id_student = @id_student
  AND d.id_direction <> @exclude_dir
  AND d.is_active = TRUE
ORDER BY d.direction_name;";

    private static int ReadInt(IDataRecord r, string n) => r.GetInt32(r.GetOrdinal(n));
    private static string ReadString(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? string.Empty : r.GetString(i);
    }
    private static DateTime ReadDateTime(IDataRecord r, string n)
    {
        var v = r.GetValue(r.GetOrdinal(n));
        return v switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(v),
        };
    }

    private static bool ReadBool(IDataRecord r, string n) => r.GetBoolean(r.GetOrdinal(n));
}
