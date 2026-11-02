#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class TeachersRepository
{
    public async Task<IReadOnlyList<TeacherListItem>> GetTeachersAsync(string? search = null, bool onlyActive = false)
    {
        var result = new List<TeacherListItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    t.id_teacher,
    t.last_name, t.first_name, t.middle_name,
    t.phone, t.email, t.hire_date, t.base_lesson_rate, t.is_active,
    COALESCE((
        SELECT COUNT(*) FROM public.study_group g
        WHERE g.id_teacher = t.id_teacher AND g.is_active = true
    ), 0)::int AS active_groups,
    COALESCE((
        SELECT COUNT(*) FROM public.schedule s
        JOIN public.study_group g ON g.id_group = s.id_group
        WHERE g.id_teacher = t.id_teacher AND s.is_active = true AND g.is_active = true
    ), 0)::int AS lessons_per_week,
    COALESCE((
        SELECT STRING_AGG(d.direction_name, ', ' ORDER BY d.direction_name)
        FROM public.teacher_direction td
        JOIN public.direction d ON d.id_direction = td.id_direction
        WHERE td.id_teacher = t.id_teacher
    ), '—') AS directions_text
FROM public.teacher t
WHERE (@search IS NULL OR
       LOWER(TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, ''))) LIKE LOWER('%' || @search || '%') OR
       t.phone LIKE '%' || @search || '%' OR
       LOWER(COALESCE(t.email, '')) LIKE LOWER('%' || @search || '%'))
  AND (@only_active = false OR t.is_active = true)
