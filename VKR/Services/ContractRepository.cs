#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class ContractRepository
{
    // -----------------------------------------------------------------
    // Список договоров (с агрегатом оплат и ФИО студента)
    // -----------------------------------------------------------------
    public async Task<IReadOnlyList<ContractListItem>> GetContractsAsync(
        string? search = null,
        string? status = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        var result = new List<ContractListItem>();

        DatabaseSchema.EnsureIfPossible();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var sql = @"
SELECT
    c.id_contract,
    c.contract_number,
    c.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
    c.id_representative,
    COALESCE(
        NULLIF(TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')), ''),
        payer.fio,
        ''
    ) AS representative_full_name,
    COALESCE(d.direction_name, c.direction_name_snapshot, '') AS direction_name,
    c.contract_date,
    c.start_date,
    c.end_date,
    c.total_amount,
    COALESCE((SELECT SUM(p.amount) FROM public.payment p WHERE p.id_contract = c.id_contract), 0) AS paid_amount,
    c.status,
    c.comment,
    c.id_branch,
    COALESCE(b.short_name, '') AS branch_name
FROM public.contract c
LEFT JOIN public.student         s ON s.id_student   = c.id_student
LEFT JOIN public.representative  r ON r.id_representative = c.id_representative
LEFT JOIN public.direction       d ON d.id_direction = c.id_direction
LEFT JOIN public.study_branch    b ON b.id_branch    = c.id_branch
LEFT JOIN LATERAL (
    SELECT TRIM(rr.last_name || ' ' || rr.first_name || COALESCE(' ' || rr.middle_name, '')) AS fio
    FROM public.student_representative sr
    JOIN public.representative rr ON rr.id_representative = sr.id_representative
    WHERE sr.id_student = c.id_student
    ORDER BY sr.is_main_payer DESC, sr.is_legal_representative DESC, sr.id_student_representative
    LIMIT 1
) payer ON TRUE
WHERE 1 = 1
  AND (@search IS NULL OR
       c.contract_number ILIKE '%' || @search || '%' OR
       s.last_name ILIKE '%' || @search || '%' OR
       s.first_name ILIKE '%' || @search || '%' OR
       COALESCE(s.middle_name, '') ILIKE '%' || @search || '%' OR
       TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) ILIKE '%' || @search || '%' OR
       TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) ILIKE '%' || @search || '%' OR
       COALESCE(payer.fio, '') ILIKE '%' || @search || '%' OR
       EXISTS (
           SELECT 1
           FROM public.student_representative sr2
           JOIN public.representative rr2 ON rr2.id_representative = sr2.id_representative
           WHERE sr2.id_student = c.id_student
             AND TRIM(rr2.last_name || ' ' || rr2.first_name || COALESCE(' ' || rr2.middle_name, ''))
                 ILIKE '%' || @search || '%'
       ) OR
       to_char(c.contract_date, 'DD.MM.YYYY') ILIKE '%' || @search || '%' OR
       to_char(c.contract_date, 'DD.MM.YY')   ILIKE '%' || @search || '%' OR
       to_char(c.contract_date, 'YYYY-MM-DD') ILIKE '%' || @search || '%' OR
       to_char(c.start_date,    'DD.MM.YYYY') ILIKE '%' || @search || '%' OR
       to_char(c.end_date,      'DD.MM.YYYY') ILIKE '%' || @search || '%')
  AND (@status   IS NULL OR c.status        = @status)
  AND (@dateFrom IS NULL OR c.contract_date >= @dateFrom)
  AND (@dateTo   IS NULL OR c.contract_date <= @dateTo)
ORDER BY c.contract_date DESC, c.id_contract DESC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("search",   NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        cmd.Parameters.Add("status",   NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(status) ? (object)DBNull.Value : status;
        cmd.Parameters.Add("dateFrom", NpgsqlDbType.Date).Value = dateFrom.HasValue ? (object)dateFrom.Value.Date : DBNull.Value;
        cmd.Parameters.Add("dateTo",   NpgsqlDbType.Date).Value = dateTo.HasValue   ? (object)dateTo.Value.Date   : DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ContractListItem
            {
                IdContract       = reader.GetInt32(reader.GetOrdinal("id_contract")),
                ContractNumber   = reader.GetString(reader.GetOrdinal("contract_number")),
                IdStudent        = reader.GetInt32(reader.GetOrdinal("id_student")),
                StudentFullName  = reader.IsDBNull(reader.GetOrdinal("student_full_name"))
                    ? "" : reader.GetString(reader.GetOrdinal("student_full_name")),
                IdRepresentative = reader.IsDBNull(reader.GetOrdinal("id_representative"))
                    ? null : reader.GetInt32(reader.GetOrdinal("id_representative")),
                RepresentativeFullName = reader.IsDBNull(reader.GetOrdinal("representative_full_name"))
                    ? "" : reader.GetString(reader.GetOrdinal("representative_full_name")),
                DirectionName    = reader.IsDBNull(reader.GetOrdinal("direction_name"))
                    ? "" : reader.GetString(reader.GetOrdinal("direction_name")),
                ContractDate     = reader.GetDateTime(reader.GetOrdinal("contract_date")),
                StartDate        = reader.IsDBNull(reader.GetOrdinal("start_date"))
                    ? null : reader.GetDateTime(reader.GetOrdinal("start_date")),
                EndDate          = reader.IsDBNull(reader.GetOrdinal("end_date"))
                    ? null : reader.GetDateTime(reader.GetOrdinal("end_date")),
                TotalAmount      = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                PaidAmount       = reader.GetDecimal(reader.GetOrdinal("paid_amount")),
                Status           = reader.IsDBNull(reader.GetOrdinal("status"))
                    ? "" : reader.GetString(reader.GetOrdinal("status")),
                Comment          = reader.IsDBNull(reader.GetOrdinal("comment"))
                    ? null : reader.GetString(reader.GetOrdinal("comment")),
                IdBranch         = reader.IsDBNull(reader.GetOrdinal("id_branch"))
                    ? 1 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("id_branch"))),
                BranchName       = reader.IsDBNull(reader.GetOrdinal("branch_name"))
                    ? "" : reader.GetString(reader.GetOrdinal("branch_name"))
            });
        }

        return result;
    }

    // -----------------------------------------------------------------
    // Один договор по id
    // -----------------------------------------------------------------
    public async Task<Contract?> GetByIdAsync(int idContract)
    {
        DatabaseSchema.EnsureIfPossible();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_contract, contract_number, contract_date, start_date, end_date,
       id_student, id_representative, id_direction, id_group, id_student_type,
       direction_name_snapshot,
       lessons_count, times_per_week, price_no_discount, discount_percent,
       discount_amount, total_amount, status, comment, id_created_by,
       created_at, updated_at, id_branch
FROM public.contract
WHERE id_contract = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idContract);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Contract
        {
            IdContract       = reader.GetInt32(0),
            ContractNumber   = reader.GetString(1),
            ContractDate     = reader.GetDateTime(2),
            StartDate        = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            EndDate          = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            IdStudent        = reader.GetInt32(5),
            IdRepresentative = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            IdDirection      = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            IdGroup          = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            IdStudentType    = reader.GetInt32(9),
            DirectionNameSnapshot = reader.IsDBNull(10) ? null : reader.GetString(10),
            LessonsCount     = reader.GetInt32(11),
            TimesPerWeek     = reader.GetInt16(12),
            PriceNoDiscount  = reader.GetDecimal(13),
            DiscountPercent  = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
            DiscountAmount   = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
            TotalAmount      = reader.GetDecimal(16),
            Status           = reader.GetString(17),
            Comment          = reader.IsDBNull(18) ? null : reader.GetString(18),
            IdCreatedBy      = reader.IsDBNull(19) ? null : reader.GetInt32(19),
            CreatedAt        = reader.IsDBNull(20) ? DateTime.MinValue : reader.GetDateTime(20),
            UpdatedAt        = reader.IsDBNull(21) ? null : reader.GetDateTime(21),
            IdBranch         = reader.IsDBNull(22) ? 1 : Convert.ToInt32(reader.GetValue(22))
        };
    }

    // -----------------------------------------------------------------
    // Создание
    // -----------------------------------------------------------------
    public async Task<string> GenerateNextContractNumberAsync(DateTime contractDate)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(id_contract), 0) + 1 FROM public.contract;", connection);
        var nextId = Convert.ToInt32(await command.ExecuteScalarAsync());
        return nextId.ToString();
    }

    public async Task<int> CreateAsync(Contract c)
    {
        FinanceSchema.Ensure();
        DatabaseSchema.EnsureIfPossible();
        c.DirectionNameSnapshot = await ResolveDirectionNameAsync(c.IdDirection, c.DirectionNameSnapshot);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO public.contract
(contract_number, contract_date, start_date, end_date,
 id_student, id_representative, id_direction, id_group, id_student_type,
 direction_name_snapshot,
 lessons_count, times_per_week, price_no_discount, discount_percent,
 discount_amount, total_amount, status, comment, id_created_by, updated_at, id_branch)
VALUES
(@num, @cdate, @sdate, @edate,
 @stud, @rep, @dir, @grp, @stype,
 @dir_name,
 @lc, @tpw, @price, @dperc,
 @damt, @total, @status, @comment, @creator, CURRENT_TIMESTAMP, @branch)
RETURNING id_contract;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("num",     NpgsqlDbType.Varchar).Value  = c.ContractNumber ?? "";
        cmd.Parameters.Add("cdate",   NpgsqlDbType.Date).Value     = c.ContractDate.Date;
        cmd.Parameters.Add("sdate",   NpgsqlDbType.Date).Value     = c.StartDate.HasValue ? (object)c.StartDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("edate",   NpgsqlDbType.Date).Value     = c.EndDate.HasValue   ? (object)c.EndDate.Value.Date   : DBNull.Value;
        cmd.Parameters.Add("stud",    NpgsqlDbType.Integer).Value  = c.IdStudent;
        cmd.Parameters.Add("rep",     NpgsqlDbType.Integer).Value  = c.IdRepresentative.HasValue ? (object)c.IdRepresentative.Value : DBNull.Value;
        cmd.Parameters.Add("dir", NpgsqlDbType.Integer).Value =
            c.IdDirection is > 0 ? c.IdDirection.Value : DBNull.Value;
        cmd.Parameters.Add("grp",     NpgsqlDbType.Integer).Value  = c.IdGroup.HasValue ? (object)c.IdGroup.Value : DBNull.Value;
        cmd.Parameters.Add("stype",   NpgsqlDbType.Integer).Value  = c.IdStudentType;
        cmd.Parameters.Add("dir_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(c.DirectionNameSnapshot) ? (object)DBNull.Value : c.DirectionNameSnapshot.Trim();
        cmd.Parameters.Add("lc",      NpgsqlDbType.Integer).Value  = c.LessonsCount;
        cmd.Parameters.Add("tpw",     NpgsqlDbType.Smallint).Value = c.TimesPerWeek;
        cmd.Parameters.Add("price",   NpgsqlDbType.Numeric).Value  = c.PriceNoDiscount;
        cmd.Parameters.Add("dperc",   NpgsqlDbType.Numeric).Value  = c.DiscountPercent.HasValue ? (object)c.DiscountPercent.Value : DBNull.Value;
        cmd.Parameters.Add("damt",    NpgsqlDbType.Numeric).Value  = c.DiscountAmount.HasValue  ? (object)c.DiscountAmount.Value  : DBNull.Value;
        cmd.Parameters.Add("total",   NpgsqlDbType.Numeric).Value  = c.TotalAmount;
        cmd.Parameters.Add("status",  NpgsqlDbType.Varchar).Value  = string.IsNullOrWhiteSpace(c.Status) ? "активен" : c.Status;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value     = string.IsNullOrWhiteSpace(c.Comment) ? (object)DBNull.Value : c.Comment.Trim();
        cmd.Parameters.Add("creator", NpgsqlDbType.Integer).Value  = c.IdCreatedBy.HasValue ? (object)c.IdCreatedBy.Value : DBNull.Value;
        cmd.Parameters.Add("branch",  NpgsqlDbType.Smallint).Value = c.IdBranch > 0 ? (short)c.IdBranch : (short)1;

        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await using (var sync = new NpgsqlCommand(
            "UPDATE public.contract SET contract_number = @id::text WHERE id_contract = @id;", connection))
        {
            sync.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
            await sync.ExecuteNonQueryAsync();
        }

        c.IdContract = id;
        c.ContractNumber = id.ToString();
        return id;
    }

    // -----------------------------------------------------------------
    // Обновление
    // -----------------------------------------------------------------
    public async Task UpdateAsync(Contract c)
    {
        FinanceSchema.Ensure();
        DatabaseSchema.EnsureIfPossible();
        c.DirectionNameSnapshot = await ResolveDirectionNameAsync(c.IdDirection, c.DirectionNameSnapshot);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
UPDATE public.contract
SET contract_number   = @num,
    contract_date     = @cdate,
    start_date        = @sdate,
    end_date          = @edate,
    id_student        = @stud,
    id_representative = @rep,
    id_direction      = @dir,
    id_group          = @grp,
    id_student_type   = @stype,
    direction_name_snapshot = @dir_name,
    lessons_count     = @lc,
    times_per_week    = @tpw,
    price_no_discount = @price,
    discount_percent  = @dperc,
    discount_amount   = @damt,
    total_amount      = @total,
    status            = @status,
    comment           = @comment,
    id_branch         = @branch,
    updated_at        = CURRENT_TIMESTAMP
WHERE id_contract = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id",      NpgsqlDbType.Integer).Value  = c.IdContract;
        cmd.Parameters.Add("num",     NpgsqlDbType.Varchar).Value  = c.ContractNumber ?? "";
        cmd.Parameters.Add("cdate",   NpgsqlDbType.Date).Value     = c.ContractDate.Date;
        cmd.Parameters.Add("sdate",   NpgsqlDbType.Date).Value     = c.StartDate.HasValue ? (object)c.StartDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("edate",   NpgsqlDbType.Date).Value     = c.EndDate.HasValue   ? (object)c.EndDate.Value.Date   : DBNull.Value;
        cmd.Parameters.Add("stud",    NpgsqlDbType.Integer).Value  = c.IdStudent;
        cmd.Parameters.Add("rep",     NpgsqlDbType.Integer).Value  = c.IdRepresentative.HasValue ? (object)c.IdRepresentative.Value : DBNull.Value;
        cmd.Parameters.Add("dir", NpgsqlDbType.Integer).Value =
            c.IdDirection is > 0 ? c.IdDirection.Value : DBNull.Value;
        cmd.Parameters.Add("grp",     NpgsqlDbType.Integer).Value  = c.IdGroup.HasValue ? (object)c.IdGroup.Value : DBNull.Value;
        cmd.Parameters.Add("stype",   NpgsqlDbType.Integer).Value  = c.IdStudentType;
        cmd.Parameters.Add("dir_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(c.DirectionNameSnapshot) ? (object)DBNull.Value : c.DirectionNameSnapshot.Trim();
        cmd.Parameters.Add("lc",      NpgsqlDbType.Integer).Value  = c.LessonsCount;
        cmd.Parameters.Add("tpw",     NpgsqlDbType.Smallint).Value = c.TimesPerWeek;
        cmd.Parameters.Add("price",   NpgsqlDbType.Numeric).Value  = c.PriceNoDiscount;
        cmd.Parameters.Add("dperc",   NpgsqlDbType.Numeric).Value  = c.DiscountPercent.HasValue ? (object)c.DiscountPercent.Value : DBNull.Value;
        cmd.Parameters.Add("damt",    NpgsqlDbType.Numeric).Value  = c.DiscountAmount.HasValue  ? (object)c.DiscountAmount.Value  : DBNull.Value;
        cmd.Parameters.Add("total",   NpgsqlDbType.Numeric).Value  = c.TotalAmount;
        cmd.Parameters.Add("status",  NpgsqlDbType.Varchar).Value  = string.IsNullOrWhiteSpace(c.Status) ? "активен" : c.Status;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value     = string.IsNullOrWhiteSpace(c.Comment) ? (object)DBNull.Value : c.Comment.Trim();
        cmd.Parameters.Add("branch",  NpgsqlDbType.Smallint).Value = c.IdBranch > 0 ? (short)c.IdBranch : (short)1;

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ResolveDirectionNameAsync(int? idDirection, string? currentSnapshot)
    {
        if (idDirection is not > 0)
            return string.IsNullOrWhiteSpace(currentSnapshot) ? null : currentSnapshot.Trim();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT direction_name FROM public.direction WHERE id_direction = @id", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idDirection.Value;
        var value = await command.ExecuteScalarAsync();
        if (value is string name && !string.IsNullOrWhiteSpace(name))
            return name.Trim();
        return string.IsNullOrWhiteSpace(currentSnapshot) ? null : currentSnapshot.Trim();
    }

    // -----------------------------------------------------------------
    // Архивация / удаление (мягкое — переводим статус)
    // -----------------------------------------------------------------
    public async Task ArchiveAsync(int idContract)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        // В contract есть два CHECK на status, в пересечение разрешённых
        // значений входят только 'активен' и 'расторгнут'. Используем
        // 'расторгнут' как ближайший аналог «архивации».
        const string sql = @"
UPDATE public.contract
SET status = 'расторгнут', updated_at = CURRENT_TIMESTAMP
WHERE id_contract = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idContract);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int idContract)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"DELETE FROM public.contract WHERE id_contract = @id;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", idContract);
        await cmd.ExecuteNonQueryAsync();
    }

    // -----------------------------------------------------------------
    // Справочники
    // -----------------------------------------------------------------
    public async Task<IReadOnlyList<LookupItem>> SearchStudentsLookupAsync(string? search, int limit = 40)
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var query = search?.Trim() ?? string.Empty;
        const string sql = @"
