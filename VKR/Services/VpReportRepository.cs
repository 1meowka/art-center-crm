#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class VpReportRepository
{
    public async Task<IReadOnlyList<VpSalesRegisterLine>> GetSalesRegisterLinesAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpSalesRegisterLine>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT payment_date, kind, contract_label, client_full_name, service_type_name, direction_name,
       teacher_name, period_text, manager_name, amount, method_name, method_code,
       cash_receipt_issued, note, discount_text, source_name, client_category_code
FROM (
    SELECT p.payment_date,
           'service' AS kind,
           COALESCE(NULLIF(TRIM(c.contract_number), ''), 'договор') AS contract_label,
           TRIM(COALESCE(s.last_name, '') || ' ' || COALESCE(s.first_name, '')
                || COALESCE(' ' || s.middle_name, '')) AS client_full_name,
           COALESCE(st.service_name, '') AS service_type_name,
           COALESCE(d.direction_name, '') AS direction_name,
           TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')
                || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
           CASE
               WHEN p.period_month IS NOT NULL AND p.period_year IS NOT NULL
                   THEN LPAD(p.period_month::text, 2, '0')
               WHEN p.period_month IS NOT NULL
                   THEN LPAD(p.period_month::text, 2, '0')
               ELSE ''
           END AS period_text,
           TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')
                || COALESCE(' ' || u.middle_name, '')) AS manager_name,
           p.amount,
           COALESCE(pm.method_name, '') AS method_name,
           COALESCE(pm.method_code, '') AS method_code,
           COALESCE(p.cash_receipt_issued, FALSE) AS cash_receipt_issued,
           COALESCE(p.comment, '') AS note,
           CASE
               WHEN COALESCE(c.discount_amount, 0) > 0 THEN 'акция'
               WHEN COALESCE(c.discount_percent, 0) > 0 THEN TRIM(TO_CHAR(c.discount_percent, 'FM999990.0')) || '%'
               ELSE ''
           END AS discount_text,
           COALESCE(src.source_name, '') AS source_name,
           CASE
               WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%нов%' THEN 1
               WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%верн%' THEN 2
               WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%перез%' THEN 4
               ELSE 3
           END AS client_category_code
    FROM public.payment p
    LEFT JOIN public.contract c ON c.id_contract = p.id_contract
    LEFT JOIN public.student s ON s.id_student = c.id_student
    LEFT JOIN public.direction d ON d.id_direction = c.id_direction
    LEFT JOIN public.study_group g ON g.id_group = c.id_group
    LEFT JOIN public.teacher t ON t.id_teacher = g.id_teacher
    LEFT JOIN public.service_type st ON st.id_service_type = p.id_service_type
    LEFT JOIN public.payment_method pm ON pm.id_payment_method = p.id_payment_method
    LEFT JOIN public.app_user u ON u.id_user = p.id_created_by
    LEFT JOIN public.student_type stt ON stt.id_student_type = COALESCE(c.id_student_type, s.id_student_type)
    LEFT JOIN public.lead_request lreq ON lreq.id_lead = (
        SELECT l.id_lead FROM public.lead_request l
        WHERE LOWER(l.child_first_name) = LOWER(s.first_name)
          AND COALESCE(LOWER(l.child_last_name), '') = COALESCE(LOWER(s.last_name), '')
        ORDER BY l.created_date DESC LIMIT 1
    )
    LEFT JOIN public.source_of_arrival src ON src.id_source = lreq.id_source
    WHERE p.payment_date::date BETWEEN @from AND @to

    UNION ALL

    SELECT s.sale_date AS payment_date,
           'shop' AS kind,
           'магазин' AS contract_label,
           '' AS client_full_name,
           'магазин' AS service_type_name,
           COALESCE(s.item_name, p.product_name, 'магазин') AS direction_name,
           '' AS teacher_name,
           '' AS period_text,
           '' AS manager_name,
           COALESCE(s.total_amount, s.sale_price * s.quantity, 0) AS amount,
           COALESCE(pm.method_name, '') AS method_name,
           COALESCE(pm.method_code, '') AS method_code,
           FALSE AS cash_receipt_issued,
           COALESCE(s.comment, '') AS note,
           '' AS discount_text,
           '' AS source_name,
           3 AS client_category_code
    FROM public.shop_sale s
    LEFT JOIN public.shop_product p ON p.id_product = s.id_product
    LEFT JOIN public.payment_method pm ON pm.id_payment_method = s.id_payment_method
    WHERE s.sale_date::date BETWEEN @from AND @to
) t
ORDER BY payment_date, kind, amount;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var methodName = reader.IsDBNull(10) ? "" : reader.GetString(10);
            var methodCode = reader.IsDBNull(11) ? "" : reader.GetString(11);
            result.Add(new VpSalesRegisterLine
            {
                Date = reader.GetDateTime(0),
                Kind = reader.GetString(1),
                ContractLabel = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ClientFullName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ServiceTypeName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                DirectionName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                TeacherName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                PeriodText = reader.IsDBNull(7) ? "" : reader.GetString(7),
                ManagerName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Amount = reader.GetDecimal(9),
                Channel = PpoPaymentChannelMapper.Resolve(methodName, methodCode),
                CashReceiptIssued = !reader.IsDBNull(12) && reader.GetBoolean(12),
                Note = reader.IsDBNull(13) ? "" : reader.GetString(13),
                DiscountText = reader.IsDBNull(14) ? "" : reader.GetString(14),
                SourceName = reader.IsDBNull(15) ? "" : reader.GetString(15),
                ClientCategoryCode = reader.GetInt32(16)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<VpContractExportRow>> GetContractsAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpContractExportRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT c.id_contract,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
       CASE
           WHEN COALESCE(c.discount_amount, 0) > 0 THEN 'акция'
           WHEN COALESCE(c.discount_percent, 0) > 0 THEN TRIM(TO_CHAR(c.discount_percent, 'FM999990.0')) || '%'
           ELSE ''
       END AS discount_text,
       c.total_amount,
       c.contract_date,
       COALESCE(d.direction_name, '') AS direction_name,
       COALESCE(NULLIF(TRIM(s.phone), ''), NULLIF(TRIM(r.phone), ''), '') AS phone,
       TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')
            || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
       c.times_per_week,
       CASE WHEN g.id_group IS NULL THEN '' ELSE 'гр №' || g.group_number::text END AS group_name,
       COALESCE(c.comment, '') AS tax_note,
       CASE
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%нов%' THEN 1
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%верн%' THEN 2
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%перез%' THEN 4
           ELSE 3
       END AS client_category_code,
       COALESCE(lrs.reason_name, ct.comment, CASE WHEN c.status = 'расторгнут' THEN c.status ELSE '' END, '') AS termination_text,
       s.birth_date,
       COALESCE(src.source_name, '') AS source_name,
       TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')
            || COALESCE(' ' || u.middle_name, '')) AS manager_name
FROM public.contract c
JOIN public.student s ON s.id_student = c.id_student
LEFT JOIN public.direction d ON d.id_direction = c.id_direction
LEFT JOIN public.study_group g ON g.id_group = c.id_group
LEFT JOIN public.teacher t ON t.id_teacher = g.id_teacher
LEFT JOIN public.student_type stt ON stt.id_student_type = c.id_student_type
LEFT JOIN public.representative r ON r.id_representative = c.id_representative
LEFT JOIN public.contract_termination ct ON ct.id_contract = c.id_contract
LEFT JOIN public.leaving_reason lrs ON lrs.id_reason = ct.id_reason
LEFT JOIN public.app_user u ON u.id_user = c.id_created_by
LEFT JOIN public.lead_request lreq ON lreq.id_lead = (
    SELECT l.id_lead FROM public.lead_request l
    WHERE LOWER(l.child_first_name) = LOWER(s.first_name)
      AND COALESCE(LOWER(l.child_last_name), '') = COALESCE(LOWER(s.last_name), '')
    ORDER BY l.created_date DESC LIMIT 1
)
LEFT JOIN public.source_of_arrival src ON src.id_source = lreq.id_source
WHERE c.contract_date::date BETWEEN @from AND @to
ORDER BY c.contract_date, c.id_contract;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapContractRow(reader));
        }

        return result;
    }

    public async Task<IReadOnlyList<VpContractExportRow>> GetAcademicYearContractsAsync(int academicYearStart)
    {
        var yearStart = new DateTime(academicYearStart, 8, 1);
        var yearEnd = new DateTime(academicYearStart + 1, 7, 31);

        var result = new List<VpContractExportRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT c.id_contract,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
       CASE
           WHEN COALESCE(c.discount_amount, 0) > 0 THEN 'акция'
           WHEN COALESCE(c.discount_percent, 0) > 0 THEN TRIM(TO_CHAR(c.discount_percent, 'FM999990.0')) || '%'
           ELSE ''
       END AS discount_text,
       c.total_amount,
       c.contract_date,
       COALESCE(d.direction_name, '') AS direction_name,
       COALESCE(NULLIF(TRIM(s.phone), ''), NULLIF(TRIM(r.phone), ''), '') AS phone,
       TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')
            || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
       c.times_per_week,
       CASE WHEN g.id_group IS NULL THEN '' ELSE 'гр №' || g.group_number::text END AS group_name,
       COALESCE(c.comment, '') AS tax_note,
       CASE
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%нов%' THEN 1
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%верн%' THEN 2
           WHEN LOWER(COALESCE(stt.type_code, '') || ' ' || COALESCE(stt.type_name, '')) LIKE '%перез%' THEN 4
           ELSE 3
       END AS client_category_code,
       COALESCE(lrs.reason_name, ct.comment, CASE WHEN c.status = 'расторгнут' THEN c.status ELSE '' END, '') AS termination_text,
       s.birth_date,
       COALESCE(src.source_name, '') AS source_name,
       TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')
            || COALESCE(' ' || u.middle_name, '')) AS manager_name
