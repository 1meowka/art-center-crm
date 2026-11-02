#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class SaleRepository
{
    public async Task<IReadOnlyList<SaleListItem>> GetSalesAsync(
        string? search = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? idStudent = null,
        int? year = null,
        SaleTypeFilter saleTypeFilter = SaleTypeFilter.All,
        SaleSortKey sortKey = SaleSortKey.DateDesc)
    {
        var list = new List<SaleListItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var orderBy = sortKey switch
        {
            SaleSortKey.DateAsc => "s.sale_date ASC, s.id_sale ASC",
            SaleSortKey.AmountDesc => "total_amount DESC, s.sale_date DESC",
            SaleSortKey.AmountAsc => "total_amount ASC, s.sale_date DESC",
            SaleSortKey.ItemAsc => "item_name ASC, s.sale_date DESC",
            SaleSortKey.ItemDesc => "item_name DESC, s.sale_date DESC",
            SaleSortKey.StudentAsc => "client_full_name ASC, s.sale_date DESC",
            SaleSortKey.StudentDesc => "client_full_name DESC, s.sale_date DESC",
            _ => "s.sale_date DESC, s.id_sale DESC"
        };

        var sql = $@"
SELECT
    s.id_sale,
    s.sale_date,
    s.id_student,
    COALESCE(
        NULLIF(TRIM(COALESCE(st.last_name, '') || ' ' || COALESCE(st.first_name, '')
             || COALESCE(' ' || st.middle_name, '')), ''),
        NULLIF(TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')), ''),
        ''
    ) AS client_full_name,
    COALESCE(s.item_name, p.product_name, '')    AS item_name,
    s.quantity,
    COALESCE(s.unit_price, s.sale_price)         AS unit_price,
    COALESCE(s.total_amount, s.sale_price * s.quantity) AS total_amount,
    COALESCE(pm.method_name, '')                 AS method_name,
    s.comment
FROM public.shop_sale s
LEFT JOIN public.shop_product   p  ON p.id_product = s.id_product
LEFT JOIN public.student        st ON st.id_student = s.id_student
LEFT JOIN public.payment_method pm ON pm.id_payment_method = s.id_payment_method
LEFT JOIN public.lead_request   lr ON lr.id_lead = (
    NULLIF(substring(s.comment FROM 'Заявка №([0-9]+)'), '')::int
)
WHERE 1 = 1
  AND (@search IS NULL OR
       COALESCE(s.item_name, p.product_name, '') ILIKE '%' || @search || '%' OR
       COALESCE(
           NULLIF(TRIM(COALESCE(st.last_name, '') || ' ' || COALESCE(st.first_name, '')
                || COALESCE(' ' || st.middle_name, '')), ''),
           NULLIF(TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')), ''),
           ''
       ) ILIKE '%' || @search || '%')
  AND (@dateFrom  IS NULL OR s.sale_date >= @dateFrom)
  AND (@dateTo    IS NULL OR s.sale_date <= @dateTo)
  AND (@idStudent IS NULL OR s.id_student = @idStudent)
  AND (@year IS NULL OR EXTRACT(YEAR FROM s.sale_date)::int = @year)
  AND (@trialOnly = false OR COALESCE(s.item_name, '') ILIKE '%пробное%')
  AND (@otherOnly = false OR COALESCE(s.item_name, '') NOT ILIKE '%пробное%')
ORDER BY {orderBy};";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("search",    NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        cmd.Parameters.Add("dateFrom",  NpgsqlDbType.Date).Value    = dateFrom.HasValue  ? (object)dateFrom.Value.Date  : DBNull.Value;
        cmd.Parameters.Add("dateTo",    NpgsqlDbType.Date).Value    = dateTo.HasValue    ? (object)dateTo.Value.Date    : DBNull.Value;
        cmd.Parameters.Add("idStudent", NpgsqlDbType.Integer).Value = idStudent.HasValue ? (object)idStudent.Value      : DBNull.Value;
        cmd.Parameters.Add("year", NpgsqlDbType.Integer).Value = year.HasValue ? (object)year.Value : DBNull.Value;
        cmd.Parameters.Add("trialOnly", NpgsqlDbType.Boolean).Value = saleTypeFilter == SaleTypeFilter.TrialOnly;
        cmd.Parameters.Add("otherOnly", NpgsqlDbType.Boolean).Value = saleTypeFilter == SaleTypeFilter.OtherOnly;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SaleListItem
            {
                IdSale            = reader.GetInt32(0),
                SaleDate          = reader.GetDateTime(1),
                IdStudent         = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                ClientFullName    = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudentFullName   = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ItemName          = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Quantity          = reader.GetInt32(5),
                UnitPrice         = reader.GetDecimal(6),
                TotalAmount       = reader.GetDecimal(7),
                PaymentMethodName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Comment           = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<int>> GetSaleYearsAsync()
    {
        var years = new List<int>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT EXTRACT(YEAR FROM sale_date)::int AS y FROM public.shop_sale ORDER BY y DESC;",
            connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            years.Add(reader.GetInt32(0));

        if (years.Count == 0)
            years.Add(DateTime.Today.Year);

        return years;
    }

    public async Task<Sale?> GetByIdAsync(int idSale)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_sale, sale_date, id_student, id_product,
       COALESCE(item_name, ''), quantity,
       COALESCE(unit_price, sale_price)             AS unit_price,
       COALESCE(total_amount, sale_price * quantity) AS total_amount,
       id_payment_method, comment
FROM public.shop_sale
WHERE id_sale = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idSale);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Sale
        {
            IdSale          = reader.GetInt32(0),
            SaleDate        = reader.GetDateTime(1),
            IdStudent       = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            IdProduct       = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            ItemName        = reader.IsDBNull(4) ? "" : reader.GetString(4),
            Quantity        = reader.GetInt32(5),
            UnitPrice       = reader.GetDecimal(6),
            TotalAmount     = reader.GetDecimal(7),
            IdPaymentMethod = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Comment         = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }

    public async Task<int> CreateAsync(Sale s)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        // sale_price оставляем равным unit_price для совместимости со старой схемой
        const string sql = @"
INSERT INTO public.shop_sale
(sale_date, id_student, id_product, item_name, quantity,
 sale_price, unit_price, total_amount, id_payment_method, comment, created_at)
VALUES
(@dt, @stud, @prod, @name, @qty,
 @price, @price, @total, @method, @comment, CURRENT_TIMESTAMP)
RETURNING id_sale;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("dt",      NpgsqlDbType.Date).Value    = s.SaleDate.Date;
        cmd.Parameters.Add("stud",    NpgsqlDbType.Integer).Value = s.IdStudent.HasValue ? (object)s.IdStudent.Value : DBNull.Value;
        cmd.Parameters.Add("prod",    NpgsqlDbType.Integer).Value = s.IdProduct.HasValue ? (object)s.IdProduct.Value : DBNull.Value;
        cmd.Parameters.Add("name",    NpgsqlDbType.Varchar).Value = string.IsNullOrWhiteSpace(s.ItemName) ? (object)DBNull.Value : s.ItemName.Trim();
        cmd.Parameters.Add("qty",     NpgsqlDbType.Integer).Value = s.Quantity;
        cmd.Parameters.Add("price",   NpgsqlDbType.Numeric).Value = s.UnitPrice;
        cmd.Parameters.Add("total",   NpgsqlDbType.Numeric).Value = s.TotalAmount;
        cmd.Parameters.Add("method",  NpgsqlDbType.Integer).Value = s.IdPaymentMethod.HasValue ? (object)s.IdPaymentMethod.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(s.Comment) ? (object)DBNull.Value : s.Comment.Trim();

        var idObj = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(idObj);
    }

    public async Task UpdateAsync(Sale s)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
UPDATE public.shop_sale
SET sale_date         = @dt,
    id_student        = @stud,
    id_product        = @prod,
    item_name         = @name,
    quantity          = @qty,
    sale_price        = @price,
    unit_price        = @price,
    total_amount      = @total,
    id_payment_method = @method,
    comment           = @comment
WHERE id_sale = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id",      NpgsqlDbType.Integer).Value = s.IdSale;
        cmd.Parameters.Add("dt",      NpgsqlDbType.Date).Value    = s.SaleDate.Date;
        cmd.Parameters.Add("stud",    NpgsqlDbType.Integer).Value = s.IdStudent.HasValue ? (object)s.IdStudent.Value : DBNull.Value;
        cmd.Parameters.Add("prod",    NpgsqlDbType.Integer).Value = s.IdProduct.HasValue ? (object)s.IdProduct.Value : DBNull.Value;
        cmd.Parameters.Add("name",    NpgsqlDbType.Varchar).Value = string.IsNullOrWhiteSpace(s.ItemName) ? (object)DBNull.Value : s.ItemName.Trim();
        cmd.Parameters.Add("qty",     NpgsqlDbType.Integer).Value = s.Quantity;
        cmd.Parameters.Add("price",   NpgsqlDbType.Numeric).Value = s.UnitPrice;
        cmd.Parameters.Add("total",   NpgsqlDbType.Numeric).Value = s.TotalAmount;
        cmd.Parameters.Add("method",  NpgsqlDbType.Integer).Value = s.IdPaymentMethod.HasValue ? (object)s.IdPaymentMethod.Value : DBNull.Value;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value    = string.IsNullOrWhiteSpace(s.Comment) ? (object)DBNull.Value : s.Comment.Trim();

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int idSale)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = "DELETE FROM public.shop_sale WHERE id_sale = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idSale);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<decimal> GetTotalForPeriodAsync(DateTime? from, DateTime? to)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COALESCE(SUM(COALESCE(total_amount, sale_price * quantity)), 0)
FROM public.shop_sale
WHERE (@from IS NULL OR sale_date >= @from)
  AND (@to   IS NULL OR sale_date <= @to);";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = from.HasValue ? (object)from.Value.Date : DBNull.Value;
        cmd.Parameters.Add("to",   NpgsqlDbType.Date).Value = to.HasValue   ? (object)to.Value.Date   : DBNull.Value;

        var v = await cmd.ExecuteScalarAsync();
        return v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);
    }

    // ---------- справочники ----------
    public async Task<IReadOnlyList<LookupItem>> GetProductsLookupAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_product, product_name
FROM public.shop_product
WHERE is_active = true
ORDER BY product_name;";

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
