#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class AiAnalyticsRepository
{
    private const decimal MediumThreshold = 0.35m;
    private const decimal HighThreshold = 0.70m;

    // Веса факторов риска оттока: Pотт = w1*x1 + ... + w6*x6.
    private const decimal WAttendanceRisk = 0.25m;
    private const decimal WDebtRatio = 0.18m;
    private const decimal WOverdueDays = 0.18m;
    private const decimal WContractEnding = 0.12m;
    private const decimal WNegativeEvents = 0.12m;
    private const decimal WLastContact = 0.15m;

    // Веса конверсии заявки: Pконв = v1*y1 + ... + v5*y5.
    private const decimal VSource = 0.20m;
    private const decimal VDirection = 0.15m;
    private const decimal VTrial = 0.30m;
    private const decimal VProcessing = 0.20m;
    private const decimal VContact = 0.15m;

    public async Task EnsureRecommendationStatusTableAsync()
    {
        AiAnalyticsSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await EnsureRecommendationStatusTableAsync(connection);
    }

    public async Task CalculateAndSaveChurnPredictionsAsync()
    {
        await EnsureRecommendationStatusTableAsync();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var students = await LoadStudentSnapshotsAsync(connection);
        var churnRows = new List<(int IdStudent, decimal Probability, string RiskLevel, string Factors, int Priority, string Recommendation)>();

        foreach (var student in students)
        {
            var scoring = ScoreChurn(student);
            var factorsText = BuildFactorsText(scoring);
            churnRows.Add((
                student.IdStudent,
                scoring.Probability,
                scoring.RiskLevel,
                factorsText,
                scoring.Priority,
                scoring.Recommendation));
        }

        await using var tx = await connection.BeginTransactionAsync();
        await using (var deleteCmd = new NpgsqlCommand("DELETE FROM public.prognoz_ottoka;", connection, tx))
            await deleteCmd.ExecuteNonQueryAsync();

        if (churnRows.Count > 0)
            await InsertChurnPredictionsBatchAsync(connection, tx, churnRows);

        await SaveMetadataAsync(connection, tx, "churn", churnRows.Count);
        await tx.CommitAsync();
    }

    public async Task CalculateAndSaveLeadConversionPredictionsAsync()
    {
        await EnsureRecommendationStatusTableAsync();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var leads = await LoadLeadSnapshotsAsync(connection);
        var conversionRows = new List<(int IdLead, decimal Probability, string Level, string Factors, string Recommendation)>();

        foreach (var lead in leads)
        {
            var scoring = ScoreLead(lead);
            conversionRows.Add((
                lead.IdLead,
                scoring.Probability,
                scoring.RiskLevel,
                BuildFactorsText(scoring),
                scoring.Recommendation));
        }

        await using var tx = await connection.BeginTransactionAsync();
        await using (var deleteCmd = new NpgsqlCommand("DELETE FROM public.prognoz_konversii_zayavok;", connection, tx))
            await deleteCmd.ExecuteNonQueryAsync();

        if (conversionRows.Count > 0)
            await InsertLeadPredictionsBatchAsync(connection, tx, conversionRows);

        await SaveMetadataAsync(connection, tx, "conversion", conversionRows.Count);
        await tx.CommitAsync();
    }

    public async Task<IReadOnlyList<ChurnPrediction>> GetChurnPredictionsAsync()
    {
        AiAnalyticsSchema.Ensure();
        await EnsureRecommendationStatusTableAsync();
        var result = new List<ChurnPrediction>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT DISTINCT ON (po.id_student)
    po.id_prognoza_ottoka,
    po.id_student,
    TRIM(COALESCE(s.last_name, '') || ' ' || COALESCE(s.first_name, '') || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
    COALESCE(s.phone, '') AS student_phone,
    po.veroyatnost_ottoka,
    po.uroven_riska,
    COALESCE(po.faktor_prognoza, '') AS faktor_prognoza,
    COALESCE(po.prioritet_obrabotki, 5) AS prioritet_obrabotki,
    COALESCE(po.rekomenduemoe_deystvie, '') AS rekomenduemoe_deystvie,
    po.data_rascheta
FROM public.prognoz_ottoka po
JOIN public.student s ON s.id_student = po.id_student
ORDER BY po.id_student, po.data_rascheta DESC, po.id_prognoza_ottoka DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ChurnPrediction
            {
                IdChurnPrediction = reader.GetInt32(0),
                IdStudent = reader.GetInt32(1),
                StudentFullName = reader.GetString(2),
                StudentPhone = reader.GetString(3),
                ChurnProbability = reader.GetDecimal(4),
                RiskLevel = reader.GetString(5),
                ChurnFactors = reader.GetString(6),
                Priority = reader.GetInt32(7),
                RecommendedAction = reader.GetString(8),
                DateCalculated = reader.GetDateTime(9)
            });
        }

        return result
            .OrderByDescending(p => p.ChurnProbability)
            .ThenBy(p => p.StudentFullName)
            .ToList();
    }

    public async Task<IReadOnlyList<LeadConversionPrediction>> GetLeadConversionPredictionsAsync()
    {
        AiAnalyticsSchema.Ensure();
        await EnsureRecommendationStatusTableAsync();
        var result = new List<LeadConversionPrediction>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT DISTINCT ON (pk.id_lead)
    pk.id_prognoza_konversii,
    pk.id_lead,
    TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')) AS lead_full_name,
    COALESCE(lr.contact_phone, '') AS lead_phone,
    pk.veroyatnost_konversii,
    pk.uroven_veroyatnosti,
    COALESCE(pk.faktor_prognoza, '') AS faktor_prognoza,
    COALESCE(pk.rekomenduemoe_deystvie, '') AS rekomenduemoe_deystvie,
    pk.data_rascheta
FROM public.prognoz_konversii_zayavok pk
JOIN public.lead_request lr ON lr.id_lead = pk.id_lead
ORDER BY pk.id_lead, pk.data_rascheta DESC, pk.id_prognoza_konversii DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LeadConversionPrediction
            {
                IdPrediction = reader.GetInt32(0),
                IdLead = reader.GetInt32(1),
                LeadFullName = reader.GetString(2),
                LeadPhone = reader.GetString(3),
                ConversionProbability = reader.GetDecimal(4),
                ProbabilityLevel = reader.GetString(5),
                ConversionFactors = reader.GetString(6),
                RecommendedAction = reader.GetString(7),
                DateCalculated = reader.GetDateTime(8)
            });
        }

        return result
            .OrderByDescending(p => p.ConversionProbability)
            .ThenBy(p => p.LeadFullName)
            .ToList();
    }

    public async Task<AiAnalyticsSummary> GetAnalyticsSummaryAsync()
    {
        AiAnalyticsSchema.Ensure();
        await EnsureRecommendationStatusTableAsync();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    (SELECT COUNT(*)::int FROM public.student WHERE status = 'активный') AS active_students,
    (SELECT COUNT(*)::int FROM public.student WHERE status = 'архивный') AS archived_students,
    (SELECT COUNT(*)::int FROM public.student
      WHERE status = 'активный'
        AND arrival_date >= DATE_TRUNC('month', CURRENT_DATE)::date) AS new_students,
    (SELECT COUNT(DISTINCT id_student) FROM (
        SELECT se.id_student
          FROM public.student_event se
          JOIN public.event_type et ON et.id_event_type = se.id_event_type
         WHERE se.event_date >= DATE_TRUNC('month', CURRENT_DATE)::date
           AND (LOWER(et.event_name) LIKE '%уход%'
                OR LOWER(et.event_name) LIKE '%отчисл%'
                OR LOWER(et.event_name) LIKE '%растор%'
                OR LOWER(et.event_name) LIKE '%прекращ%')
        UNION
        SELECT c.id_student
          FROM public.contract c
         WHERE c.status IN ('расторгнут', 'завершен')
           AND COALESCE(c.end_date, c.contract_date) >= DATE_TRUNC('month', CURRENT_DATE)::date
        UNION
        SELECT c.id_student
          FROM public.contract_termination ct
          JOIN public.contract c ON c.id_contract = ct.id_contract
         WHERE ct.termination_date >= DATE_TRUNC('month', CURRENT_DATE)::date
    ) AS churned)::int AS left_students,
    (SELECT CASE WHEN COUNT(*) = 0 THEN 0
            ELSE ROUND(SUM(CASE WHEN status = 'присутствовал' THEN 1 ELSE 0 END)::numeric / COUNT(*), 4)
        END
       FROM public.attendance
      WHERE lesson_date >= CURRENT_DATE - INTERVAL '30 days') AS attendance_rate,
    (SELECT COUNT(*)::int FROM public.lead_request
      WHERE status NOT IN ('конвертирована', 'отказ')) AS active_leads,
    (SELECT COALESCE(SUM(debt_amount - paid_amount), 0)
       FROM public.debt
      WHERE status IN ('активна', 'частично_погашена')) AS total_debt,
    (SELECT COUNT(DISTINCT id_student)::int
       FROM public.debt
      WHERE status IN ('активна', 'частично_погашена')
        AND debt_amount > paid_amount) AS debtors_count,
    (SELECT MAX(last_calculated_at) FROM public.ai_analytics_metadata) AS last_calc;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new AiAnalyticsSummary();

        var activeStudents = reader.GetInt32(0);
        var newStudents = reader.GetInt32(2);
        var leftStudents = reader.GetInt32(3);
        var activeAtStart = Math.Max(1, activeStudents + leftStudents - newStudents);

        var summary = new AiAnalyticsSummary
        {
            ActiveStudentsCount = activeStudents,
            ArchivedStudentsCount = reader.GetInt32(1),
            NewStudentsInPeriod = newStudents,
            LeftStudentsInPeriod = leftStudents,
            ChurnRate = leftStudents / (decimal)activeAtStart,
            ClientGrowthRate = (newStudents - leftStudents) / (decimal)activeAtStart,
            AttendanceRate = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
            ActiveLeadsCount = reader.GetInt32(5),
            TotalDebt = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
            DebtorsCount = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            LastAnalyticsCalculatedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
        };

        await reader.CloseAsync();
        await FillRiskDistributionAsync(connection, summary);
        await FillConversionDistributionAsync(connection, summary);
        await FillDebtRatioAsync(connection, summary);
        await FillActiveRecommendationsCountAsync(connection, summary);

        return summary;
    }

    public async Task<IReadOnlyList<DirectionAnalyticsItem>> GetDirectionAnalyticsAsync()
    {
        AiAnalyticsSchema.Ensure();
        await EnsureRecommendationStatusTableAsync();
        var result = new List<DirectionAnalyticsItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    d.direction_name,
    COUNT(DISTINCT CASE WHEN s.status = 'активный' THEN s.id_student END)::int AS active_students,
    COUNT(DISTINCT CASE WHEN latest_churn.uroven_riska = 'высокий' THEN s.id_student END)::int AS high_risk,
    COALESCE(AVG(latest_churn.veroyatnost_ottoka), 0) AS avg_churn,
    COUNT(DISTINCT CASE WHEN lr.status NOT IN ('конвертирована', 'отказ') THEN lr.id_lead END)::int AS active_leads,
    COALESCE(AVG(latest_conv.veroyatnost_konversii), 0) AS avg_conversion,
    COALESCE(SUM(CASE WHEN debt.status IN ('активна', 'частично_погашена')
        THEN GREATEST(debt.debt_amount - debt.paid_amount, 0) ELSE 0 END), 0) AS total_debt
FROM public.direction d
LEFT JOIN public.study_group g ON g.id_direction = d.id_direction AND g.is_active = true
LEFT JOIN public.group_membership gm ON gm.id_group = g.id_group AND gm.expulsion_date IS NULL
LEFT JOIN public.student s ON s.id_student = gm.id_student
LEFT JOIN LATERAL (
    SELECT po.veroyatnost_ottoka, po.uroven_riska
    FROM public.prognoz_ottoka po
    WHERE po.id_student = s.id_student
    ORDER BY po.data_rascheta DESC, po.id_prognoza_ottoka DESC
    LIMIT 1
) latest_churn ON TRUE
LEFT JOIN public.lead_request lr ON lr.id_direction = d.id_direction
LEFT JOIN LATERAL (
    SELECT pk.veroyatnost_konversii
    FROM public.prognoz_konversii_zayavok pk
    WHERE pk.id_lead = lr.id_lead
    ORDER BY pk.data_rascheta DESC, pk.id_prognoza_konversii DESC
    LIMIT 1
) latest_conv ON TRUE
LEFT JOIN public.contract c ON c.id_student = s.id_student AND c.status = 'активен'
LEFT JOIN public.debt debt ON debt.id_contract = c.id_contract
GROUP BY d.direction_name
ORDER BY d.direction_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DirectionAnalyticsItem
            {
                DirectionName = reader.GetString(0),
                ActiveStudentsCount = reader.GetInt32(1),
                HighRiskCount = reader.GetInt32(2),
                AverageChurnProbability = reader.GetDecimal(3),
                ActiveLeadsCount = reader.GetInt32(4),
                AverageConversionProbability = reader.GetDecimal(5),
                TotalDebt = reader.GetDecimal(6)
            });
        }

        return result;
    }

    public async Task<Dictionary<(string EntityType, int EntityId, string Text), bool>> GetRecommendationCompletionMapAsync()
    {
        await EnsureRecommendationStatusTableAsync();
        var map = new Dictionary<(string, int, string), bool>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT entity_type, entity_id, recommendation_text, is_completed
FROM public.ai_recommendation_status;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[(reader.GetString(0), reader.GetInt32(1), reader.GetString(2))] = reader.GetBoolean(3);
        }

        return map;
    }

    public async Task SetRecommendationCompletedAsync(
        string entityType,
        int entityId,
        string recommendationText,
        bool isCompleted,
        string? ruleCode = null,
        string? recommendationType = null,
        string? entityDisplayName = null,
        string? triggeredSigns = null,
        int? completedByUserId = null,
        string? completedByName = null)
    {
        await EnsureRecommendationStatusTableAsync();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.ai_recommendation_status
    (entity_type, entity_id, recommendation_text, is_completed, completed_at,
     rule_code, recommendation_type, entity_display_name, triggered_signs,
     completed_by_user_id, completed_by_name)
VALUES
    (@type, @id, @text, @done,
     CASE WHEN @done THEN CURRENT_TIMESTAMP ELSE NULL END,
     @rule, @rec_type, @entity_name, @signs, @user_id, @user_name)
ON CONFLICT (entity_type, entity_id, recommendation_text)
DO UPDATE SET
    is_completed = EXCLUDED.is_completed,
    completed_at = CASE WHEN EXCLUDED.is_completed THEN CURRENT_TIMESTAMP ELSE NULL END,
    rule_code = COALESCE(EXCLUDED.rule_code, public.ai_recommendation_status.rule_code),
    recommendation_type = COALESCE(EXCLUDED.recommendation_type, public.ai_recommendation_status.recommendation_type),
    entity_display_name = COALESCE(EXCLUDED.entity_display_name, public.ai_recommendation_status.entity_display_name),
    triggered_signs = COALESCE(EXCLUDED.triggered_signs, public.ai_recommendation_status.triggered_signs),
    completed_by_user_id = CASE WHEN EXCLUDED.is_completed THEN EXCLUDED.completed_by_user_id ELSE NULL END,
    completed_by_name = CASE WHEN EXCLUDED.is_completed THEN EXCLUDED.completed_by_name ELSE NULL END;", connection);

        cmd.Parameters.AddWithValue("type", entityType);
        cmd.Parameters.AddWithValue("id", entityId);
        cmd.Parameters.AddWithValue("text", recommendationText);
        cmd.Parameters.AddWithValue("done", isCompleted);
        cmd.Parameters.AddWithValue("rule", (object?)ruleCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("rec_type", (object?)recommendationType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("entity_name", (object?)entityDisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("signs", (object?)triggeredSigns ?? DBNull.Value);
        cmd.Parameters.AddWithValue("user_id", (object?)completedByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("user_name", (object?)completedByName ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AiRecommendationHistoryItem>> GetRecommendationHistoryAsync(int limit = 50)
    {
        await EnsureRecommendationStatusTableAsync();
        var result = new List<AiRecommendationHistoryItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT entity_type,
       entity_id,
       COALESCE(entity_display_name, '') AS entity_display_name,
       recommendation_text,
       rule_code,
       COALESCE(recommendation_type, '') AS recommendation_type,
       triggered_signs,
       completed_at,
       completed_by_name
FROM public.ai_recommendation_status
WHERE is_completed = TRUE
ORDER BY completed_at DESC NULLS LAST
LIMIT @limit;", connection);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AiRecommendationHistoryItem
            {
                EntityType = reader.GetString(0),
                EntityId = reader.GetInt32(1),
                EntityDisplayName = reader.GetString(2),
                RecommendationText = reader.GetString(3),
                RuleCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                RecommendationType = reader.GetString(5),
                TriggeredSigns = reader.IsDBNull(6) ? null : reader.GetString(6),
                CompletedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                CompletedByName = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return result;
    }

    private static async Task<List<StudentSnapshot>> LoadStudentSnapshotsAsync(NpgsqlConnection connection)
    {
        const string sql = @"
SELECT
    s.id_student,
    COALESCE(s.arrival_date, s.created_at::date) AS arrival_date,
    COALESCE(att.total_count, 0) AS total_count,
    COALESCE(att.visited_count, 0) AS visited_count,
    COALESCE(att.missed_count, 0) AS missed_count,
    COALESCE(debt_summary.debt_amount, 0) AS debt_amount,
    COALESCE(debt_summary.max_overdue_days, 0) AS max_overdue_days,
    COALESCE(contract_summary.contract_total, 0) AS contract_total,
    contract_summary.contract_end_date,
    COALESCE(events.has_warning, FALSE) AS has_warning
FROM public.student s
LEFT JOIN LATERAL (
    SELECT
        COUNT(*)::int AS total_count,
        SUM(CASE WHEN a.status = 'присутствовал' THEN 1 ELSE 0 END)::int AS visited_count,
        SUM(CASE WHEN a.status IN ('отсутствовал', 'не был') THEN 1 ELSE 0 END)::int AS missed_count
    FROM public.attendance a
    WHERE a.id_student = s.id_student
      AND a.lesson_date >= CURRENT_DATE - INTERVAL '30 days'
) att ON TRUE
LEFT JOIN LATERAL (
    SELECT
        SUM(GREATEST(d.debt_amount - d.paid_amount, 0)) AS debt_amount,
        MAX(GREATEST((CURRENT_DATE - d.due_date)::int, 0)) AS max_overdue_days
    FROM public.debt d
    JOIN public.contract c ON c.id_contract = d.id_contract
    WHERE c.id_student = s.id_student
      AND d.status IN ('активна', 'частично_погашена')
) debt_summary ON TRUE
LEFT JOIN LATERAL (
    SELECT
        SUM(c.total_amount) AS contract_total,
        MAX(c.end_date) AS contract_end_date
    FROM public.contract c
    WHERE c.id_student = s.id_student
      AND c.status = 'активен'
) contract_summary ON TRUE
LEFT JOIN LATERAL (
    SELECT TRUE AS has_warning
    FROM public.student_event se
    JOIN public.event_type et ON et.id_event_type = se.id_event_type
    WHERE se.id_student = s.id_student
      AND se.event_date >= CURRENT_DATE - INTERVAL '60 days'
      AND (
            LOWER(et.event_name) LIKE '%приостанов%'
         OR LOWER(et.event_name) LIKE '%уход%'
         OR LOWER(et.event_name) LIKE '%снижен%'
         OR LOWER(COALESCE(se.comment, '')) LIKE '%снижен%'
      )
    LIMIT 1
) events ON TRUE
WHERE s.status = 'активный';";

        var students = new List<StudentSnapshot>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            students.Add(new StudentSnapshot
            {
                IdStudent = reader.GetInt32(0),
                ArrivalDate = reader.GetDateTime(1),
                TotalCount = reader.GetInt32(2),
                VisitedCount = reader.GetInt32(3),
                MissedCount = reader.GetInt32(4),
                DebtAmount = reader.GetDecimal(5),
                MaxOverdueDays = reader.GetInt32(6),
                ContractTotal = reader.GetDecimal(7),
                ContractEndDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                HasWarning = reader.GetBoolean(9)
            });
        }

        return students;
    }

    private static async Task<List<LeadSnapshot>> LoadLeadSnapshotsAsync(NpgsqlConnection connection)
    {
        const string sql = @"
SELECT
    lr.id_lead,
    lr.status,
    lr.created_date::date AS created_date,
    lr.id_source,
    lr.id_direction,
    COALESCE(d.is_active, TRUE) AS direction_active,
    COALESCE(lr.comment, '') AS lead_comment,
    COALESCE(src_rate.conversion_rate, CASE WHEN lr.id_source IS NULL THEN 0.30 ELSE 0.50 END) AS source_rate,
    COALESCE(dir_rate.conversion_rate, CASE WHEN COALESCE(d.is_active, TRUE) THEN 0.50 ELSE 0.10 END) AS direction_rate,
    trial.has_trial,
    COALESCE(trial.trial_result, '') AS trial_result,
    lr.status AS lead_status
FROM public.lead_request lr
JOIN public.direction d ON d.id_direction = lr.id_direction
LEFT JOIN LATERAL (
    SELECT CASE WHEN COUNT(*) = 0 THEN 0.50
                ELSE SUM(CASE WHEN l2.status = 'конвертирована' THEN 1 ELSE 0 END)::numeric / COUNT(*)
           END AS conversion_rate
    FROM public.lead_request l2
    WHERE l2.id_source = lr.id_source
) src_rate ON lr.id_source IS NOT NULL
LEFT JOIN LATERAL (
    SELECT CASE WHEN COUNT(*) = 0 THEN 0.50
                ELSE SUM(CASE WHEN l3.status = 'конвертирована' THEN 1 ELSE 0 END)::numeric / COUNT(*)
           END AS conversion_rate
    FROM public.lead_request l3
    WHERE l3.id_direction = lr.id_direction
) dir_rate ON TRUE
LEFT JOIN LATERAL (
    SELECT TRUE AS has_trial, COALESCE(tl.result, lr.status) AS trial_result
    FROM public.trial_lesson tl
    WHERE tl.id_lead = lr.id_lead
    ORDER BY tl.scheduled_at DESC
    LIMIT 1
) trial ON TRUE
WHERE lr.status NOT IN ('конвертирована', 'отказ');";

        var leads = new List<LeadSnapshot>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            leads.Add(new LeadSnapshot
            {
                IdLead = reader.GetInt32(0),
                Status = reader.GetString(1),
                CreatedDate = reader.GetDateTime(2),
                HasSource = !reader.IsDBNull(3),
                DirectionActive = reader.GetBoolean(5),
                LeadComment = reader.GetString(6),
                SourceConversionRate = reader.GetDecimal(7),
                DirectionConversionRate = reader.GetDecimal(8),
                HasTrial = !reader.IsDBNull(9) && reader.GetBoolean(9),
                TrialResultText = reader.IsDBNull(10) ? "" : reader.GetString(10),
                LeadStatus = reader.GetString(11)
            });
        }

        return leads;
    }

    private static ScoringResult ScoreChurn(StudentSnapshot s)
    {
        var hasNoAttendance = s.TotalCount == 0;
        var attendanceRate = hasNoAttendance ? 0.5m : s.VisitedCount / (decimal)s.TotalCount;

        var x1 = hasNoAttendance ? 0.5m : Clamp01(1m - attendanceRate);
        var x2 = s.ContractTotal > 0
            ? Clamp01(s.DebtAmount / s.ContractTotal)
            : (s.DebtAmount > 0 ? 1m : 0m);
        var x3 = Clamp01(s.MaxOverdueDays / 30m);
        var x4 = 0m;
        var x5 = s.HasWarning ? 1m : 0m;
        int? contractDaysLeft = null;

        if (s.ContractEndDate.HasValue)
        {
            contractDaysLeft = (s.ContractEndDate.Value.Date - DateTime.Today).Days;
            x4 = contractDaysLeft <= 0 ? 1m
                : contractDaysLeft <= 14
                    ? Clamp01((14m - contractDaysLeft.Value) / 14m)
                    : 0m;
        }

        var daysSinceArrival = Math.Max(0, (DateTime.Today - s.ArrivalDate.Date).Days);
        var x6 = GetLastContactRiskScore(null, s.ArrivalDate);

        var probability = Clamp01(
            WAttendanceRisk * x1 +
            WDebtRatio * x2 +
            WOverdueDays * x3 +
            WContractEnding * x4 +
            WNegativeEvents * x5 +
            WLastContact * x6);

        var riskLevel = GetLevel(probability);
        var expert = ExpertRecommendationEngine.EvaluateChurn(riskLevel, new StudentChurnFeatures
        {
            AttendanceRate = attendanceRate,
            AttendanceRecordsCount = s.TotalCount,
            HasNoAttendanceData = hasNoAttendance,
            MissedCount = s.MissedCount,
            DebtAmount = s.DebtAmount,
            MaxOverdueDays = s.MaxOverdueDays,
            ContractDaysLeft = contractDaysLeft,
            HasWarningEvent = s.HasWarning,
            DaysSinceLastContact = null,
            DaysSinceArrival = daysSinceArrival
        });

        return new ScoringResult(probability, expert.Signs, expert.Action, expert.Priority, riskLevel, expert.RuleCode, expert.IsActionable);
    }

    private static ScoringResult ScoreLead(LeadSnapshot lead)
    {
        var y1 = Clamp01(lead.SourceConversionRate);
        var y2 = Clamp01(lead.DirectionConversionRate);
        var y3 = GetTrialScore(lead);
        var daysSinceCreated = Math.Max(0, (DateTime.Today - lead.CreatedDate.Date).Days);
        var y4 = GetProcessingSpeedScore(null, daysSinceCreated);
        var y5 = GetContactResultScore(lead.LeadComment);

        var probability = Clamp01(
            VSource * y1 +
            VDirection * y2 +
            VTrial * y3 +
            VProcessing * y4 +
            VContact * y5);

        var level = GetLevel(probability);
        var trialDoneNoContract = lead.HasTrial
            && lead.Status is "посещено" or "подтверждена"
            && !string.Equals(lead.TrialResultText, "заключил договор", StringComparison.OrdinalIgnoreCase);

        var expert = ExpertRecommendationEngine.EvaluateLead(level, new LeadConversionFeatures
        {
            HasSource = lead.HasSource,
            SourceConversionRate = y1,
            DirectionActive = lead.DirectionActive,
            DirectionConversionRate = y2,
            HasTrial = lead.HasTrial,
            TrialResultText = string.IsNullOrWhiteSpace(lead.TrialResultText) ? lead.Status : lead.TrialResultText,
            DaysToFirstContact = null,
            LastContactResult = lead.LeadComment,
            TrialDoneNoContract = trialDoneNoContract,
            DaysSinceCreated = daysSinceCreated,
            Status = lead.Status
        });

        return new ScoringResult(probability, expert.Signs, expert.Action, expert.Priority, level, expert.RuleCode, expert.IsActionable);
    }

    private static decimal GetLastContactRiskScore(int? daysSinceLastContact, DateTime arrivalDate)
    {
        if (daysSinceLastContact.HasValue)
        {
            var days = daysSinceLastContact.Value;
            if (days <= 7) return 0m;
            if (days <= 14) return 0.30m;
            if (days <= 30) return 0.60m;
            return 1.00m;
        }

        var daysInCenter = Math.Max(0, (DateTime.Today - arrivalDate.Date).Days);
        if (daysInCenter <= 14) return 0.35m;
        if (daysInCenter <= 30) return 0.55m;
        return 0.75m;
    }

    private static decimal GetTrialScore(LeadSnapshot lead)
    {
        if (!lead.HasTrial)
            return string.Equals(lead.Status, "подтверждена", StringComparison.OrdinalIgnoreCase) ? 0.45m : 0.20m;

        var text = (lead.TrialResultText ?? "").ToLowerInvariant();
        if (text.Contains("заключил")) return 1.00m;
        if (text.Contains("понрав")) return 0.90m;
        if (string.Equals(lead.Status, "посещено", StringComparison.OrdinalIgnoreCase)) return 0.85m;
        if (text.Contains("перенес")) return 0.55m;
        if (text.Contains("не понрав")) return 0.35m;
        if (text.Contains("не приш")) return 0.15m;
        return 0.55m;
    }

    private static decimal GetProcessingSpeedScore(int? daysToFirstContact, int daysSinceCreated)
    {
        var days = daysToFirstContact ?? daysSinceCreated;
        if (days <= 1) return 1.00m;
        if (days <= 3) return 0.75m;
        if (days <= 7) return 0.50m;
        return 0.20m;
    }

    private static decimal GetContactResultScore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.50m;

        var lower = text.ToLowerInvariant();
        if (lower.Contains("отказ") || lower.Contains("не интерес")) return 0.15m;
        if (lower.Contains("дума") || lower.Contains("перезвон")) return 0.60m;
        if (lower.Contains("соглас") || lower.Contains("готов") || lower.Contains("да")) return 1.00m;
        return 0.50m;
    }

    private static string GetLevel(decimal probability)
    {
        if (probability >= HighThreshold) return "высокий";
        if (probability >= MediumThreshold) return "средний";
        return "низкий";
    }

    private static decimal Clamp01(decimal value)
    {
        if (value < 0m) return 0m;
        if (value > 1m) return 1m;
        return value;
    }

    private static string BuildFactorsText(ScoringResult scoring)
    {
        var parts = scoring.Factors.Count == 0
            ? new List<string> { "Критичных факторов не выявлено" }
            : scoring.Factors.ToList();

        if (!string.IsNullOrWhiteSpace(scoring.RuleCode))
            parts.Add($"Правило: {scoring.RuleCode}");

        return string.Join("; ", parts);
    }

    private static async Task InsertChurnPredictionsBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        IReadOnlyList<(int IdStudent, decimal Probability, string RiskLevel, string Factors, int Priority, string Recommendation)> rows)
    {
        const int batchSize = 50;
        for (var offset = 0; offset < rows.Count; offset += batchSize)
        {
            var batch = rows.Skip(offset).Take(batchSize).ToList();
            var sql = new StringBuilder();
            sql.Append("INSERT INTO public.prognoz_ottoka (id_student, veroyatnost_ottoka, uroven_riska, faktor_prognoza, prioritet_obrabotki, rekomenduemoe_deystvie) VALUES ");
            await using var cmd = new NpgsqlCommand { Connection = connection, Transaction = tx };

            for (var i = 0; i < batch.Count; i++)
            {
                if (i > 0) sql.Append(',');
                sql.Append($"(@sid{i}, @prob{i}, @risk{i}, @fact{i}, @prio{i}, @rec{i})");
                cmd.Parameters.AddWithValue($"sid{i}", batch[i].IdStudent);
                cmd.Parameters.AddWithValue($"prob{i}", batch[i].Probability);
                cmd.Parameters.AddWithValue($"risk{i}", batch[i].RiskLevel);
                cmd.Parameters.AddWithValue($"fact{i}", batch[i].Factors);
                cmd.Parameters.AddWithValue($"prio{i}", batch[i].Priority);
                cmd.Parameters.AddWithValue($"rec{i}", batch[i].Recommendation);
            }

            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertLeadPredictionsBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        IReadOnlyList<(int IdLead, decimal Probability, string Level, string Factors, string Recommendation)> rows)
    {
        const int batchSize = 50;
        for (var offset = 0; offset < rows.Count; offset += batchSize)
        {
            var batch = rows.Skip(offset).Take(batchSize).ToList();
            var sql = new StringBuilder();
            sql.Append("INSERT INTO public.prognoz_konversii_zayavok (id_lead, veroyatnost_konversii, uroven_veroyatnosti, faktor_prognoza, rekomenduemoe_deystvie) VALUES ");
            await using var cmd = new NpgsqlCommand { Connection = connection, Transaction = tx };

            for (var i = 0; i < batch.Count; i++)
            {
                if (i > 0) sql.Append(',');
                sql.Append($"(@lid{i}, @prob{i}, @lvl{i}, @fact{i}, @rec{i})");
                cmd.Parameters.AddWithValue($"lid{i}", batch[i].IdLead);
                cmd.Parameters.AddWithValue($"prob{i}", batch[i].Probability);
                cmd.Parameters.AddWithValue($"lvl{i}", batch[i].Level);
                cmd.Parameters.AddWithValue($"fact{i}", batch[i].Factors);
                cmd.Parameters.AddWithValue($"rec{i}", batch[i].Recommendation);
            }

            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SaveMetadataAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string analyticsType, int recordsProcessed)
    {
        await using var ensureCmd = new NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS public.ai_analytics_metadata (
    analytics_type VARCHAR(30) PRIMARY KEY,
    last_calculated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    records_processed INTEGER NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL DEFAULT 'ok'
);", connection, tx);
        await ensureCmd.ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.ai_analytics_metadata (analytics_type, last_calculated_at, records_processed, status)
VALUES (@type, CURRENT_TIMESTAMP, @count, 'ok')
ON CONFLICT (analytics_type)
DO UPDATE SET
    last_calculated_at = EXCLUDED.last_calculated_at,
    records_processed = EXCLUDED.records_processed,
    status = EXCLUDED.status;", connection, tx);
        cmd.Parameters.AddWithValue("type", analyticsType);
        cmd.Parameters.AddWithValue("count", recordsProcessed);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task FillRiskDistributionAsync(NpgsqlConnection connection, AiAnalyticsSummary summary)
    {
        const string sql = @"
SELECT uroven_riska, COUNT(*)::int
FROM (
    SELECT DISTINCT ON (id_student) uroven_riska
    FROM public.prognoz_ottoka
    ORDER BY id_student, data_rascheta DESC, id_prognoza_ottoka DESC
) latest
GROUP BY uroven_riska;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            switch (reader.GetString(0))
            {
                case "высокий": summary.HighRiskStudentsCount = reader.GetInt32(1); break;
                case "средний": summary.MediumRiskStudentsCount = reader.GetInt32(1); break;
                case "низкий": summary.LowRiskStudentsCount = reader.GetInt32(1); break;
            }
        }
    }

    private static async Task FillConversionDistributionAsync(NpgsqlConnection connection, AiAnalyticsSummary summary)
    {
        const string sql = @"
SELECT
    COALESCE(SUM(CASE WHEN latest.uroven_veroyatnosti = 'высокий' THEN 1 ELSE 0 END), 0)::int AS high_count,
    COALESCE(SUM(CASE WHEN latest.uroven_veroyatnosti = 'средний' THEN 1 ELSE 0 END), 0)::int AS medium_count,
    COALESCE(SUM(CASE WHEN latest.uroven_veroyatnosti = 'низкий' THEN 1 ELSE 0 END), 0)::int AS low_count,
    COALESCE(SUM(latest.veroyatnost_konversii), 0) AS expected_clients
FROM (
    SELECT DISTINCT ON (pk.id_lead) pk.uroven_veroyatnosti, pk.veroyatnost_konversii
    FROM public.prognoz_konversii_zayavok pk
    JOIN public.lead_request lr ON lr.id_lead = pk.id_lead
    WHERE lr.status NOT IN ('конвертирована', 'отказ')
    ORDER BY pk.id_lead, pk.data_rascheta DESC, pk.id_prognoza_konversii DESC
) latest;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            summary.HighConversionLeadsCount = reader.GetInt32(0);
            summary.MediumConversionLeadsCount = reader.GetInt32(1);
            summary.LowConversionLeadsCount = reader.GetInt32(2);
            summary.ExpectedNewClients = reader.GetDecimal(3);
        }
    }

    private static async Task FillDebtRatioAsync(NpgsqlConnection connection, AiAnalyticsSummary summary)
    {
        const string sql = @"
SELECT CASE WHEN SUM(c.total_amount) > 0
            THEN COALESCE(SUM(GREATEST(d.debt_amount - d.paid_amount, 0)), 0) / SUM(c.total_amount)
            ELSE 0 END
FROM public.debt d
JOIN public.contract c ON c.id_contract = d.id_contract
WHERE d.status IN ('активна', 'частично_погашена');";

        await using var cmd = new NpgsqlCommand(sql, connection);
        var value = await cmd.ExecuteScalarAsync();
        summary.AverageDebtRatio = value is decimal d ? d : 0m;
    }

    private static async Task FillActiveRecommendationsCountAsync(NpgsqlConnection connection, AiAnalyticsSummary summary)
    {
        const string sql = @"
SELECT COUNT(*)::int FROM (
    SELECT rekomenduemoe_deystvie FROM public.prognoz_ottoka
    WHERE COALESCE(rekomenduemoe_deystvie, '') <> ''
      AND rekomenduemoe_deystvie <> @churn_std
    UNION ALL
    SELECT rekomenduemoe_deystvie FROM public.prognoz_konversii_zayavok
    WHERE COALESCE(rekomenduemoe_deystvie, '') <> ''
      AND rekomenduemoe_deystvie <> @lead_std
) t;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("churn_std", ExpertRecommendationEngine.ChurnStandardAction);
        cmd.Parameters.AddWithValue("lead_std", ExpertRecommendationEngine.LeadLowPriorityAction);
        summary.ActiveRecommendationsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task EnsureRecommendationStatusTableAsync(NpgsqlConnection connection)
    {
        await using (var cmd = new NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS public.ai_recommendation_status (
    entity_type VARCHAR(20) NOT NULL,
    entity_id INTEGER NOT NULL,
    recommendation_text TEXT NOT NULL,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    completed_at TIMESTAMP NULL,
    rule_code VARCHAR(50) NULL,
    recommendation_type VARCHAR(20) NULL,
    entity_display_name TEXT NULL,
    triggered_signs TEXT NULL,
    completed_by_user_id INTEGER NULL,
    completed_by_name VARCHAR(255) NULL,
    PRIMARY KEY (entity_type, entity_id, recommendation_text)
);", connection))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await EnsureRecommendationStatusColumnAsync(connection, "rule_code", "VARCHAR(50) NULL");
        await EnsureRecommendationStatusColumnAsync(connection, "recommendation_type", "VARCHAR(20) NULL");
        await EnsureRecommendationStatusColumnAsync(connection, "entity_display_name", "TEXT NULL");
        await EnsureRecommendationStatusColumnAsync(connection, "triggered_signs", "TEXT NULL");
        await EnsureRecommendationStatusColumnAsync(connection, "completed_by_user_id", "INTEGER NULL");
        await EnsureRecommendationStatusColumnAsync(connection, "completed_by_name", "VARCHAR(255) NULL");
    }

    private static async Task EnsureRecommendationStatusColumnAsync(
        NpgsqlConnection connection,
        string columnName,
        string columnDefinition)
    {
        await using var cmd = new NpgsqlCommand($@"
DO $$
BEGIN
    ALTER TABLE public.ai_recommendation_status ADD COLUMN {columnName} {columnDefinition};
EXCEPTION
    WHEN duplicate_column THEN NULL;
END $$;", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class StudentSnapshot
    {
        public int IdStudent { get; init; }
        public DateTime ArrivalDate { get; init; }
        public int TotalCount { get; init; }
        public int VisitedCount { get; init; }
        public int MissedCount { get; init; }
        public decimal DebtAmount { get; init; }
        public int MaxOverdueDays { get; init; }
        public decimal ContractTotal { get; init; }
        public DateTime? ContractEndDate { get; init; }
        public bool HasWarning { get; init; }
    }

    private sealed class LeadSnapshot
    {
        public int IdLead { get; init; }
        public string Status { get; init; } = "";
        public DateTime CreatedDate { get; init; }
        public bool HasSource { get; init; }
        public bool DirectionActive { get; init; }
        public string LeadComment { get; init; } = "";
        public decimal SourceConversionRate { get; init; }
        public decimal DirectionConversionRate { get; init; }
        public bool HasTrial { get; init; }
        public string TrialResultText { get; init; } = "";
        public string LeadStatus { get; init; } = "";
    }

    private sealed record ScoringResult(
        decimal Probability,
        IReadOnlyList<string> Factors,
        string Recommendation,
        int Priority,
        string RiskLevel,
        string RuleCode,
        bool IsActionable);
}
