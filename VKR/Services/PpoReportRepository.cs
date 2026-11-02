#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class PpoReportRepository
{
    public async Task<IReadOnlyList<PpoDailyIncomeRow>> GetDailyIncomeAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<PpoDailyIncomeRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT d::date AS income_date, method_name, method_code, SUM(amount) AS total
FROM (
    SELECT p.payment_date::date AS d, pm.method_name, pm.method_code, p.amount
    FROM public.payment p
    LEFT JOIN public.payment_method pm ON pm.id_payment_method = p.id_payment_method
    WHERE p.payment_date::date BETWEEN @from AND @to

    UNION ALL

    SELECT s.sale_date::date AS d, pm.method_name, pm.method_code,
           COALESCE(s.total_amount, s.sale_price * s.quantity) AS amount
    FROM public.shop_sale s
    LEFT JOIN public.payment_method pm ON pm.id_payment_method = s.id_payment_method
    WHERE s.sale_date::date BETWEEN @from AND @to
) t
GROUP BY d, method_name, method_code
ORDER BY d;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var date = reader.GetDateTime(0);
            var methodName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var methodCode = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var amount = reader.GetDecimal(3);
            if (amount == 0)
                continue;

            result.Add(new PpoDailyIncomeRow
            {
                Date = date,
                Channel = PpoPaymentChannelMapper.Resolve(methodName, methodCode),
                Amount = amount
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<PpoExpenseExportRow>> GetExpensesAsync(DateTime dateFrom, DateTime dateTo)
    {
        var result = new List<PpoExpenseExportRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT e.id_expense,
       e.expense_date,
       COALESCE(NULLIF(TRIM(e.description), ''), et.expense_name, '') AS vendor_name,
       e.amount,
       COALESCE(NULLIF(TRIM(e.comment), ''), et.expense_name, '') AS details,
       COALESCE(et.expense_name, '') AS category_name
FROM public.expense e
LEFT JOIN public.expense_type et ON et.id_expense_type = e.id_expense_type
WHERE e.expense_date::date BETWEEN @from AND @to
ORDER BY e.expense_date, e.id_expense;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var category = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var vendor = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var details = reader.IsDBNull(4) ? "" : reader.GetString(4);

            result.Add(new PpoExpenseExportRow
            {
                IdExpense = reader.GetInt32(0),
                ExpenseDate = reader.GetDateTime(1),
                VendorName = vendor,
                Amount = reader.GetDecimal(3),
                Details = details,
                OrgCode = PpoExpenseCategoryMapper.ResolveOrgCode(category, vendor)
            });
        }

        return result;
    }
}
