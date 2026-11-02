#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

// Репозиторий для раздела «Уведомления → Именинники».
// В БД уже есть таблица birthday_greeting с полями
//   id_student, year_of, is_greeted, greeting_date
// и UNIQUE(id_student, year_of), так что просто читаем оттуда и
// upsert-им запись по нажатию галочки.
public sealed class NotificationsRepository
{
    // Все обучающиеся, у которых день рождения совпадает с выбранной датой
    // (по дню и месяцу). Year_of берётся из самой выбранной даты.
    public async Task<IReadOnlyList<BirthdayItem>> GetBirthdaysOnDateAsync(DateTime date)
    {
        var list = new List<BirthdayItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        // Идея запроса:
        //   - сначала собираем студентов с подходящим днём/месяцем
        //   - джойним основного плательщика из student_representative
        //   - джойним birthday_greeting по (id_student, year_of)
        const string sql = @"
WITH main_payer AS (
    SELECT DISTINCT ON (sr.id_student)
        sr.id_student,
        TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS payer_name,
        r.phone AS payer_phone
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    WHERE sr.is_main_payer = TRUE
    ORDER BY sr.id_student, sr.id_student_representative
)
SELECT
    s.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS full_name,
    s.birth_date,
    DATE_PART('year', AGE(@d, s.birth_date))::int AS age_on_date,
    COALESCE(s.phone, '')                          AS phone,
    s.status,
    COALESCE(mp.payer_name,  '')                   AS payer_name,
    COALESCE(mp.payer_phone, '')                   AS payer_phone,
    bg.id_greeting,
    COALESCE(bg.is_greeted, FALSE)                 AS is_greeted,
    bg.greeting_date
FROM public.student s
LEFT JOIN main_payer mp ON mp.id_student = s.id_student
LEFT JOIN public.birthday_greeting bg
       ON bg.id_student = s.id_student
      AND bg.year_of = @year
WHERE EXTRACT(MONTH FROM s.birth_date) = EXTRACT(MONTH FROM @d)
  AND EXTRACT(DAY   FROM s.birth_date) = EXTRACT(DAY   FROM @d)
ORDER BY (s.status = 'активный') DESC, s.last_name, s.first_name;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("d",    NpgsqlDbType.Date).Value     = date.Date;
        cmd.Parameters.Add("year", NpgsqlDbType.Smallint).Value = (short)date.Year;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new BirthdayItem
            {
                IdStudent       = ReadInt(reader, "id_student"),
                FullName        = ReadString(reader, "full_name"),
                BirthDate       = ReadDateTime(reader, "birth_date"),
                AgeOnDate       = ReadInt(reader, "age_on_date"),
                Phone           = ReadString(reader, "phone"),
                Status          = ReadString(reader, "status"),
                MainPayerName   = ReadString(reader, "payer_name"),
                MainPayerPhone  = ReadString(reader, "payer_phone"),
                YearOf          = date.Year,
                IsGreeted       = ReadBool(reader, "is_greeted"),
                GreetingDate    = ReadNullableDateTime(reader, "greeting_date"),
            });
        }

        return list;
    }

    // Отмечаем именинника как «поздравлен» / снимаем галочку.
    // Используем UPSERT по UNIQUE(id_student, year_of).
    public async Task SetGreetedAsync(int idStudent, int year, bool isGreeted, DateTime? greetingDate)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO public.birthday_greeting (id_student, year_of, is_greeted, greeting_date)
VALUES (@id_student, @year, @is_greeted, @greeting_date)
ON CONFLICT (id_student, year_of)
DO UPDATE SET is_greeted   = EXCLUDED.is_greeted,
              greeting_date = EXCLUDED.greeting_date;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("id_student",    NpgsqlDbType.Integer).Value  = idStudent;
        cmd.Parameters.Add("year",          NpgsqlDbType.Smallint).Value = (short)year;
        cmd.Parameters.Add("is_greeted",    NpgsqlDbType.Boolean).Value  = isGreeted;
        cmd.Parameters.Add("greeting_date", NpgsqlDbType.Date).Value     =
            greetingDate.HasValue ? (object)greetingDate.Value.Date : DBNull.Value;

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Сумма всех активных уведомлений для бейджа колокольчика.</summary>
    public async Task<int> CountPendingNotificationsAsync(
        StudentSatisfactionRepository satisfactionRepository,
        ContractRenewalRepository renewalRepository)
    {
        var birthdays = await CountTodayBirthdaysAsync();
        var satisfaction = await satisfactionRepository.CountDueSurveysAsync();
        var renewal = await renewalRepository.CountDueAsync();
        return birthdays + satisfaction + renewal;
    }

    // Сколько именинников сегодня.
    public async Task<int> CountTodayBirthdaysAsync()
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT COUNT(*)::int
FROM public.student s
WHERE EXTRACT(MONTH FROM s.birth_date) = EXTRACT(MONTH FROM CURRENT_DATE)
  AND EXTRACT(DAY   FROM s.birth_date) = EXTRACT(DAY   FROM CURRENT_DATE);";

        await using var cmd = new NpgsqlCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    // ---- read-helpers (одинаковый стиль с остальными репозиториями) ----
    private static int     ReadInt(IDataRecord r, string n)            => r.GetInt32(r.GetOrdinal(n));
    private static bool    ReadBool(IDataRecord r, string n)           => r.GetBoolean(r.GetOrdinal(n));
    private static string  ReadString(IDataRecord r, string n)
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
            DateOnly d  => d.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(v),
        };
    }
    private static DateTime? ReadNullableDateTime(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        if (r.IsDBNull(i)) return null;
        var v = r.GetValue(i);
        return v switch
        {
            DateTime dt => dt,
            DateOnly d  => d.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(v),
        };
    }
}