FROM public.contract c
JOIN public.student s ON s.id_student = c.id_student
LEFT JOIN public.direction d ON d.id_direction = c.id_direction
LEFT JOIN public.study_group g ON g.id_group = c.id_group
LEFT JOIN public.teacher t ON t.id_teacher = g.id_teacher
LEFT JOIN public.student_type stt ON stt.id_student_type = c.id_student_type
LEFT JOIN public.representative r ON r.id_representative = c.id_representative
LEFT JOIN public.contract_termination ct ON ct.id_contract = c.id_contract
LEFT JOIN public.leaving_reason lrs ON lrs.id_reason = ct.id_reason
LEFT JOIN public.app_user u ON u.id_user = c.id_created_by
LEFT JOIN public.lead_request lreq ON lreq.id_lead = (
    SELECT l.id_lead FROM public.lead_request l
    WHERE LOWER(l.child_first_name) = LOWER(s.first_name)
      AND COALESCE(LOWER(l.child_last_name), '') = COALESCE(LOWER(s.last_name), '')
    ORDER BY l.created_date DESC LIMIT 1
)
LEFT JOIN public.source_of_arrival src ON src.id_source = lreq.id_source
WHERE c.contract_date::date BETWEEN @from AND @to
   OR (c.contract_date::date <= @to AND COALESCE(c.end_date::date, @to) >= @from AND c.status = 'активен')