SELECT id_student,
       TRIM(last_name || ' ' || first_name || COALESCE(' ' || middle_name, '')) AS fio
FROM public.student
WHERE @hasQuery = FALSE
   OR TRIM(last_name || ' ' || first_name || COALESCE(' ' || middle_name, '')) ILIKE '%' || @search || '%'
   OR COALESCE(phone, '') ILIKE '%' || @search || '%'
ORDER BY last_name, first_name
LIMIT @limit;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("hasQuery", NpgsqlDbType.Boolean).Value = query.Length > 0;
        cmd.Parameters.Add("search", NpgsqlDbType.Text).Value = query;
        cmd.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LookupItem
            {
                Id = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1)
            });
        }

        return list;
    }

    public async Task<LookupItem?> GetStudentLookupByIdAsync(int idStudent)
    {
        var info = await GetStudentInfoAsync(idStudent);
        return info == null ? null : new LookupItem { Id = info.IdStudent, Name = info.FullName };
    }

    public async Task<ContractStudentInfo?> GetStudentInfoAsync(int idStudent)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT id_student,
       TRIM(last_name || ' ' || first_name || COALESCE(' ' || middle_name, '')) AS fio,
       birth_date
FROM public.student
WHERE id_student = @id;", connection);
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idStudent;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new ContractStudentInfo
        {
            IdStudent = reader.GetInt32(0),
            FullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
            BirthDate = reader.IsDBNull(2) ? DateTime.Today.AddYears(-7) : reader.GetDateTime(2)
        };
    }

    public async Task<IReadOnlyList<LookupItem>> GetStudentsLookupAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_student,
       TRIM(last_name || ' ' || first_name || COALESCE(' ' || middle_name, '')) AS fio
