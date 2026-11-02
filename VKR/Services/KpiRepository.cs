#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

/// <summary>
/// Репозиторий планов и фактических KPI.
///
/// ВСЕ SQL-запросы используют актуальные имена таблиц/полей:
///   lead_request.created_date          (а не data_zayavki)
///   lead_request.status                (а не status_zayavki)
///   contract.contract_date             (а не data_zaklyucheniya)
///   student.arrival_date               (а не data_registracii)
///   payment.payment_date, payment.amount (а не platezh.data_platezha/summa_platezha)
///   debt.debt_amount/paid_amount/status (а не zadolzhennost.summa_*)
///   attendance.lesson_date/status      (а не poseshchaemost.*)
///
/// Все блоки расчёта обёрнуты в try/catch: если данных нет — возвращаем 0
/// и не валим окно «Планы и KPI».
/// </summary>
public sealed class KpiRepository
{
    // -----------------------------------------------------------------
    // CRUD для kpi_plan
    // -----------------------------------------------------------------
    public async Task<IReadOnlyList<KpiPlan>> GetAllPlansAsync()
    {
        var result = new List<KpiPlan>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT id_kpi_plan, period_start, period_end, plan_name,
       planned_leads, planned_new_students, planned_contracts,
       planned_revenue, planned_attendance_percent, planned_max_churn,
       created_at
FROM public.kpi_plan
ORDER BY period_start DESC;", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(ReadPlan(reader));
        }
        return result;
    }

    public async Task<KpiPlan?> GetPlanByIdAsync(int idKpiPlan)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT id_kpi_plan, period_start, period_end, plan_name,
       planned_leads, planned_new_students, planned_contracts,
       planned_revenue, planned_attendance_percent, planned_max_churn,
       created_at
