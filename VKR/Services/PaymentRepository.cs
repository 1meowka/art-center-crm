#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class PaymentRepository
{
    public async Task<IReadOnlyList<PaymentListItem>> GetPaymentsAsync(
        string? search = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? idStudent = null,
        int? idBranch = null,
        int? periodMonth = null)
    {
        var list = new List<PaymentListItem>();

        DatabaseSchema.EnsureIfPossible();
        FinanceSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var sql = @"
SELECT
    p.id_payment,
    p.payment_date,
    p.id_contract,
    COALESCE(c.contract_number, '')                                          AS contract_number,
    COALESCE(p.id_student, c.id_student)                                     AS id_student,
    TRIM(COALESCE(s.last_name, '') || ' ' || COALESCE(s.first_name, '')
         || COALESCE(' ' || s.middle_name, ''))                              AS student_full_name,
    p.amount,
    COALESCE(pm.method_name, '')                                             AS method_name,
    COALESCE(st.service_name, '')                                            AS service_name,
    p.comment,
    CASE WHEN g.id_group IS NULL THEN '' ELSE 'гр. ' || g.group_number::text END AS group_name,
    p.period_month,
    p.period_year,
    TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, ''))     AS manager_name,
    COALESCE(c.id_branch, g.id_branch) AS id_branch,
    p.discount_percent,
    p.amount_base,
    COALESCE(c.direction_name_snapshot, d.direction_name, '') AS direction_name,
    c.times_per_week,
    p.actual_date,
    COALESCE(p.payment_kind, '') AS payment_kind,
    COALESCE(p.event_text, '') AS event_text,
    COALESCE(p.period_label, '') AS period_label,
    TRIM(COALESCE(mgr.last_name, '') || ' ' || COALESCE(mgr.first_name, '')) AS manager_user,
    TRIM(COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')) AS teacher_name
FROM public.payment p
LEFT JOIN public.contract       c  ON c.id_contract       = p.id_contract
LEFT JOIN public.student        s  ON s.id_student        = COALESCE(p.id_student, c.id_student)
LEFT JOIN public.payment_method pm ON pm.id_payment_method = p.id_payment_method
LEFT JOIN public.service_type   st ON st.id_service_type  = p.id_service_type
LEFT JOIN public.study_group    g  ON g.id_group          = c.id_group
LEFT JOIN public.direction      d  ON d.id_direction      = c.id_direction
LEFT JOIN public.app_user       u  ON u.id_user           = p.id_created_by
LEFT JOIN public.app_user       mgr ON mgr.id_user        = COALESCE(p.id_manager, p.id_created_by)
LEFT JOIN public.teacher        t  ON t.id_teacher        = g.id_teacher
WHERE 1 = 1
  AND (@search IS NULL OR
       COALESCE(c.contract_number, '') ILIKE '%' || @search || '%' OR
       (COALESCE(s.last_name, '') || ' ' || COALESCE(s.first_name, '') ||
        COALESCE(' ' || s.middle_name, '')) ILIKE '%' || @search || '%' OR
       COALESCE(s.last_name, '') ILIKE '%' || @search || '%' OR
       to_char(p.payment_date, 'DD.MM.YYYY') ILIKE '%' || @search || '%' OR
       to_char(p.payment_date, 'DD.MM.YY') ILIKE '%' || @search || '%' OR
       to_char(p.payment_date, 'YYYY-MM-DD') ILIKE '%' || @search || '%' OR
       COALESCE(p.comment, '') ILIKE '%' || @search || '%' OR
       COALESCE(st.service_name, '') ILIKE '%' || @search || '%' OR
       COALESCE(p.event_text, '') ILIKE '%' || @search || '%' OR
       COALESCE(p.period_label, '') ILIKE '%' || @search || '%' OR
       (COALESCE(t.last_name, '') || ' ' || COALESCE(t.first_name, '')) ILIKE '%' || @search || '%' OR
       (COALESCE(mgr.last_name, '') || ' ' || COALESCE(mgr.first_name, '')) ILIKE '%' || @search || '%')
  AND (@dateFrom  IS NULL OR p.payment_date >= @dateFrom)
  AND (@dateTo    IS NULL OR p.payment_date <= @dateTo)
  AND (@idStudent IS NULL OR COALESCE(p.id_student, c.id_student) = @idStudent)
  AND (@idBranch  IS NULL OR COALESCE(c.id_branch, g.id_branch) = @idBranch)
  AND (@periodMonth IS NULL OR p.period_month = @periodMonth
       OR (p.actual_date IS NOT NULL AND EXTRACT(MONTH FROM p.actual_date)::int = @periodMonth)
       OR (p.period_month IS NULL AND EXTRACT(MONTH FROM p.payment_date)::int = @periodMonth))
ORDER BY p.payment_date DESC, p.id_payment DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("search",    NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        cmd.Parameters.Add("dateFrom",  NpgsqlDbType.Date).Value    = dateFrom.HasValue  ? (object)dateFrom.Value.Date  : DBNull.Value;
        cmd.Parameters.Add("dateTo",    NpgsqlDbType.Date).Value    = dateTo.HasValue    ? (object)dateTo.Value.Date    : DBNull.Value;
        cmd.Parameters.Add("idStudent", NpgsqlDbType.Integer).Value = idStudent.HasValue ? (object)idStudent.Value      : DBNull.Value;
        cmd.Parameters.Add("idBranch",  NpgsqlDbType.Smallint).Value = idBranch.HasValue ? (object)(short)idBranch.Value : DBNull.Value;
        cmd.Parameters.Add("periodMonth", NpgsqlDbType.Smallint).Value =
            periodMonth is > 0 ? (object)(short)periodMonth.Value : DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PaymentListItem
            {
                IdPayment         = reader.GetInt32(0),
                PaymentDate       = reader.GetDateTime(1),
                IdContract        = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ContractNumber    = reader.IsDBNull(3) ? "" : reader.GetString(3),
                IdStudent         = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                StudentFullName   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Amount            = reader.GetDecimal(6),
                PaymentMethodName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                ServiceTypeName   = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Comment           = reader.IsDBNull(9) ? null : reader.GetString(9),
                GroupName         = reader.IsDBNull(10) ? "" : reader.GetString(10),
                PeriodMonth       = reader.IsDBNull(11) ? null : reader.GetInt16(11),
                PeriodYear        = reader.IsDBNull(12) ? null : reader.GetInt16(12),
                IdBranch          = reader.IsDBNull(14) ? null : Convert.ToInt32(reader.GetValue(14)),
                DiscountPercent   = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                AmountBase        = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                DirectionName     = reader.IsDBNull(17) ? "" : reader.GetString(17),
                TimesPerWeek      = reader.IsDBNull(18) ? null : reader.GetInt16(18),
                ActualDate        = reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetDateTime(19) : null,
                PaymentKind       = reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetString(20) : "",
                EventText         = reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetString(21) : "",
                PeriodLabel       = reader.FieldCount > 22 && !reader.IsDBNull(22) ? reader.GetString(22) : "",
                ManagerName       = reader.FieldCount > 23 && !reader.IsDBNull(23) && !string.IsNullOrWhiteSpace(reader.GetString(23))
                    ? reader.GetString(23)
                    : (reader.IsDBNull(13) ? "" : reader.GetString(13)),
                TeacherName       = reader.FieldCount > 24 && !reader.IsDBNull(24) ? reader.GetString(24) : ""
            });
        }

        return list;
    }

    public async Task<Payment?> GetByIdAsync(int idPayment)
    {
        DatabaseSchema.EnsureIfPossible();
        FinanceSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_payment, payment_date, id_contract, amount, id_payment_method,
       id_service_type, cash_receipt_issued,
       period_month, period_year, id_created_by, comment,
       id_student, discount_percent, amount_base,
       actual_date, payment_kind, event_text, period_label, id_manager
FROM public.payment
WHERE id_payment = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idPayment);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Payment
        {
            IdPayment        = reader.GetInt32(0),
            PaymentDate      = reader.GetDateTime(1),
            IdContract       = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            Amount           = reader.GetDecimal(3),
            IdPaymentMethod  = reader.GetInt32(4),
            IdServiceType    = reader.GetInt32(5),
            CashReceiptIssued = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
            PeriodMonth      = reader.IsDBNull(7) ? null : reader.GetInt16(7),
            PeriodYear       = reader.IsDBNull(8) ? null : reader.GetInt16(8),
            IdCreatedBy      = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            Comment          = reader.IsDBNull(10) ? null : reader.GetString(10),
            IdStudent        = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            DiscountPercent  = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            AmountBase       = reader.IsDBNull(13) ? null : reader.GetDecimal(13),
            ActualDate       = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetDateTime(14) : null,
            PaymentKind      = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : null,
            EventText        = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
            PeriodLabel      = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : null,
            IdManager        = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetInt32(18) : null
        };
    }

    public async Task<int> CreateAsync(Payment p)
    {
        FinanceSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO public.payment
(payment_date, id_contract, amount, id_payment_method, id_service_type,
 cash_receipt_issued, period_month, period_year,
 id_created_by, comment, id_student, discount_percent, amount_base,
 actual_date, payment_kind, event_text, period_label, id_manager)
VALUES
(@dt, @contract, @amount, @method, @service,
 @receipt, @pm, @py,
 @creator, @comment, @student, @discount, @base,
 @actual, @kind, @event, @period, @manager)
RETURNING id_payment;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("dt",       NpgsqlDbType.Date).Value     = p.PaymentDate.Date;
        cmd.Parameters.Add("contract", NpgsqlDbType.Integer).Value  = p.IdContract.HasValue ? (object)p.IdContract.Value : DBNull.Value;
        cmd.Parameters.Add("amount",   NpgsqlDbType.Numeric).Value  = p.Amount;
        cmd.Parameters.Add("method",   NpgsqlDbType.Integer).Value  = p.IdPaymentMethod;
        cmd.Parameters.Add("service",  NpgsqlDbType.Integer).Value  = p.IdServiceType;
        cmd.Parameters.Add("receipt",  NpgsqlDbType.Boolean).Value  = p.CashReceiptIssued;
        cmd.Parameters.Add("pm",       NpgsqlDbType.Smallint).Value = p.PeriodMonth.HasValue ? (object)p.PeriodMonth.Value : DBNull.Value;
        cmd.Parameters.Add("py",       NpgsqlDbType.Smallint).Value = p.PeriodYear.HasValue  ? (object)p.PeriodYear.Value  : DBNull.Value;
        cmd.Parameters.Add("creator",  NpgsqlDbType.Integer).Value  = p.IdCreatedBy.HasValue ? (object)p.IdCreatedBy.Value : DBNull.Value;
        cmd.Parameters.Add("comment",  NpgsqlDbType.Text).Value     = string.IsNullOrWhiteSpace(p.Comment) ? (object)DBNull.Value : p.Comment.Trim();
        cmd.Parameters.Add("student",  NpgsqlDbType.Integer).Value  = p.IdStudent.HasValue ? (object)p.IdStudent.Value : DBNull.Value;
        cmd.Parameters.Add("discount", NpgsqlDbType.Numeric).Value  = p.DiscountPercent.HasValue ? (object)p.DiscountPercent.Value : DBNull.Value;
        cmd.Parameters.Add("base",     NpgsqlDbType.Numeric).Value  = p.AmountBase.HasValue ? (object)p.AmountBase.Value : DBNull.Value;
        BindExtraPaymentFields(cmd, p);

        var idObj = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(idObj);
    }

    public async Task UpdateAsync(Payment p)
    {
        FinanceSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
UPDATE public.payment
SET payment_date        = @dt,
    id_contract         = @contract,
    amount              = @amount,
    id_payment_method   = @method,
    id_service_type     = @service,
    cash_receipt_issued = @receipt,
    period_month        = @pm,
    period_year         = @py,
    comment             = @comment,
    id_student          = @student,
    discount_percent    = @discount,
    amount_base         = @base,
    actual_date         = @actual,
    payment_kind        = @kind,
    event_text          = @event,
    period_label        = @period,
    id_manager          = @manager
WHERE id_payment = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id",       NpgsqlDbType.Integer).Value  = p.IdPayment;
        cmd.Parameters.Add("dt",       NpgsqlDbType.Date).Value     = p.PaymentDate.Date;
        cmd.Parameters.Add("contract", NpgsqlDbType.Integer).Value  = p.IdContract.HasValue ? (object)p.IdContract.Value : DBNull.Value;
        cmd.Parameters.Add("amount",   NpgsqlDbType.Numeric).Value  = p.Amount;
        cmd.Parameters.Add("method",   NpgsqlDbType.Integer).Value  = p.IdPaymentMethod;
        cmd.Parameters.Add("service",  NpgsqlDbType.Integer).Value  = p.IdServiceType;
        cmd.Parameters.Add("receipt",  NpgsqlDbType.Boolean).Value  = p.CashReceiptIssued;
        cmd.Parameters.Add("pm",       NpgsqlDbType.Smallint).Value = p.PeriodMonth.HasValue ? (object)p.PeriodMonth.Value : DBNull.Value;
        cmd.Parameters.Add("py",       NpgsqlDbType.Smallint).Value = p.PeriodYear.HasValue  ? (object)p.PeriodYear.Value  : DBNull.Value;
        cmd.Parameters.Add("comment",  NpgsqlDbType.Text).Value     = string.IsNullOrWhiteSpace(p.Comment) ? (object)DBNull.Value : p.Comment.Trim();
        cmd.Parameters.Add("student",  NpgsqlDbType.Integer).Value  = p.IdStudent.HasValue ? (object)p.IdStudent.Value : DBNull.Value;
        cmd.Parameters.Add("discount", NpgsqlDbType.Numeric).Value  = p.DiscountPercent.HasValue ? (object)p.DiscountPercent.Value : DBNull.Value;
        cmd.Parameters.Add("base",     NpgsqlDbType.Numeric).Value  = p.AmountBase.HasValue ? (object)p.AmountBase.Value : DBNull.Value;
        BindExtraPaymentFields(cmd, p);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int idPayment)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "DELETE FROM public.payment WHERE id_payment = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idPayment);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------- справочники ----------
    public async Task<IReadOnlyList<LookupItem>> GetPaymentMethodsAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "SELECT id_payment_method, method_name FROM public.payment_method ORDER BY method_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LookupItem
            {
                Id   = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1)
            });
        }
        return LookupDeduper.ByName(list);
    }

    private static void BindExtraPaymentFields(NpgsqlCommand cmd, Payment p)
    {
        cmd.Parameters.Add("actual", NpgsqlDbType.Date).Value =
            p.ActualDate.HasValue ? (object)p.ActualDate.Value.Date : p.PaymentDate.Date;
        cmd.Parameters.Add("kind", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(p.PaymentKind) ? DBNull.Value : p.PaymentKind.Trim();
        cmd.Parameters.Add("event", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(p.EventText) ? DBNull.Value : p.EventText.Trim();
        cmd.Parameters.Add("period", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(p.PeriodLabel) ? DBNull.Value : p.PeriodLabel.Trim();
        cmd.Parameters.Add("manager", NpgsqlDbType.Integer).Value =
            p.IdManager.HasValue ? (object)p.IdManager.Value : DBNull.Value;
    }

    public async Task<int> GetOrCreatePaymentMethodAsync(string name)
    {
        var trimmed = name.Trim();
        var existing = (await GetPaymentMethodsAsync())
            .FirstOrDefault(x => x.Name.Equals(trimmed, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
            return existing.Id;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
INSERT INTO public.payment_method (method_code, method_name)
VALUES (@code, @name)
RETURNING id_payment_method;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        var code = new string(trimmed.Where(char.IsLetterOrDigit).Take(20).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code))
            code = "pm" + DateTime.UtcNow.Ticks;
        cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
        cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = trimmed;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<string>> GetPeriodLabelsAsync()
    {
        FinanceSchema.Ensure();
        var list = PaymentPeriodNames.Months.ToList();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT period_label FROM public.payment WHERE period_label IS NOT NULL AND BTRIM(period_label) <> '' ORDER BY 1",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.GetString(0).Trim();
            if (list.All(x => !x.Equals(value, StringComparison.CurrentCultureIgnoreCase)))
                list.Add(value);
        }
        return list;
    }

    public async Task<IReadOnlyList<LookupItem>> GetServiceTypesAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "SELECT id_service_type, service_name FROM public.service_type ORDER BY service_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LookupItem
            {
                Id   = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1)
            });
        }
        return LookupDeduper.ByName(list);
    }

    public async Task<IReadOnlyList<LookupItem>> GetActiveContractsLookupAsync()
    {
        var list = new List<LookupItem>();
        DatabaseSchema.EnsureIfPossible();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT c.id_contract,
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name
FROM public.contract c
JOIN public.student s ON s.id_student = c.id_student
WHERE c.status NOT IN ('расторгнут', 'архивный', 'завершен')
ORDER BY student_full_name, c.id_contract;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var fio = reader.IsDBNull(1) ? "" : reader.GetString(1);
            list.Add(new LookupItem
            {
                Id   = id,
                Name = LookupComboHelper.ContractLabel(id.ToString(), fio)
            });
        }
        return list
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<decimal> GetTotalForPeriodAsync(DateTime? from, DateTime? to)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COALESCE(SUM(amount), 0)
FROM public.payment
WHERE (@from IS NULL OR payment_date >= @from)
  AND (@to   IS NULL OR payment_date <= @to);";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from.HasValue ? (object)from.Value.Date : DBNull.Value;
        cmd.Parameters.Add("to",   NpgsqlDbType.Date).Value = to.HasValue   ? (object)to.Value.Date   : DBNull.Value;

        var v = await cmd.ExecuteScalarAsync();
        return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);
    }
}