FROM public.student
ORDER BY last_name, first_name;";

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

    public async Task<IReadOnlyList<LookupItem>> GetDirectionsLookupAsync(int? ensureDirectionId = null)
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_direction,
       direction_name || CASE WHEN NOT is_active THEN ' (архив)' ELSE '' END AS direction_name
FROM public.direction
ORDER BY is_active DESC, direction_name;";

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

        if (ensureDirectionId.HasValue && list.All(x => x.Id != ensureDirectionId.Value))
        {
            list.Insert(0, new LookupItem
            {
                Id = ensureDirectionId.Value,
                Name = $"Направление #{ensureDirectionId.Value} (удалено из справочника)"
            });
        }

        return LookupDeduper.ByName(list);
    }

    public async Task<IReadOnlyList<LookupItem>> GetStudentTypesLookupAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT id_student_type, type_name, type_code
FROM public.student_type
ORDER BY
    CASE WHEN type_code ~ '^[0-9]+$' THEN CAST(type_code AS INTEGER) ELSE id_student_type END,
    type_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var code = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            list.Add(new LookupItem
            {
                Id   = reader.GetInt32(0),
                Name = int.TryParse(code, out var num) ? $"{num} — {name}" : name
            });
        }
        return LookupDeduper.ByName(list);
    }

    public async Task<IReadOnlyList<LookupItem>> GetStudentTypeLegendAsync()
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT type_code, type_name
FROM public.student_type
ORDER BY
    CASE WHEN type_code ~ '^[0-9]+$' THEN CAST(type_code AS INTEGER) ELSE 999 END,
    type_name;", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = reader.IsDBNull(0) ? "?" : reader.GetString(0).Trim();
            var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
            list.Add(new LookupItem { Id = 0, Name = $"{code} = {name}" });
        }

        return list;
    }

    // -----------------------------------------------------------------
    // Финансовая сводка для дашборда раздела "Оплаты"
    // -----------------------------------------------------------------
    public async Task<FinancialSummary> GetFinancialSummaryAsync()
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
WITH agg AS (
    SELECT
        c.id_contract,
        c.id_student,
        c.total_amount,
        COALESCE((SELECT SUM(p.amount) FROM public.payment p WHERE p.id_contract = c.id_contract), 0) AS paid
    FROM public.contract c
    WHERE c.status NOT IN ('расторгнут')
)
SELECT
    COALESCE(SUM(total_amount), 0)                        AS total_contracts,
    COALESCE(SUM(paid), 0)                                AS total_paid,
    COALESCE(SUM(GREATEST(total_amount - paid, 0)), 0)    AS total_debt,
    COUNT(DISTINCT CASE WHEN total_amount - paid > 0 THEN id_student END)::int AS debtors_count