ORDER BY t.is_active DESC, t.last_name, t.first_name;", connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActive;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TeacherListItem
            {
                IdTeacher = reader.GetInt32(reader.GetOrdinal("id_teacher")),
                LastName = reader.GetString(reader.GetOrdinal("last_name")),
                FirstName = reader.GetString(reader.GetOrdinal("first_name")),
                MiddleName = ReadNullable(reader, "middle_name"),
                Phone = reader.GetString(reader.GetOrdinal("phone")),
                Email = ReadNullable(reader, "email"),
                HireDate = ReadDate(reader, "hire_date"),
                BaseLessonRate = ReadNullableDecimal(reader, "base_lesson_rate"),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
                ActiveGroupsCount = reader.GetInt32(reader.GetOrdinal("active_groups")),
                LessonsPerWeek = reader.GetInt32(reader.GetOrdinal("lessons_per_week")),
                DirectionsText = reader.GetString(reader.GetOrdinal("directions_text"))
            });
        }
        return result;
    }

    public async Task<int> SaveTeacherAsync(TeacherEditModel teacher)
    {
        if (string.IsNullOrWhiteSpace(teacher.LastName))
            teacher.LastName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(teacher.FirstName))
            teacher.FirstName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(teacher.Phone))
            teacher.Phone = OptionalFieldDefaults.EmptyPhone;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (teacher.IdTeacher == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.teacher (last_name, first_name, middle_name, phone, email, hire_date, base_lesson_rate, is_active)
VALUES (@last_name, @first_name, @middle_name, @phone, @email, @hire_date, @base_lesson_rate, @is_active)
RETURNING id_teacher;", connection);
            FillTeacherParameters(command, teacher);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        else
        {
            await using var command = new NpgsqlCommand(@"
UPDATE public.teacher
SET last_name = @last_name, first_name = @first_name, middle_name = @middle_name,
    phone = @phone, email = @email, hire_date = @hire_date,
    base_lesson_rate = @base_lesson_rate, is_active = @is_active
WHERE id_teacher = @id_teacher;", connection);
            command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = teacher.IdTeacher;
            FillTeacherParameters(command, teacher);
            await command.ExecuteNonQueryAsync();
            return teacher.IdTeacher;
        }
    }

    public async Task DeleteTeacherAsync(int idTeacher)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM public.teacher WHERE id_teacher = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idTeacher;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int?> ResolveTeacherIdForUserAsync(AppUser user)
    {
        if (user.IdTeacher.HasValue)
            return user.IdTeacher;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_teacher
FROM public.teacher
WHERE id_user = @id_user
  AND is_active = true
LIMIT 1;", connection);
        command.Parameters.Add("id_user", NpgsqlDbType.Integer).Value = user.IdUser;

        var result = await command.ExecuteScalarAsync();
        return result is int idTeacher ? idTeacher : null;
    }

    public async Task<IReadOnlyList<TeacherLessonItem>> GetTeacherLessonsAsync(int idTeacher)
    {
        var result = new List<TeacherLessonItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    s.id_schedule, s.id_group, s.day_of_week, s.time_start, s.time_end,
    d.direction_name, g.group_number,
    cr.room_number,
    COALESCE((
        SELECT COUNT(*) FROM public.group_membership gm
        WHERE gm.id_group = g.id_group
          AND gm.enrollment_date <= CURRENT_DATE
          AND (gm.expulsion_date IS NULL OR gm.expulsion_date >= CURRENT_DATE)
    ), 0)::int AS students_count
FROM public.schedule s
JOIN public.study_group g ON g.id_group = s.id_group
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.classroom cr ON cr.id_classroom = s.id_classroom
WHERE g.id_teacher = @id_teacher
  AND s.is_active = true
  AND g.is_active = true
ORDER BY s.day_of_week, s.time_start;", connection);
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = idTeacher;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TeacherLessonItem
            {
                IdSchedule = reader.GetInt32(reader.GetOrdinal("id_schedule")),
                IdGroup = reader.GetInt32(reader.GetOrdinal("id_group")),
                GroupTitle = $"{ReadString(reader, "direction_name")} — гр. {reader.GetInt16(reader.GetOrdinal("group_number"))}",
                DayOfWeek = reader.GetInt16(reader.GetOrdinal("day_of_week")),
                TimeStart = ReadTime(reader, "time_start"),
                TimeEnd = ReadTime(reader, "time_end"),
                ClassroomNumber = ReadString(reader, "room_number"),
                StudentsCount = reader.GetInt32(reader.GetOrdinal("students_count"))
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<AttendanceRow>> GetAttendanceRowsAsync(int idSchedule, int idGroup, DateTime lessonDate)
    {
        var result = new List<AttendanceRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    gm.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
    COALESCE(att.status, 'не отмечено') AS att_status,
    att.id_attendance,
    att.comment
FROM public.group_membership gm
JOIN public.student s ON s.id_student = gm.id_student
LEFT JOIN LATERAL (
    SELECT a.id_attendance, a.status, a.comment
    FROM public.attendance a
    WHERE a.id_student = gm.id_student
      AND a.id_schedule = @id_schedule
      AND a.lesson_date = @lesson_date
    ORDER BY a.id_attendance DESC
    LIMIT 1
) att ON true
WHERE gm.id_group = @id_group
  AND gm.enrollment_date <= @lesson_date
  AND (gm.expulsion_date IS NULL OR gm.expulsion_date >= @lesson_date)
ORDER BY s.last_name, s.first_name, s.middle_name;", connection);
        command.Parameters.Add("id_schedule", NpgsqlDbType.Integer).Value = idSchedule;
        command.Parameters.Add("id_group", NpgsqlDbType.Integer).Value = idGroup;
        command.Parameters.Add("lesson_date", NpgsqlDbType.Date).Value = lessonDate.Date;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AttendanceRow
            {
                IdStudent = reader.GetInt32(reader.GetOrdinal("id_student")),
                StudentFullName = ReadString(reader, "student_full_name"),
                Status = ToUiAttendanceStatus(ReadString(reader, "att_status")),
                IdAttendance = reader.IsDBNull(reader.GetOrdinal("id_attendance")) ? null : reader.GetInt32(reader.GetOrdinal("id_attendance")),
                Comment = ReadNullable(reader, "comment")
            });
        }
        return result;
    }

    public async Task SaveAttendanceAsync(int idSchedule, DateTime lessonDate, IEnumerable<AttendanceRow> rows)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var row in rows)
            {
                var uiStatus = (row.Status ?? "не отмечено").Trim().ToLowerInvariant();
                var status = ToDatabaseAttendanceStatus(row.Status);
                var comment = string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment.Trim();

                if (uiStatus == "опоздал" && status == "присутствовал")
                {
                    comment = string.IsNullOrEmpty(comment)
                        ? "опоздал"
                        : comment.Contains("опоздал", StringComparison.OrdinalIgnoreCase)
                            ? comment
                            : $"{comment} (опоздал)";
                }

                await using (var delete = new NpgsqlCommand(@"
DELETE FROM public.attendance
WHERE id_schedule = @id_schedule
  AND id_student = @id_student
  AND lesson_date = @lesson_date;", connection, transaction))
                {
                    delete.Parameters.Add("id_schedule", NpgsqlDbType.Integer).Value = idSchedule;
                    delete.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = row.IdStudent;
                    delete.Parameters.Add("lesson_date", NpgsqlDbType.Date).Value = lessonDate.Date;
                    await delete.ExecuteNonQueryAsync();
                }

                if (status == "не отмечено") continue;

                await using var insert = new NpgsqlCommand(@"
INSERT INTO public.attendance (id_schedule, id_student, lesson_date, status, comment)
VALUES (@id_schedule, @id_student, @lesson_date, @status, @comment);", connection, transaction);
                insert.Parameters.Add("id_schedule", NpgsqlDbType.Integer).Value = idSchedule;
                insert.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = row.IdStudent;
                insert.Parameters.Add("lesson_date", NpgsqlDbType.Date).Value = lessonDate.Date;
                insert.Parameters.Add("status", NpgsqlDbType.Varchar).Value = status;
                insert.Parameters.Add("comment", NpgsqlDbType.Varchar).Value = comment is null ? (object)DBNull.Value : comment;
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            if (ex is Npgsql.PostgresException { SqlState: "23514" })
                throw new InvalidOperationException(
                    "Не удалось сохранить посещаемость: недопустимый статус. Обратитесь к администратору.", ex);
            throw;
        }
    }

    public async Task<TeacherSalaryReport> GetSalaryAsync(int idTeacher, DateTime fromDate, DateTime toDate)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
WITH attendance_latest AS (
    SELECT DISTINCT ON (att.id_schedule, att.id_student, att.lesson_date)
        att.id_schedule,
        att.id_student,
        att.lesson_date,
        att.status
    FROM public.attendance att
    JOIN public.schedule s ON s.id_schedule = att.id_schedule
    JOIN public.study_group g ON g.id_group = s.id_group
    WHERE g.id_teacher = @id_teacher
      AND s.is_active = true
      AND g.is_active = true
      AND att.lesson_date BETWEEN @date_from AND @date_to
    ORDER BY att.id_schedule, att.id_student, att.lesson_date, att.id_attendance DESC
), held AS (
    SELECT DISTINCT id_schedule, lesson_date
    FROM attendance_latest
    WHERE LOWER(TRIM(status)) <> 'не отмечено'
), visits AS (
    SELECT COUNT(*)::int AS visits_count
    FROM attendance_latest
    WHERE LOWER(TRIM(status)) IN ('был', 'присутствовал', 'присутствует', 'посетил', 'есть', 'да', 'опоздал')
)
SELECT
    t.base_lesson_rate,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    COALESCE((SELECT COUNT(*) FROM held), 0)::int AS lessons_held,
    COALESCE((SELECT visits_count FROM visits), 0)::int AS visits_count
FROM public.teacher t
WHERE t.id_teacher = @id_teacher;", connection);
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = idTeacher;
        command.Parameters.Add("date_from", NpgsqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("date_to", NpgsqlDbType.Date).Value = toDate.Date;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Преподаватель не найден.");

        var rate = ReadNullableDecimal(reader, "base_lesson_rate") ?? 0m;
        var lessonsHeld = reader.GetInt32(reader.GetOrdinal("lessons_held"));
        var lessonHours = TeacherListItem.HoursPerLesson;
        return new TeacherSalaryReport
        {
            TeacherName = reader.GetString(reader.GetOrdinal("teacher_name")),
            BaseLessonRate = rate,
            LessonsHeld = lessonsHeld,
            LessonHours = lessonHours,
            TotalHours = lessonsHeld * lessonHours,
            VisitsCount = reader.GetInt32(reader.GetOrdinal("visits_count")),
            Total = rate * lessonsHeld,
            FromDate = fromDate,
            ToDate = toDate
        };
    }

    public async Task<TeacherAttendanceStatsReport> GetTeacherAttendanceStatsAsync(int idTeacher, DateTime fromDate, DateTime toDate)
    {
        var report = new TeacherAttendanceStatsReport { FromDate = fromDate.Date, ToDate = toDate.Date };
        var rows = new List<TeacherGroupAttendanceStatsRow>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using (var header = new NpgsqlCommand(@"
SELECT
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    COALESCE((
        SELECT COUNT(*) FROM public.study_group g
        WHERE g.id_teacher = t.id_teacher AND g.is_active = true
    ), 0)::int AS groups_count,
    COALESCE((
        SELECT COUNT(DISTINCT gm.id_student)
        FROM public.group_membership gm
        JOIN public.study_group g ON g.id_group = gm.id_group
        WHERE g.id_teacher = t.id_teacher
          AND g.is_active = true
          AND gm.enrollment_date <= @date_to
          AND (gm.expulsion_date IS NULL OR gm.expulsion_date >= @date_from)
    ), 0)::int AS students_count
FROM public.teacher t
WHERE t.id_teacher = @id_teacher;", connection))
        {
            header.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = idTeacher;
            header.Parameters.Add("date_from", NpgsqlDbType.Date).Value = fromDate.Date;
            header.Parameters.Add("date_to", NpgsqlDbType.Date).Value = toDate.Date;
            await using var reader = await header.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Преподаватель не найден.");
            report.TeacherName = reader.GetString(reader.GetOrdinal("teacher_name"));
            report.GroupsCount = reader.GetInt32(reader.GetOrdinal("groups_count"));
            report.StudentsCount = reader.GetInt32(reader.GetOrdinal("students_count"));
        }

        await using (var command = new NpgsqlCommand(@"
WITH days AS (
    SELECT generate_series(CAST(@date_from AS date), CAST(@date_to AS date), '1 day'::interval)::date AS lesson_date
), planned AS (
    SELECT
        g.id_group,
        d.direction_name || ' — гр. ' || g.group_number::text AS group_title,
        s.id_schedule,
        days.lesson_date
    FROM days
    JOIN public.schedule s ON EXTRACT(ISODOW FROM days.lesson_date)::int = s.day_of_week
    JOIN public.study_group g ON g.id_group = s.id_group
    JOIN public.direction d ON d.id_direction = g.id_direction
    WHERE g.id_teacher = @id_teacher
      AND g.is_active = true
      AND s.is_active = true
), attendance_latest AS (
    SELECT DISTINCT ON (att.id_schedule, att.id_student, att.lesson_date)
        att.id_schedule,
        att.id_student,
        att.lesson_date,
        att.status
    FROM public.attendance att
    JOIN public.schedule s ON s.id_schedule = att.id_schedule
    JOIN public.study_group g ON g.id_group = s.id_group
    WHERE g.id_teacher = @id_teacher
      AND att.lesson_date BETWEEN @date_from AND @date_to
    ORDER BY att.id_schedule, att.id_student, att.lesson_date, att.id_attendance DESC
), fact AS (
    SELECT
        id_schedule,
        lesson_date,
        COUNT(*) FILTER (WHERE LOWER(TRIM(status)) <> 'не отмечено')::int AS marked_count,
        COUNT(*) FILTER (WHERE LOWER(TRIM(status)) IN ('был', 'присутствовал', 'присутствует', 'посетил', 'есть', 'да', 'опоздал'))::int AS visits_count,
        COUNT(*) FILTER (WHERE LOWER(TRIM(status)) IN ('не был', 'отсутствовал', 'нет', 'н'))::int AS absences_count,
        COUNT(*) FILTER (WHERE LOWER(TRIM(status)) IN ('уважительная', 'уважительная причина'))::int AS excused_count,
        COUNT(*) FILTER (WHERE LOWER(TRIM(status)) = 'опоздал')::int AS late_count
    FROM attendance_latest
    GROUP BY id_schedule, lesson_date
), combined AS (
    SELECT
        p.id_group,
        p.group_title,
        p.id_schedule,
        p.lesson_date,
        COALESCE(f.marked_count, 0) AS marked_count,
        COALESCE(f.visits_count, 0) AS visits_count,
        COALESCE(f.absences_count, 0) AS absences_count,
        COALESCE(f.excused_count, 0) AS excused_count,
        COALESCE(f.late_count, 0) AS late_count
    FROM planned p
    LEFT JOIN fact f ON f.id_schedule = p.id_schedule AND f.lesson_date = p.lesson_date
)
SELECT
    c.id_group,
    c.group_title,
    COALESCE(MAX(students.students_count), 0)::int AS students_count,
    COUNT(*)::int AS planned_lessons,
    COUNT(*) FILTER (WHERE c.marked_count > 0)::int AS lessons_held,
    (COUNT(*) FILTER (WHERE c.marked_count > 0) * 2)::numeric AS total_hours,
    COALESCE(SUM(c.visits_count), 0)::int AS visits_count,
    COALESCE(SUM(c.absences_count), 0)::int AS absences_count,
    COALESCE(SUM(c.excused_count), 0)::int AS excused_count,
    COALESCE(SUM(c.late_count), 0)::int AS late_count
FROM combined c
LEFT JOIN LATERAL (
    SELECT COUNT(*)::int AS students_count
    FROM public.group_membership gm
    WHERE gm.id_group = c.id_group
      AND gm.enrollment_date <= @date_to
      AND (gm.expulsion_date IS NULL OR gm.expulsion_date >= @date_from)
) students ON true
GROUP BY c.id_group, c.group_title
ORDER BY c.group_title;", connection))
        {
            command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value = idTeacher;
            command.Parameters.Add("date_from", NpgsqlDbType.Date).Value = fromDate.Date;
            command.Parameters.Add("date_to", NpgsqlDbType.Date).Value = toDate.Date;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new TeacherGroupAttendanceStatsRow
                {
                    GroupTitle = reader.GetString(reader.GetOrdinal("group_title")),
                    StudentsCount = reader.GetInt32(reader.GetOrdinal("students_count")),
                    PlannedLessons = reader.GetInt32(reader.GetOrdinal("planned_lessons")),
                    LessonsHeld = reader.GetInt32(reader.GetOrdinal("lessons_held")),
                    TotalHours = Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("total_hours"))),
                    Visits = reader.GetInt32(reader.GetOrdinal("visits_count")),
                    Absences = reader.GetInt32(reader.GetOrdinal("absences_count")),
                    Excused = reader.GetInt32(reader.GetOrdinal("excused_count")),
                    Late = reader.GetInt32(reader.GetOrdinal("late_count"))
                };
                rows.Add(row);
                report.PlannedLessons += row.PlannedLessons;
                report.LessonsHeld += row.LessonsHeld;
                report.TotalHours += row.TotalHours;
                report.Visits += row.Visits;
                report.Absences += row.Absences;
                report.Excused += row.Excused;
                report.Late += row.Late;
            }
        }

        report.Rows = rows;
        return report;
    }

    public async Task<IReadOnlyList<LookupItem>> GetDirectionsAsync()
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id_direction, direction_name FROM public.direction WHERE is_active = true ORDER BY direction_name;",
            connection);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new LookupItem { Id = r.GetInt32(0), Name = r.GetString(1) });
        return LookupDeduper.ByName(result);
    }

    public async Task<IReadOnlyList<int>> GetTeacherDirectionIdsAsync(int idTeacher)
    {
        var result = new List<int>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id_direction FROM public.teacher_direction WHERE id_teacher = @id;", connection);
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idTeacher;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add(r.GetInt32(0));
        return result;
    }

    public async Task SaveTeacherDirectionsAsync(int idTeacher, IEnumerable<int> directionIds)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var del = new NpgsqlCommand(
                "DELETE FROM public.teacher_direction WHERE id_teacher = @id;", connection, transaction))
            {
                del.Parameters.Add("id", NpgsqlDbType.Integer).Value = idTeacher;
                await del.ExecuteNonQueryAsync();
            }
            foreach (var dirId in directionIds)
            {
                await using var ins = new NpgsqlCommand(
                    "INSERT INTO public.teacher_direction (id_teacher, id_direction) VALUES (@t, @d);",
                    connection, transaction);
                ins.Parameters.Add("t", NpgsqlDbType.Integer).Value = idTeacher;
                ins.Parameters.Add("d", NpgsqlDbType.Integer).Value = dirId;
                await ins.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    /// <summary>
    /// Значения для INSERT — по CHECK attendance_status_check в PostgreSQL:
    /// 'присутствовал', 'отсутствовал', 'уважительная'.
    /// </summary>
    private static string ToDatabaseAttendanceStatus(string? status)
    {
        var value = (status ?? "не отмечено").Trim().ToLowerInvariant();
        return value switch
        {
            "был" or "присутствовал" or "присутствует" or "посетил" or "есть" or "да" or "опоздал" => "присутствовал",
            "не был" or "отсутствовал" or "нет" or "н" => "отсутствовал",
            "уважительная" or "уважительная причина" => "уважительная",
            "не отмечено" or "" => "не отмечено",
            _ => throw new InvalidOperationException($"Неизвестный статус посещаемости: {status}")
        };
    }

    /// <summary>
    /// Значения для UI — короткие подписи в форме отметки посещаемости.
    /// </summary>
    private static string ToUiAttendanceStatus(string? status)
    {
        var value = (status ?? "не отмечено").Trim().ToLowerInvariant();
        return value switch
        {
            "присутствовал" or "присутствует" or "посетил" or "был" or "есть" or "да" => "был",
            "отсутствовал" or "не был" or "нет" or "н" => "не был",
            "уважительная" or "уважительная причина" => "уважительная",
            "опоздал" => "опоздал",
            "не отмечено" or "" => "не отмечено",
            _ => status?.Trim() is { Length: > 0 } raw ? raw : "не отмечено"
        };
    }

    private static void FillTeacherParameters(NpgsqlCommand command, TeacherEditModel teacher)
    {
        command.Parameters.Add("last_name", NpgsqlDbType.Varchar).Value = teacher.LastName.Trim();
        command.Parameters.Add("first_name", NpgsqlDbType.Varchar).Value = teacher.FirstName.Trim();
        command.Parameters.Add("middle_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(teacher.MiddleName) ? (object)DBNull.Value : teacher.MiddleName.Trim();
        command.Parameters.Add("phone", NpgsqlDbType.Varchar).Value = teacher.Phone.Trim();
        command.Parameters.Add("email", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(teacher.Email) ? (object)DBNull.Value : teacher.Email.Trim();
        command.Parameters.Add("hire_date", NpgsqlDbType.Date).Value = teacher.HireDate.Date;
        command.Parameters.Add("base_lesson_rate", NpgsqlDbType.Numeric).Value =
            teacher.BaseLessonRate.HasValue ? (object)teacher.BaseLessonRate.Value : DBNull.Value;
        command.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = teacher.IsActive;
    }

    private static string? ReadNullable(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static string ReadString(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? string.Empty : r.GetString(i);
    }

    private static decimal? ReadNullableDecimal(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? (decimal?)null : Convert.ToDecimal(r.GetValue(i));
    }

    private static DateTime ReadDate(IDataRecord r, string n)
    {
        var v = r.GetValue(r.GetOrdinal(n));
        return v switch
        {
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(v)
        };
    }

    private static TimeSpan ReadTime(IDataRecord r, string n)
    {
        var v = r.GetValue(r.GetOrdinal(n));
        return v switch
        {
            TimeSpan ts => ts,
            TimeOnly to => to.ToTimeSpan(),
            _ => TimeSpan.Parse(v.ToString() ?? "00:00")
        };
    }
}
