#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class ExpenseRepository
{
    public async Task<IReadOnlyList<ExpenseListItem>> GetExpensesAsync(
        string? search = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? idCategory = null)
    {
        var list = new List<ExpenseListItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    e.id_expense,
    e.expense_date,
    COALESCE(et.expense_name, '') AS category,
    e.amount,
    COALESCE(e.description, '')   AS description,
    COALESCE(pm.method_name, '')  AS method_name,
    e.comment
FROM public.expense e
LEFT JOIN public.expense_type   et ON et.id_expense_type   = e.id_expense_type
LEFT JOIN public.payment_method pm ON pm.id_payment_method = e.id_payment_method
WHERE 1 = 1
  AND (@search     IS NULL OR
       COALESCE(e.description, '') ILIKE '%' || @search || '%' OR
       COALESCE(et.expense_name, '') ILIKE '%' || @search || '%')
  AND (@dateFrom   IS NULL OR e.expense_date    >= @dateFrom)
  AND (@dateTo     IS NULL OR e.expense_date    <= @dateTo)
  AND (@idCategory IS NULL OR e.id_expense_type  = @idCategory)
ORDER BY e.expense_date DESC, e.id_expense DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("search",     NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        cmd.Parameters.Add("dateFrom",   NpgsqlDbType.Date).Value    = dateFrom.HasValue   ? (object)dateFrom.Value.Date   : DBNull.Value;
        cmd.Parameters.Add("dateTo",     NpgsqlDbType.Date).Value    = dateTo.HasValue     ? (object)dateTo.Value.Date     : DBNull.Value;
        cmd.Parameters.Add("idCategory", NpgsqlDbType.Integer).Value = idCategory.HasValue ? (object)idCategory.Value      : DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ExpenseListItem
            {
                IdExpense         = reader.GetInt32(0),
                ExpenseDate       = reader.GetDateTime(1),
                CategoryName      = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Amount            = reader.GetDecimal(3),
                Description       = reader.IsDBNull(4) ? "" : reader.GetString(4),
                PaymentMethodName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Comment           = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task<Expense?> GetByIdAsync(int idExpense)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_expense, expense_date, id_expense_type, amount,
       COALESCE(description, ''), id_payment_method, comment, id_created_by, created_at
FROM public.expense
WHERE id_expense = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idExpense);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Expense
        {
            IdExpense       = reader.GetInt32(0),
            ExpenseDate     = reader.GetDateTime(1),
            IdExpenseType   = reader.GetInt32(2),
            Amount          = reader.GetDecimal(3),
            Description     = reader.IsDBNull(4) ? "" : reader.GetString(4),
            IdPaymentMethod = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Comment         = reader.IsDBNull(6) ? null : reader.GetString(6),
            IdCreatedBy     = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            CreatedAt       = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8)
        };
    }

    public async Task<int> CreateAsync(Expense e)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO public.expense
(expense_date, id_expense_type, amount, description, id_payment_method, comment, id_created_by, created_at)
VALUES
(@dt, @cat, @amount, @descr, @method, @comment, @creator, CURRENT_TIMESTAMP)
RETURNING id_expense;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("dt",      NpgsqlDbType.Date).Value    = e.ExpenseDate.Date;
        cmd.Parameters.Add("cat",     NpgsqlDbType.Integer).Value = e.IdExpenseType;
        cmd.Parameters.Add("amount",  NpgsqlDbType.Numeric).Value = e.Amount;
        cmd.Parameters.Add("descr",   NpgsqlDbType.Text).Value    = e.Description ?? "";
        cmd.Parameters.Add("method",  NpgsqlDbType.Integer).Value = e.IdPaymentMethod.HasValue ? (object)e.IdPaymentMethod.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(e.Comment) ? (object)DBNull.Value : e.Comment.Trim();
        cmd.Parameters.Add("creator", NpgsqlDbType.Integer).Value = e.IdCreatedBy.HasValue ? (object)e.IdCreatedBy.Value : DBNull.Value;

        var idObj = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(idObj);
    }

    public async Task UpdateAsync(Expense e)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
UPDATE public.expense
SET expense_date      = @dt,
    id_expense_type   = @cat,
    amount            = @amount,
    description       = @descr,
    id_payment_method = @method,
    comment           = @comment
WHERE id_expense = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id",      NpgsqlDbType.Integer).Value = e.IdExpense;
        cmd.Parameters.Add("dt",      NpgsqlDbType.Date).Value    = e.ExpenseDate.Date;
        cmd.Parameters.Add("cat",     NpgsqlDbType.Integer).Value = e.IdExpenseType;
        cmd.Parameters.Add("amount",  NpgsqlDbType.Numeric).Value = e.Amount;
        cmd.Parameters.Add("descr",   NpgsqlDbType.Text).Value    = e.Description ?? "";
        cmd.Parameters.Add("method",  NpgsqlDbType.Integer).Value = e.IdPaymentMethod.HasValue ? (object)e.IdPaymentMethod.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(e.Comment) ? (object)DBNull.Value : e.Comment.Trim();

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int idExpense)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "DELETE FROM public.expense WHERE id_expense = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idExpense);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<decimal> GetTotalForPeriodAsync(DateTime? from, DateTime? to)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COALESCE(SUM(amount), 0)
FROM public.expense
WHERE (@from IS NULL OR expense_date >= @from)
  AND (@to   IS NULL OR expense_date <= @to);";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from.HasValue ? (object)from.Value.Date : DBNull.Value;
        cmd.Parameters.Add("to",   NpgsqlDbType.Date).Value = to.HasValue   ? (object)to.Value.Date   : DBNull.Value;

        var v = await cmd.ExecuteScalarAsync();
        return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);
    }

    public async Task<IReadOnlyList<ExpenseByCategory>> GetByCategoryAsync(DateTime? from, DateTime? to)
    {
        var list = new List<ExpenseByCategory>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COALESCE(et.expense_name, '— без категории —') AS name,
       COALESCE(SUM(e.amount), 0)                    AS total
FROM public.expense e
LEFT JOIN public.expense_type et ON et.id_expense_type = e.id_expense_type
WHERE (@from IS NULL OR e.expense_date >= @from)
  AND (@to   IS NULL OR e.expense_date <= @to)
GROUP BY et.expense_name
ORDER BY total DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from.HasValue ? (object)from.Value.Date : DBNull.Value;
        cmd.Parameters.Add("to",   NpgsqlDbType.Date).Value = to.HasValue   ? (object)to.Value.Date   : DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ExpenseByCategory
            {
                CategoryName = reader.IsDBNull(0) ? "" : reader.GetString(0),
                Total        = reader.GetDecimal(1)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<LookupItem>> GetCategoriesAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "SELECT id_expense_type, expense_name FROM public.expense_type ORDER BY expense_name;";

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
}
