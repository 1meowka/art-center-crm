#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class ContractRenewalRepository
{
    public async Task<int> CountDueAsync()
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(CountDueSql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<ContractRenewalDueItem>> GetDueRemindersAsync()
    {
        var list = new List<ContractRenewalDueItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(ListDueSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ContractRenewalDueItem
            {
                IdStudent = ReadInt(reader, "id_student"),
                IdContract = ReadInt(reader, "id_contract"),
                StudentName = ReadString(reader, "student_name"),
                ContractNumber = ReadString(reader, "contract_number"),
                DirectionName = ReadString(reader, "direction_name"),
                ContractEndDate = ReadDateTime(reader, "end_date"),
                RemindFromDate = ReadDateTime(reader, "remind_from"),
                DaysUntilEnd = ReadInt(reader, "days_until_end"),
            });
        }
        return list;
    }

    public async Task SaveReminderAsync(ContractRenewalInput input)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        const string sql = @"
INSERT INTO public.contract_renewal_reminder (
    id_contract, id_student, contract_end_date, will_renew,
    is_completed, comment, completed_at, id_completed_by)
VALUES (
    @id_contract, @id_student, @end_date, @will_renew,
    TRUE, @comment, CURRENT_TIMESTAMP, @id_user)
ON CONFLICT (id_contract, contract_end_date)
DO UPDATE SET
    will_renew      = EXCLUDED.will_renew,
    is_completed    = TRUE,
    comment         = EXCLUDED.comment,
    completed_at    = EXCLUDED.completed_at,
    id_completed_by = EXCLUDED.id_completed_by;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id_contract", NpgsqlDbType.Integer).Value = input.IdContract;
        cmd.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = input.IdStudent;
        cmd.Parameters.Add("end_date", NpgsqlDbType.Date).Value = input.ContractEndDate.Date;
        cmd.Parameters.Add("will_renew", NpgsqlDbType.Boolean).Value =
            input.WillRenew.HasValue ? input.WillRenew.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(input.Comment) ? DBNull.Value : input.Comment.Trim();
        cmd.Parameters.Add("id_user", NpgsqlDbType.Integer).Value =
            input.IdCompletedBy.HasValue ? input.IdCompletedBy.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ContractRenewalStatsSummary> GetStatsSummaryAsync(DateTime fromDate, DateTime toDate)
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(SummarySql, connection);
        cmd.Parameters.Add("from_date", NpgsqlDbType.Date).Value = fromDate.Date;
        cmd.Parameters.Add("to_date", NpgsqlDbType.Date).Value = toDate.Date;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new ContractRenewalStatsSummary();

        return new ContractRenewalStatsSummary
        {
            Total = ReadInt(reader, "total"),
            YesCount = ReadInt(reader, "yes_count"),
            NoCount = ReadInt(reader, "no_count"),
            UnknownCount = ReadInt(reader, "unknown_count"),
        };
    }

    public async Task<IReadOnlyList<ContractRenewalHistoryItem>> GetHistoryAsync(DateTime fromDate, DateTime toDate)
    {
        NotificationSchema.Ensure();
        var list = new List<ContractRenewalHistoryItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(HistorySql, connection);
        cmd.Parameters.Add("from_date", NpgsqlDbType.Date).Value = fromDate.Date;
        cmd.Parameters.Add("to_date", NpgsqlDbType.Date).Value = toDate.Date;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ContractRenewalHistoryItem
            {
                StudentName = ReadString(reader, "student_name"),
                ContractNumber = ReadString(reader, "contract_number"),
                DirectionName = ReadString(reader, "direction_name"),
                ContractEndDate = ReadDateTime(reader, "contract_end_date"),
                WillRenew = ReadNullableBool(reader, "will_renew"),
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
    COUNT(*) FILTER (WHERE crr.will_renew = TRUE)::int AS yes_count,
    COUNT(*) FILTER (WHERE crr.will_renew = FALSE)::int AS no_count,
    COUNT(*) FILTER (WHERE crr.will_renew IS NULL)::int AS unknown_count
FROM public.contract_renewal_reminder crr
WHERE crr.is_completed = TRUE
  AND crr.completed_at IS NOT NULL
  AND crr.completed_at::date >= @from_date
  AND crr.completed_at::date <= @to_date;";

    private const string HistorySql = @"
SELECT
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_name,
    c.contract_number,
    d.direction_name,
    crr.contract_end_date,
    crr.will_renew,
    COALESCE(crr.comment, '') AS comment,
    crr.completed_at,
    COALESCE(TRIM(u.last_name || ' ' || u.first_name), '') AS completed_by_name
FROM public.contract_renewal_reminder crr
INNER JOIN public.student s ON s.id_student = crr.id_student
INNER JOIN public.contract c ON c.id_contract = crr.id_contract
INNER JOIN public.direction d ON d.id_direction = c.id_direction
LEFT JOIN public.app_user u ON u.id_user = crr.id_completed_by
WHERE crr.is_completed = TRUE
  AND crr.completed_at IS NOT NULL
  AND crr.completed_at::date >= @from_date
  AND crr.completed_at::date <= @to_date
ORDER BY crr.completed_at DESC, crr.id_reminder DESC;";

    private const string SnoozeFilter = @"
AND NOT EXISTS (
    SELECT 1 FROM public.notification_snooze ns
    WHERE ns.task_key = 'renew:' || ec.id_contract || ':' || to_char(ec.end_date, 'YYYY-MM-DD')
      AND ns.snoozed_until >= CURRENT_DATE
)";

    private const string CompletedFilter = @"
AND NOT EXISTS (
    SELECT 1 FROM public.contract_renewal_reminder crr
    WHERE crr.id_contract = ec.id_contract
      AND crr.contract_end_date = ec.end_date
      AND crr.is_completed = TRUE
)";

    private const string EndingContractsCte = @"
WITH ending_contracts AS (
    SELECT
        c.id_contract,
        c.id_student,
        c.contract_number,
        c.end_date,
        (c.end_date - INTERVAL '7 days')::date AS remind_from,
        (c.end_date - CURRENT_DATE)::int AS days_until_end,
        TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_name,
        d.direction_name
    FROM public.contract c
    INNER JOIN public.student s ON s.id_student = c.id_student
    INNER JOIN public.direction d ON d.id_direction = c.id_direction
    WHERE c.status = 'активен'
      AND s.status = 'активный'
      AND c.end_date IS NOT NULL
      AND CURRENT_DATE >= (c.end_date - INTERVAL '7 days')::date
)";

    private const string CountDueSql = EndingContractsCte + @"
SELECT COUNT(*)::int
FROM ending_contracts ec
WHERE 1 = 1 " + CompletedFilter + SnoozeFilter + ";";

    private const string ListDueSql = EndingContractsCte + @"
SELECT
    ec.id_student,
    ec.id_contract,
    ec.student_name,
    ec.contract_number,
    ec.direction_name,
    ec.end_date,
    ec.remind_from,
    ec.days_until_end
FROM ending_contracts ec
WHERE 1 = 1 " + CompletedFilter + SnoozeFilter + @"
ORDER BY ec.end_date, ec.student_name;";

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

    private static bool? ReadNullableBool(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? null : r.GetBoolean(i);
    }
}