FROM public.kpi_plan
WHERE id_kpi_plan = @id;", connection);
        command.Parameters.AddWithValue("id", idKpiPlan);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPlan(reader) : null;
    }

    public async Task<int> SavePlanAsync(KpiPlan plan)
    {
        if (plan.PeriodEnd < plan.PeriodStart)
            plan.PeriodEnd = plan.PeriodStart;
        if (string.IsNullOrWhiteSpace(plan.PlanName))
            plan.PlanName = OptionalFieldDefaults.DictionaryNameOrDefault(null);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (plan.IdKpiPlan == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.kpi_plan (period_start, period_end, plan_name,
    planned_leads, planned_new_students, planned_contracts,
    planned_revenue, planned_attendance_percent, planned_max_churn)
VALUES (@period_start, @period_end, @plan_name,
    @planned_leads, @planned_new_students, @planned_contracts,
    @planned_revenue, @planned_attendance_percent, @planned_max_churn)
RETURNING id_kpi_plan;", connection);

            FillPlanParameters(command, plan);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        else
        {
            await using var command = new NpgsqlCommand(@"
UPDATE public.kpi_plan
SET period_start = @period_start,
    period_end = @period_end,
    plan_name = @plan_name,
    planned_leads = @planned_leads,
    planned_new_students = @planned_new_students,
    planned_contracts = @planned_contracts,
    planned_revenue = @planned_revenue,
    planned_attendance_percent = @planned_attendance_percent,
    planned_max_churn = @planned_max_churn
WHERE id_kpi_plan = @id;", connection);

            command.Parameters.AddWithValue("id", plan.IdKpiPlan);
            FillPlanParameters(command, plan);

            await command.ExecuteNonQueryAsync();
            return plan.IdKpiPlan;
        }
    }

    public async Task DeletePlanAsync(int idKpiPlan)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.kpi_plan WHERE id_kpi_plan = @id;", connection);
        command.Parameters.AddWithValue("id", idKpiPlan);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Находит KPI-план, пересекающийся с указанным периодом (последний по дате начала).
    /// </summary>
    public async Task<KpiPlan?> FindPlanForPeriodAsync(DateTime periodStart, DateTime periodEnd)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT id_kpi_plan, period_start, period_end, plan_name,
       planned_leads, planned_new_students, planned_contracts,
       planned_revenue, planned_attendance_percent, planned_max_churn,
       created_at
FROM public.kpi_plan
WHERE period_start::date <= @end AND period_end::date >= @start
ORDER BY period_start DESC
LIMIT 1;", connection);
        command.Parameters.AddWithValue("start", periodStart.Date);
        command.Parameters.AddWithValue("end", periodEnd.Date);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPlan(reader) : null;
    }

    // -----------------------------------------------------------------
    // Расчёт фактических KPI за период
    // -----------------------------------------------------------------
    public async Task<KpiFact> CalculateFactAsync(DateTime periodStart, DateTime periodEnd)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var fact = new KpiFact();

        // 1. Фактические заявки за период
        fact.FactLeads = await ScalarSafeAsync<int>(connection, @"
SELECT COUNT(*)
FROM public.lead_request
WHERE created_date::date BETWEEN @start AND @end;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 2. Новые договоры за период
        fact.FactContracts = await ScalarSafeAsync<int>(connection, @"
SELECT COUNT(*)
FROM public.contract
WHERE contract_date BETWEEN @start AND @end;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 3. Новые обучающиеся (используем arrival_date)
        fact.FactNewStudents = await ScalarSafeAsync<int>(connection, @"
SELECT COUNT(*)
FROM public.student
WHERE arrival_date BETWEEN @start AND @end;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 4. Выручка за период: сумма договоров + продажи магазина.
        //    Здесь считаем именно стоимость договоров из public.contract и продажи из public.shop_sale,
        //    чтобы KPI совпадали с главной страницей и разделами «Договоры»/«Продажи».
        fact.FactRevenue = await ScalarSafeAsync<decimal>(connection, @"
SELECT
    COALESCE((
        SELECT SUM(c.total_amount)
        FROM public.contract c
        WHERE c.contract_date BETWEEN @start AND @end
    ), 0)
    +
    COALESCE((
        SELECT SUM(COALESCE(s.total_amount, s.unit_price * s.quantity, s.sale_price * s.quantity, 0))
        FROM public.shop_sale s
        WHERE s.sale_date BETWEEN @start AND @end
    ), 0);",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 5. Активная задолженность на текущий момент (debt.debt_amount - debt.paid_amount)
        //    Согласно CHECK debt_status_check разрешены: 'активна', 'частично_погашена',
        //    'погашена', 'списана'. Считаем долгом всё непогашенное.
        var activeDebt = await ScalarSafeAsync<decimal>(connection, @"
SELECT COALESCE(SUM(debt_amount - paid_amount), 0)
FROM public.debt
WHERE status IN ('активна', 'частично_погашена');");
        fact.FactActiveDebt = (int)Math.Round(activeDebt);

        // 6. Посещаемость за период
        //    По CHECK attendance_status_check разрешены значения:
        //    'присутствовал', 'отсутствовал', 'уважительная'.
        fact.FactAttendancePercent = await ScalarSafeAsync<decimal>(connection, @"
SELECT CASE
    WHEN COUNT(*) = 0 THEN 0
    ELSE ROUND(
        100.0 * SUM(CASE WHEN status = 'присутствовал' THEN 1 ELSE 0 END) / COUNT(*),
        2)::numeric
END
FROM public.attendance
WHERE lesson_date BETWEEN @start AND @end;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 7. Отток за период:
        //    - студенты, у которых есть событие ухода в периоде
        //    - либо договор расторгнут в периоде
        //    Это надёжнее, чем смотреть только на статус 'архивный' без даты архивации.
        fact.FactChurn = await ScalarSafeAsync<int>(connection, @"
SELECT COUNT(DISTINCT id_student) FROM (
    SELECT se.id_student
    FROM public.student_event se
    JOIN public.event_type et ON et.id_event_type = se.id_event_type
    WHERE se.event_date BETWEEN @start AND @end
      AND (LOWER(et.event_name) LIKE '%уход%'
           OR LOWER(et.event_name) LIKE '%отчисл%'
           OR LOWER(et.event_name) LIKE '%приостанов%')
    UNION
    SELECT c.id_student
    FROM public.contract c
    JOIN public.contract_termination ct ON ct.id_contract = c.id_contract
    WHERE ct.termination_date BETWEEN @start AND @end
) AS churned;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        // 8. Конверсия заявок за период = (заявок со статусом 'конвертирована') / (всех заявок) * 100
        //    Используется ЕДИНЫЙ статус «конвертирована», см. соглашение по статусам.
        fact.FactConversionPercent = await ScalarSafeAsync<decimal>(connection, @"
SELECT CASE
    WHEN COUNT(*) = 0 THEN 0
    ELSE ROUND(
        100.0 * SUM(CASE WHEN status = 'конвертирована' THEN 1 ELSE 0 END) / COUNT(*),
        2)::numeric
END
FROM public.lead_request
WHERE created_date::date BETWEEN @start AND @end;",
            ("start", periodStart.Date), ("end", periodEnd.Date));

        return fact;
    }

    // -----------------------------------------------------------------
    // Внутренние утилиты
    // -----------------------------------------------------------------
    private static KpiPlan ReadPlan(NpgsqlDataReader reader) => new KpiPlan
    {
        IdKpiPlan                 = (int)reader["id_kpi_plan"],
        PeriodStart               = ToDateTime(reader["period_start"]),
        PeriodEnd                 = ToDateTime(reader["period_end"]),
        PlanName                  = (string)reader["plan_name"],
        PlannedLeads              = (int)reader["planned_leads"],
        PlannedNewStudents        = (int)reader["planned_new_students"],
        PlannedContracts          = (int)reader["planned_contracts"],
        PlannedRevenue            = (decimal)reader["planned_revenue"],
        PlannedAttendancePercent  = (decimal)reader["planned_attendance_percent"],
        PlannedMaxChurn           = (int)reader["planned_max_churn"],
        CreatedAt                 = ToDateTime(reader["created_at"])
    };

    /// <summary>
    /// Безопасная конвертация значения в DateTime.
    /// В новых версиях Npgsql колонки типа date возвращаются как DateOnly,
    /// а timestamp — как DateTime. Прямое приведение (DateTime)value падает
    /// на DateOnly с InvalidCastException, поэтому обрабатываем оба варианта.
    /// </summary>
    private static DateTime ToDateTime(object value)
    {
        if (value is DateTime dt) return dt;
        if (value is DateOnly d)  return d.ToDateTime(TimeOnly.MinValue);
        return Convert.ToDateTime(value);
    }

    private static void FillPlanParameters(NpgsqlCommand command, KpiPlan plan)
    {
        command.Parameters.Add("period_start", NpgsqlTypes.NpgsqlDbType.Date).Value = plan.PeriodStart.Date;
        command.Parameters.Add("period_end",   NpgsqlTypes.NpgsqlDbType.Date).Value = plan.PeriodEnd.Date;
        command.Parameters.AddWithValue("plan_name", plan.PlanName);
        command.Parameters.AddWithValue("planned_leads", plan.PlannedLeads);
        command.Parameters.AddWithValue("planned_new_students", plan.PlannedNewStudents);
        command.Parameters.AddWithValue("planned_contracts", plan.PlannedContracts);
        command.Parameters.AddWithValue("planned_revenue", plan.PlannedRevenue);
        command.Parameters.AddWithValue("planned_attendance_percent", plan.PlannedAttendancePercent);
        command.Parameters.AddWithValue("planned_max_churn", plan.PlannedMaxChurn);
    }

    /// <summary>
    /// Защищённый вызов ExecuteScalar: возвращает default(T) при любой
    /// ошибке (например, отсутствует таблица или поле). Так окно «Планы
    /// и KPI» не падает, а просто показывает нули.
    /// </summary>
    private static async Task<T> ScalarSafeAsync<T>(
        NpgsqlConnection connection, string sql, params (string name, object value)[] parameters)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                // Для дат явно указываем NpgsqlDbType.Date, иначе в новых
                // версиях Npgsql при сравнении DateTime-параметра с колонкой
                // типа date может бросаться InvalidCastException.
                if (value is DateTime dt)
                    cmd.Parameters.Add(name, NpgsqlTypes.NpgsqlDbType.Date).Value = dt.Date;
                else
                    cmd.Parameters.AddWithValue(name, value);
            }

            var result = await cmd.ExecuteScalarAsync();
            if (result is null || result is DBNull)
                return default!;

            if (typeof(T) == typeof(int))    return (T)(object)Convert.ToInt32(result);
            if (typeof(T) == typeof(long))   return (T)(object)Convert.ToInt64(result);
            if (typeof(T) == typeof(decimal)) return (T)(object)Convert.ToDecimal(result);
            if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(result);

            return (T)result;
        }
        catch
        {
            // Намеренно глушим: задача метода — не уронить окно.
            // Конкретную диагностику можно посмотреть через расчёт по одной метрике.
            return default!;
        }
    }
}