ORDER BY c.contract_date, c.id_contract;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = yearStart;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = yearEnd;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(MapContractRow(reader));
        }

        return result;
    }

    private static VpContractExportRow MapContractRow(NpgsqlDataReader reader) =>
        new()
        {
            IdContract = reader.GetInt32(0),
            StudentFullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
            DiscountText = reader.IsDBNull(2) ? "" : reader.GetString(2),
            TotalAmount = reader.GetDecimal(3),
            ContractDate = reader.GetDateTime(4),
            DirectionName = reader.IsDBNull(5) ? "" : reader.GetString(5),
            Phone = reader.IsDBNull(6) ? "" : reader.GetString(6),
            TeacherName = reader.IsDBNull(7) ? "" : reader.GetString(7),
            TimesPerWeek = reader.IsDBNull(8) ? (short)0 : reader.GetInt16(8),
            GroupName = reader.IsDBNull(9) ? "" : reader.GetString(9),
            TaxNote = reader.IsDBNull(10) ? "" : reader.GetString(10),
            ClientCategoryCode = reader.IsDBNull(11) ? 3 : reader.GetInt32(11),
            TerminationText = reader.IsDBNull(12) ? "" : reader.GetString(12),
            BirthDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            SourceName = reader.IsDBNull(14) ? "" : reader.GetString(14),
            ManagerName = reader.IsDBNull(15) ? "" : reader.GetString(15)
        };

    public async Task<IReadOnlyList<VpTrialLessonRow>> GetTrialLessonsAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpTrialLessonRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT tl.scheduled_at,
       TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')) AS child_full_name,
       lr.child_birth_date,
       ''::text AS parent_name,
       lr.contact_phone,
       COALESCE(d.direction_name, '') AS direction_name,
       COALESCE(slot.teacher_name, '') AS teacher_name,
       COALESCE(lr.comment, '') AS lead_comment,
       COALESCE(src.source_name, '') AS source_name,
       c.contract_date