FROM agg;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FinancialSummary
            {
                TotalContracts = reader.GetDecimal(0),
                TotalPaid      = reader.GetDecimal(1),
                TotalDebt      = reader.GetDecimal(2),
                DebtorsCount   = reader.GetInt32(3)
            };
        }
        return new FinancialSummary();
    }

    // -----------------------------------------------------------------
    // Список должников.
    // Заказчик хочет видеть: период, название занятия, ФИО клиента,
    // сумма долга, оплаченная сумма, комментарий, дата оплаты.
    // Для этого читаем строки из таблицы public.debt — там как раз
    // лежат debt_amount, paid_amount, repayment_date, note. Связь
    // с договором даёт нам период и направление, связь со student — ФИО.
    // Долгом считаем все записи со статусом активна / частично_погашена
    // (остальные — погашена / списана — не должников, но если поиск пуст
    // и фильтр не ограничивает, мы можем отдать только реально открытые).
    // -----------------------------------------------------------------
    public async Task<IReadOnlyList<DebtorItem>> GetDebtorsAsync(
        string? search = null,
        DebtorSortKey sortKey = DebtorSortKey.OverdueDesc,
        DebtorDisplayFilter displayFilter = DebtorDisplayFilter.All)
    {
        FinanceSchema.Ensure();
        await EnsureDebtDueDatesAsync();
        await EnsureContractBalanceDebtsAsync();

        var list = new List<DebtorItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var orderBy = sortKey switch
        {
            DebtorSortKey.OverdueAsc => "days_overdue ASC, outstanding DESC, effective_due ASC",
            DebtorSortKey.AmountDesc => "outstanding DESC, days_overdue DESC",
            DebtorSortKey.AmountAsc => "outstanding ASC, days_overdue DESC",
            DebtorSortKey.NameAsc => "full_name ASC, days_overdue DESC",
            DebtorSortKey.NameDesc => "full_name DESC, days_overdue DESC",
            DebtorSortKey.OccurrenceAsc => "occurrence_date ASC, outstanding DESC",
            DebtorSortKey.OccurrenceDesc => "occurrence_date DESC, outstanding DESC",
            _ => "days_overdue DESC, outstanding DESC, effective_due ASC"
        };

        var filterSql = displayFilter switch
        {
            DebtorDisplayFilter.AwaitingPayment =>
                "AND days_overdue = 0 AND NOT is_deferred",
            DebtorDisplayFilter.Overdue =>
                "AND days_overdue > 0 AND NOT is_deferred",
            DebtorDisplayFilter.Deferred =>
                "AND is_deferred",
            _ => string.Empty
        };

        var sql = $@"
WITH base AS (
SELECT
    db.id_debt,
    db.id_contract,
    c.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS full_name,
    c.contract_number,
    COALESCE(NULLIF(d.direction_name, ''), NULLIF(c.direction_name_snapshot, ''), '—') AS direction_name,
    c.start_date,
    c.end_date,
    db.debt_amount,
    db.paid_amount,
    db.repayment_date,
    db.note,
    db.status,
    db.occurrence_date,
    db.due_date,
    db.defer_until,
    db.defer_comment,
    GREATEST(db.debt_amount - db.paid_amount, 0) AS outstanding,
    (db.defer_until IS NOT NULL AND db.defer_until::date >= CURRENT_DATE) AS is_deferred,
    CASE
        WHEN db.defer_until IS NOT NULL AND db.defer_until::date >= CURRENT_DATE THEN 0
        WHEN GREATEST(db.debt_amount - db.paid_amount, 0) <= 0 THEN 0
        ELSE GREATEST(
            CURRENT_DATE - COALESCE(
                db.due_date,
                (DATE_TRUNC('month', db.occurrence_date) + INTERVAL '1 month' + INTERVAL '19 days')::date
            ),
            0
        )::int
    END AS days_overdue,
    COALESCE(
        db.due_date,
        (DATE_TRUNC('month', db.occurrence_date) + INTERVAL '1 month' + INTERVAL '19 days')::date
    ) AS effective_due
FROM public.debt db
JOIN public.contract  c ON c.id_contract  = db.id_contract
JOIN public.student   s ON s.id_student   = c.id_student
LEFT JOIN public.direction d ON d.id_direction = c.id_direction
WHERE db.status IN ('активна', 'частично_погашена')
  AND GREATEST(db.debt_amount - db.paid_amount, 0) > 0
  AND (@search IS NULL
       OR (s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) ILIKE '%' || @search || '%'
       OR c.contract_number ILIKE '%' || @search || '%'
       OR COALESCE(d.direction_name, c.direction_name_snapshot, '') ILIKE '%' || @search || '%')
)
SELECT *
FROM base
WHERE 1=1 {filterSql}
ORDER BY {orderBy};";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("search", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DebtorItem
            {
                IdDebt          = reader.GetInt32(0),
                IdContract      = reader.GetInt32(1),
                IdStudent       = reader.GetInt32(2),
                StudentFullName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ContractNumber  = reader.IsDBNull(4) ? "" : reader.GetString(4),
                DirectionName   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                PeriodStart     = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
                PeriodEnd       = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                DebtAmount      = reader.GetDecimal(8),
                PaidAmount      = reader.GetDecimal(9),
                RepaymentDate   = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10),
                Comment         = reader.IsDBNull(11) ? null : reader.GetString(11),
                Status          = reader.IsDBNull(12) ? "" : reader.GetString(12),
                OccurrenceDate  = reader.IsDBNull(13) ? (DateTime?)null : reader.GetDateTime(13),
                DueDate         = reader.IsDBNull(14) ? (DateTime?)null : reader.GetDateTime(14),
                DeferUntil      = reader.IsDBNull(15) ? (DateTime?)null : reader.GetDateTime(15),
                DeferComment    = reader.IsDBNull(16) ? null : reader.GetString(16)
            });
        }

        return list;
    }

    public async Task SaveDebtDeferAsync(int idDebt, DateTime deferUntil, string? comment)
    {
        FinanceSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
UPDATE public.debt
SET defer_until = @defer_until,
    defer_comment = @comment
WHERE id_debt = @id;", connection);
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idDebt;
        cmd.Parameters.Add("defer_until", NpgsqlDbType.Date).Value = deferUntil.Date;
        cmd.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(comment) ? (object)DBNull.Value : comment.Trim();
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
            throw new InvalidOperationException("Задолженность не найдена.");
    }

    private static async Task EnsureDebtDueDatesAsync()
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
UPDATE public.debt
SET due_date = (DATE_TRUNC('month', occurrence_date) + INTERVAL '1 month' + INTERVAL '19 days')::date
WHERE due_date IS NULL AND occurrence_date IS NOT NULL;", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Если по договору есть неоплаченный остаток, но строки в debt нет — создаём её,
    /// чтобы список дебиторки совпадал с суммой «Общий долг».
    /// </summary>
    private static async Task EnsureContractBalanceDebtsAsync()
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.debt
    (id_contract, debt_amount, paid_amount, occurrence_date, due_date, status, note)
SELECT
    c.id_contract,
    c.total_amount,
    COALESCE(pay.paid, 0),
    occ.occurrence_date,
    (DATE_TRUNC('month', occ.occurrence_date) + INTERVAL '1 month' + INTERVAL '19 days')::date,
    CASE
        WHEN COALESCE(pay.paid, 0) >= c.total_amount THEN 'погашена'
        WHEN COALESCE(pay.paid, 0) > 0 THEN 'частично_погашена'
        ELSE 'активна'
    END,
    'Остаток по договору'
FROM public.contract c
LEFT JOIN (
    SELECT id_contract, SUM(amount) AS paid
    FROM public.payment
    GROUP BY id_contract
) pay ON pay.id_contract = c.id_contract
CROSS JOIN LATERAL (
    SELECT LEAST(
        COALESCE(c.contract_date, c.start_date, CURRENT_DATE),
        CURRENT_DATE
    )::date AS occurrence_date
) occ
WHERE c.status NOT IN ('расторгнут')
  AND c.total_amount > COALESCE(pay.paid, 0)
  AND NOT EXISTS (
      SELECT 1 FROM public.debt d
      WHERE d.id_contract = c.id_contract
        AND d.status IN ('активна', 'частично_погашена')
  );", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> RepayDebtAsync(Payment payment, int idDebt)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string debtSql = @"