FROM public.trial_lesson tl
JOIN public.lead_request lr ON lr.id_lead = tl.id_lead
JOIN public.direction d ON d.id_direction = lr.id_direction
LEFT JOIN public.source_of_arrival src ON src.id_source = lr.id_source
LEFT JOIN LATERAL (
    SELECT TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name
    FROM public.schedule sc
    JOIN public.study_group g ON g.id_group = sc.id_group AND g.id_direction = lr.id_direction
    JOIN public.teacher t ON t.id_teacher = g.id_teacher
    WHERE sc.is_active = true
      AND sc.day_of_week = CASE
            WHEN EXTRACT(ISODOW FROM tl.scheduled_at)::int = 7 THEN 7
            ELSE EXTRACT(ISODOW FROM tl.scheduled_at)::int
          END
      AND sc.time_start = tl.scheduled_at::time
    ORDER BY sc.id_schedule
    LIMIT 1
) slot ON TRUE
LEFT JOIN LATERAL (
    SELECT MIN(ct.contract_date) AS contract_date
    FROM public.contract ct
    JOIN public.student s ON s.id_student = ct.id_student
    WHERE LOWER(s.first_name) = LOWER(lr.child_first_name)
      AND COALESCE(LOWER(s.last_name), '') = COALESCE(LOWER(lr.child_last_name), '')
      AND ct.contract_date >= tl.scheduled_at::date
) c ON TRUE
WHERE tl.scheduled_at::date BETWEEN @from AND @to
ORDER BY tl.scheduled_at, tl.id_trial;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTime? birth = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            var ageText = "";
            if (birth.HasValue)
            {
                var age = DateTime.Today.Year - birth.Value.Year;
                if (birth.Value.Date > DateTime.Today.AddYears(-age))
                    age--;
                ageText = $"{age} лет";
            }

            result.Add(new VpTrialLessonRow
            {
                ScheduledAt = reader.GetDateTime(0),
                ChildFullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                AgeText = ageText,
                ParentInfo = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? "" : reader.GetString(4),
                DirectionName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                TeacherName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Note = reader.IsDBNull(7) ? "" : reader.GetString(7),
                SourceName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                ContractDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<VpNewcomerRow>> GetNewcomersAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpNewcomerRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COALESCE(s.arrival_date, first_c.contract_date) AS arrival_date,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS client_full_name,
       COALESCE(pay.amount, 0) AS amount,
       COALESCE(d.direction_name, '') AS direction_name,
       TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')) AS teacher_name,
       TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS manager_name,
       COALESCE(s.comment, '') AS comment
FROM public.student s
LEFT JOIN LATERAL (
    SELECT SUM(p.amount) AS amount
    FROM public.payment p
    JOIN public.contract c ON c.id_contract = p.id_contract
    WHERE c.id_student = s.id_student
      AND p.payment_date::date BETWEEN @from AND @to
) pay ON TRUE
LEFT JOIN LATERAL (
    SELECT c.id_direction, c.id_group, c.id_created_by, c.contract_date
    FROM public.contract c
    WHERE c.id_student = s.id_student
    ORDER BY c.contract_date DESC
    LIMIT 1
) last_c ON TRUE
LEFT JOIN LATERAL (
    SELECT MIN(c.contract_date) AS contract_date
    FROM public.contract c
    WHERE c.id_student = s.id_student
) first_c ON TRUE
LEFT JOIN public.direction d ON d.id_direction = last_c.id_direction
LEFT JOIN public.study_group g ON g.id_group = last_c.id_group
LEFT JOIN public.teacher t ON t.id_teacher = g.id_teacher
LEFT JOIN public.app_user u ON u.id_user = last_c.id_created_by
WHERE COALESCE(s.arrival_date, first_c.contract_date)::date BETWEEN @from AND @to
ORDER BY COALESCE(s.arrival_date, first_c.contract_date), s.id_student;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new VpNewcomerRow
            {
                VisitDate = reader.GetDateTime(0),
                ClientFullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Amount = reader.GetDecimal(2),
                DirectionName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                TeacherName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ManagerName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Comment = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<VpDebtorExportRow>> GetDebtorsAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpDebtorExportRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT db.occurrence_date,
       COALESCE(d.direction_name, '') AS direction_name,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS client_full_name,
       db.debt_amount,
       db.paid_amount,
       TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS manager_name,
       COALESCE(db.note, '') AS note,
       db.repayment_date
FROM public.debt db
JOIN public.contract c ON c.id_contract = db.id_contract
JOIN public.student s ON s.id_student = c.id_student
JOIN public.direction d ON d.id_direction = c.id_direction
LEFT JOIN public.app_user u ON u.id_user = c.id_created_by
WHERE db.occurrence_date::date <= @to
  AND COALESCE(db.repayment_date::date, DATE '9999-12-31') >= @from
ORDER BY db.occurrence_date, db.id_debt;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new VpDebtorExportRow
            {
                OccurrenceDate = reader.GetDateTime(0),
                DirectionName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                ClientFullName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                DebtAmount = reader.GetDecimal(3),
                PaidAmount = reader.GetDecimal(4),
                ManagerName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Note = reader.IsDBNull(6) ? "" : reader.GetString(6),
                RepaymentDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<VpChurnedRow>> GetChurnedAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpChurnedRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT c.total_amount,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS client_full_name,
       ct.termination_date,
       TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '') || ' / ' || COALESCE(d.direction_name, '')) AS teacher_direction,
       TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS manager_name,
       COALESCE(lr.reason_name, ct.comment, c.comment, '') AS comment
FROM public.contract_termination ct
JOIN public.contract c ON c.id_contract = ct.id_contract
JOIN public.student s ON s.id_student = c.id_student
LEFT JOIN public.direction d ON d.id_direction = c.id_direction
LEFT JOIN public.study_group g ON g.id_group = c.id_group
LEFT JOIN public.teacher t ON t.id_teacher = g.id_teacher
LEFT JOIN public.leaving_reason lr ON lr.id_reason = ct.id_reason
LEFT JOIN public.app_user u ON u.id_user = c.id_created_by
WHERE ct.termination_date::date BETWEEN @from AND @to
ORDER BY ct.termination_date, ct.id_termination;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new VpChurnedRow
            {
                Amount = reader.GetDecimal(0),
                ClientFullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                TerminationDate = reader.GetDateTime(2),
                TeacherDirectionText = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ManagerName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Comment = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<VpMonthBucketMetrics>> GetMonthMetricsAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<VpMonthBucketMetrics>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var cursor = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var end = new DateTime(dateTo.Year, dateTo.Month, 1);
        while (cursor <= end)
        {
            var monthStart = cursor;
            var monthEnd = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            if (monthStart < dateFrom.Date)
                monthStart = dateFrom.Date;
            if (monthEnd > dateTo.Date)
                monthEnd = dateTo.Date;

            var metrics = new VpMonthBucketMetrics
            {
                Year = cursor.Year,
                Month = cursor.Month
            };

            await FillMonthMetricsAsync(connection, metrics, monthStart, monthEnd);
            result.Add(metrics);
            cursor = cursor.AddMonths(1);
        }

        return result;
    }

    private static async Task FillMonthMetricsAsync(
        NpgsqlConnection connection,
        VpMonthBucketMetrics metrics,
        DateTime monthStart,
        DateTime monthEnd)
    {
        await using (var cmd = new NpgsqlCommand(@"
SELECT payment_date::date, SUM(amount)
FROM public.payment
WHERE payment_date::date BETWEEN @from AND @to
GROUP BY payment_date::date;", connection))
        {
            cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = monthStart;
            cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = monthEnd;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var day = reader.GetDateTime(0).Day;
                var amount = reader.GetDecimal(1);
                var bucket = VpReportSupport.WeekBucketIndex(day);
                metrics.ServicePaymentsByWeek[bucket] += amount;
                metrics.ServicePaymentsTotal += amount;
            }
        }

        await using (var cmd = new NpgsqlCommand(@"
SELECT sale_date::date, SUM(COALESCE(total_amount, sale_price * quantity, 0))
FROM public.shop_sale
WHERE sale_date::date BETWEEN @from AND @to
GROUP BY sale_date::date;", connection))
        {
            cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = monthStart;
            cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = monthEnd;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var day = reader.GetDateTime(0).Day;
                var amount = reader.GetDecimal(1);
                var bucket = VpReportSupport.WeekBucketIndex(day);
                metrics.ShopSalesByWeek[bucket] += amount;
                metrics.ShopSalesTotal += amount;
            }
        }

        metrics.TrialLessons = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.trial_lesson
WHERE scheduled_at::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.ContractsCount = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.contract
WHERE contract_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.NewContracts = metrics.ContractsCount;

        metrics.UniqueClients = await ScalarIntAsync(connection, @"
SELECT COUNT(DISTINCT c.id_student) FROM public.payment p
JOIN public.contract c ON c.id_contract = p.id_contract
WHERE p.payment_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.ActiveGroups = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.study_group WHERE is_active = TRUE;", monthStart, monthEnd);

        metrics.ChurnedCount = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.contract_termination
WHERE termination_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.DebtorsCount = await ScalarIntAsync(connection, @"
SELECT COUNT(DISTINCT c.id_student)
FROM public.debt db
JOIN public.contract c ON c.id_contract = db.id_contract
WHERE db.status IN ('активна', 'частично_погашена')
  AND (db.debt_amount - db.paid_amount) > 0
  AND db.occurrence_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.DebtorsAmount = await ScalarDecimalAsync(connection, @"
SELECT COALESCE(SUM(GREATEST(db.debt_amount - db.paid_amount, 0)), 0)
FROM public.debt db
WHERE db.status IN ('активна', 'частично_погашена')
  AND db.occurrence_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        metrics.AttendanceCount = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.attendance
WHERE status = 'присутствовал'
  AND lesson_date::date BETWEEN @from AND @to;",
            monthStart, monthEnd);

        var paymentCount = await ScalarIntAsync(connection, @"
SELECT COUNT(*) FROM public.payment
WHERE payment_date::date BETWEEN @from AND @to;", monthStart, monthEnd);

        metrics.AverageCheck = paymentCount > 0
            ? Math.Round(metrics.ServicePaymentsTotal / paymentCount, 2)
            : 0;
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        string sql,
        DateTime from,
        DateTime to)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = to;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static async Task<decimal> ScalarDecimalAsync(
        NpgsqlConnection connection,
        string sql,
        DateTime from,
        DateTime to)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = to;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }
}