SELECT debt_amount, paid_amount
FROM public.debt
WHERE id_debt = @id
  AND status IN ('активна', 'частично_погашена')
FOR UPDATE;";

            await using (var debtCmd = new NpgsqlCommand(debtSql, connection, transaction))
            {
                debtCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idDebt;
                await using var reader = await debtCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Задолженность не найдена или уже закрыта.");

                var debtAmount = reader.GetDecimal(0);
                var paidAmount = reader.GetDecimal(1);
                var outstanding = Math.Max(0, debtAmount - paidAmount);
                if (payment.Amount <= 0)
                    throw new InvalidOperationException("Сумма оплаты должна быть больше нуля.");
                if (payment.Amount > outstanding)
                    throw new InvalidOperationException("Сумма платежа превышает сумму задолженности.");
            }

            const string insertPaymentSql = @"
INSERT INTO public.payment
(payment_date, id_contract, amount, id_payment_method, id_service_type,
 cash_receipt_issued, period_month, period_year, id_created_by, comment)
VALUES
(@dt, @contract, @amount, @method, @service, @receipt, @pm, @py, @creator, @comment)
RETURNING id_payment;";

            int idPayment;
            await using (var paymentCmd = new NpgsqlCommand(insertPaymentSql, connection, transaction))
            {
                paymentCmd.Parameters.Add("dt", NpgsqlDbType.Date).Value = payment.PaymentDate.Date;
                paymentCmd.Parameters.Add("contract", NpgsqlDbType.Integer).Value =
                    payment.IdContract.HasValue ? (object)payment.IdContract.Value : DBNull.Value;
                paymentCmd.Parameters.Add("amount", NpgsqlDbType.Numeric).Value = payment.Amount;
                paymentCmd.Parameters.Add("method", NpgsqlDbType.Integer).Value = payment.IdPaymentMethod;
                paymentCmd.Parameters.Add("service", NpgsqlDbType.Integer).Value = payment.IdServiceType;
                paymentCmd.Parameters.Add("receipt", NpgsqlDbType.Boolean).Value = payment.CashReceiptIssued;
                paymentCmd.Parameters.Add("pm", NpgsqlDbType.Smallint).Value =
                    payment.PeriodMonth.HasValue ? (object)payment.PeriodMonth.Value : DBNull.Value;
                paymentCmd.Parameters.Add("py", NpgsqlDbType.Smallint).Value =
                    payment.PeriodYear.HasValue ? (object)payment.PeriodYear.Value : DBNull.Value;
                paymentCmd.Parameters.Add("creator", NpgsqlDbType.Integer).Value =
                    payment.IdCreatedBy.HasValue ? (object)payment.IdCreatedBy.Value : DBNull.Value;
                paymentCmd.Parameters.Add("comment", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(payment.Comment) ? (object)DBNull.Value : payment.Comment.Trim();

                idPayment = Convert.ToInt32(await paymentCmd.ExecuteScalarAsync());
            }

            const string updateDebtSql = @"
UPDATE public.debt
SET paid_amount = paid_amount + @amount,
    repayment_date = CASE
        WHEN paid_amount + @amount >= debt_amount THEN @payment_date
        ELSE repayment_date
    END,
    status = CASE
        WHEN paid_amount + @amount >= debt_amount THEN 'погашена'
        WHEN paid_amount + @amount > 0 THEN 'частично_погашена'
        ELSE status
    END
WHERE id_debt = @id;";

            await using (var updateCmd = new NpgsqlCommand(updateDebtSql, connection, transaction))
            {
                updateCmd.Parameters.Add("amount", NpgsqlDbType.Numeric).Value = payment.Amount;
                updateCmd.Parameters.Add("payment_date", NpgsqlDbType.Date).Value = payment.PaymentDate.Date;
                updateCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idDebt;
                await updateCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return idPayment;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
