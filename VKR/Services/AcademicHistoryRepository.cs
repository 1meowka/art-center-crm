#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class GroupHistoryFilter
{
    public int? IdGroup { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? IdTeacher { get; set; }
    /// <summary>null — все; teacher_change, enroll, transfer, expel</summary>
    public string? EventCategory { get; set; }
    public int Limit { get; set; } = 500;
}

public sealed class AcademicHistoryRepository
{
    public async Task LogTeacherChangeAsync(
        int idGroup,
        int? idTeacherOld,
        int idTeacherNew,
        int? idChangedBy,
        string? comment = null)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
INSERT INTO public.group_teacher_history
    (id_group, id_teacher_old, id_teacher_new, id_changed_by, comment)
VALUES (@group, @old, @new, @user, @comment);", connection);
        command.Parameters.Add("group", NpgsqlDbType.Integer).Value = idGroup;
        command.Parameters.Add("old", NpgsqlDbType.Integer).Value =
            idTeacherOld.HasValue && idTeacherOld.Value > 0 ? idTeacherOld.Value : DBNull.Value;
        command.Parameters.Add("new", NpgsqlDbType.Integer).Value = idTeacherNew;
        command.Parameters.Add("user", NpgsqlDbType.Integer).Value =
            idChangedBy.HasValue ? idChangedBy.Value : DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim();
        await command.ExecuteNonQueryAsync();
    }

    public Task LogStudentTransferAsync(
        int idStudent,
        int? idGroupFrom,
        int idGroupTo,
        int? idTeacherFrom,
        int? idTeacherTo,
        DateTime transferDate,
        int? idChangedBy,
        string? reason = null,
        string? comment = null) =>
        LogStudentGroupEventAsync(
            idStudent,
            idGroupFrom,
            idGroupTo,
            idTeacherFrom,
            idTeacherTo,
            transferDate,
            idChangedBy,
            "transfer",
            reason,
            comment);

    public async Task LogStudentGroupEventAsync(
        int idStudent,
        int? idGroupFrom,
        int? idGroupTo,
        int? idTeacherFrom,
        int? idTeacherTo,
        DateTime eventDate,
        int? idChangedBy,
        string eventType,
        string? reason = null,
        string? comment = null)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
INSERT INTO public.student_group_history
    (id_student, id_group_from, id_group_to, id_teacher_from, id_teacher_to,
     transfer_date, id_changed_by, reason, comment, event_type)
VALUES
    (@student, @from_g, @to_g, @from_t, @to_t, @date, @user, @reason, @comment, @event_type);", connection);
        command.Parameters.Add("student", NpgsqlDbType.Integer).Value = idStudent;
        command.Parameters.Add("from_g", NpgsqlDbType.Integer).Value =
            idGroupFrom.HasValue && idGroupFrom.Value > 0 ? idGroupFrom.Value : DBNull.Value;
        command.Parameters.Add("to_g", NpgsqlDbType.Integer).Value =
            idGroupTo.HasValue && idGroupTo.Value > 0 ? idGroupTo.Value : DBNull.Value;
        command.Parameters.Add("from_t", NpgsqlDbType.Integer).Value =
            idTeacherFrom.HasValue && idTeacherFrom.Value > 0 ? idTeacherFrom.Value : DBNull.Value;
        command.Parameters.Add("to_t", NpgsqlDbType.Integer).Value =
            idTeacherTo.HasValue && idTeacherTo.Value > 0 ? idTeacherTo.Value : DBNull.Value;
        command.Parameters.Add("date", NpgsqlDbType.Date).Value = eventDate.Date;
        command.Parameters.Add("user", NpgsqlDbType.Integer).Value =
            idChangedBy.HasValue ? idChangedBy.Value : DBNull.Value;
        command.Parameters.Add("reason", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim();
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim();
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = eventType;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<GroupHistoryTimelineItem>> GetGroupHistoryTimelineAsync(GroupHistoryFilter filter)
    {
        AcademicSchema.Ensure();
        var result = new List<GroupHistoryTimelineItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT event_at, event_category, id_group, group_title, subject, detail, teacher_name, changed_by
FROM (
    SELECT
        h.changed_at AS event_at,
        'teacher_change'::text AS event_category,
        h.id_group,
        d.direction_name || ' — гр. ' || g.group_number::text AS group_title,
        'Преподаватель группы' AS subject,
        TRIM(COALESCE(t_old.last_name || ' ' || t_old.first_name, 'не назначен')) || ' → '
            || TRIM(t_new.last_name || ' ' || t_new.first_name) AS detail,
        TRIM(t_new.last_name || ' ' || t_new.first_name) AS teacher_name,
        TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS changed_by
    FROM public.group_teacher_history h
    JOIN public.study_group g ON g.id_group = h.id_group
    JOIN public.direction d ON d.id_direction = g.id_direction
    JOIN public.teacher t_new ON t_new.id_teacher = h.id_teacher_new
    LEFT JOIN public.teacher t_old ON t_old.id_teacher = h.id_teacher_old
    LEFT JOIN public.app_user u ON u.id_user = h.id_changed_by
    WHERE (@id_group IS NULL OR h.id_group = @id_group)
      AND (@date_from IS NULL OR h.changed_at >= @date_from)
      AND (@date_to IS NULL OR h.changed_at < @date_to_end)
      AND (@id_teacher IS NULL OR h.id_teacher_new = @id_teacher OR h.id_teacher_old = @id_teacher)
      AND (@event_category IS NULL OR @event_category = 'teacher_change')

    UNION ALL

    SELECT
        h.changed_at,
        h.event_type,
        COALESCE(h.id_group_to, h.id_group_from) AS id_group,
        CASE
            WHEN h.event_type = 'enroll' THEN
                d_to.direction_name || ' — гр. ' || g_to.group_number::text
            WHEN h.event_type = 'expel' AND g_from.id_group IS NOT NULL THEN
                d_from.direction_name || ' — гр. ' || g_from.group_number::text
            WHEN g_to.id_group IS NOT NULL THEN
                d_to.direction_name || ' — гр. ' || g_to.group_number::text
            ELSE
                d_from.direction_name || ' — гр. ' || g_from.group_number::text
        END AS group_title,
        TRIM(s.last_name || ' ' || s.first_name) AS subject,
        CASE h.event_type
            WHEN 'enroll' THEN 'Зачислен в «' || d_to.direction_name || ' — гр. ' || g_to.group_number::text || '»'
            WHEN 'expel' THEN 'Отчислен из «' || d_from.direction_name || ' — гр. ' || g_from.group_number::text || '»'
            ELSE COALESCE(
                CASE WHEN g_from.id_group IS NULL THEN '—'
                     ELSE d_from.direction_name || ' — гр. ' || g_from.group_number::text END,
                '—')
                || ' → '
                || COALESCE(d_to.direction_name || ' — гр. ' || g_to.group_number::text, '—')
        END AS detail,
        TRIM(COALESCE(t_to.last_name || ' ' || t_to.first_name, t_from.last_name || ' ' || t_from.first_name, '')) AS teacher_name,
        TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS changed_by
    FROM public.student_group_history h
    JOIN public.student s ON s.id_student = h.id_student
    LEFT JOIN public.study_group g_from ON g_from.id_group = h.id_group_from
    LEFT JOIN public.direction d_from ON d_from.id_direction = g_from.id_direction
    LEFT JOIN public.study_group g_to ON g_to.id_group = h.id_group_to
    LEFT JOIN public.direction d_to ON d_to.id_direction = g_to.id_direction
    LEFT JOIN public.teacher t_from ON t_from.id_teacher = h.id_teacher_from
    LEFT JOIN public.teacher t_to ON t_to.id_teacher = h.id_teacher_to
    LEFT JOIN public.app_user u ON u.id_user = h.id_changed_by
    WHERE (@id_group IS NULL OR h.id_group_from = @id_group OR h.id_group_to = @id_group)
      AND (@date_from IS NULL OR h.changed_at >= @date_from)
      AND (@date_to IS NULL OR h.changed_at < @date_to_end)
      AND (@id_teacher IS NULL OR h.id_teacher_from = @id_teacher OR h.id_teacher_to = @id_teacher)
      AND (@event_category IS NULL OR h.event_type = @event_category)
) timeline
ORDER BY event_at DESC
LIMIT @limit;", connection);

        command.Parameters.Add("id_group", NpgsqlDbType.Integer).Value =
            filter.IdGroup.HasValue ? filter.IdGroup.Value : DBNull.Value;
        command.Parameters.Add("date_from", NpgsqlDbType.TimestampTz).Value =
            filter.DateFrom.HasValue ? filter.DateFrom.Value.Date : DBNull.Value;
        command.Parameters.Add("date_to", NpgsqlDbType.TimestampTz).Value =
            filter.DateTo.HasValue ? filter.DateTo.Value.Date : DBNull.Value;
        command.Parameters.Add("date_to_end", NpgsqlDbType.TimestampTz).Value =
            filter.DateTo.HasValue ? filter.DateTo.Value.Date.AddDays(1) : DBNull.Value;
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
            filter.IdTeacher.HasValue ? filter.IdTeacher.Value : DBNull.Value;
        command.Parameters.Add("event_category", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(filter.EventCategory) ? DBNull.Value : filter.EventCategory.Trim();
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = filter.Limit;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new GroupHistoryTimelineItem
            {
                EventAt = reader.GetDateTime(0),
                EventCategory = reader.GetString(1),
                IdGroup = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                GroupTitle = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Subject = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim(),
                Detail = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                TeacherName = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                ChangedByName = reader.IsDBNull(7) ? null : reader.GetString(7).Trim()
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<GroupTeacherHistoryItem>> GetTeacherHistoryAsync(int? idGroup = null, int limit = 200)
    {
        var filter = new GroupHistoryFilter { IdGroup = idGroup, EventCategory = "teacher_change", Limit = limit };
        var timeline = await GetGroupHistoryTimelineAsync(filter);
        var result = new List<GroupTeacherHistoryItem>();
        foreach (var t in timeline)
        {
            result.Add(new GroupTeacherHistoryItem
            {
                IdGroup = t.IdGroup ?? 0,
                GroupTitle = t.GroupTitle,
                TeacherOldName = ExtractTeacherOld(t.Detail),
                TeacherNewName = ExtractTeacherNew(t.Detail),
                ChangedAt = t.EventAt,
                ChangedByName = t.ChangedByName,
                Comment = null
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<StudentGroupHistoryItem>> GetStudentTransferHistoryAsync(
        int? idStudent = null,
        int limit = 200)
    {
        AcademicSchema.Ensure();
        var result = new List<StudentGroupHistoryItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    h.id_history,
    h.id_student,
    TRIM(s.last_name || ' ' || s.first_name) AS student_name,
    CASE WHEN g_from.id_group IS NULL THEN NULL
         ELSE d_from.direction_name || ' — гр. ' || g_from.group_number::text END AS group_from,
    CASE WHEN g_to.id_group IS NULL THEN '—'
         ELSE d_to.direction_name || ' — гр. ' || g_to.group_number::text END AS group_to,
    TRIM(COALESCE(t_from.last_name, '') || ' ' || COALESCE(t_from.first_name, '')) AS teacher_from,
    TRIM(COALESCE(t_to.last_name, '') || ' ' || COALESCE(t_to.first_name, '')) AS teacher_to,
    h.transfer_date,
    h.changed_at,
    TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '')) AS changed_by,
    h.reason
FROM public.student_group_history h
JOIN public.student s ON s.id_student = h.id_student
LEFT JOIN public.study_group g_to ON g_to.id_group = h.id_group_to
LEFT JOIN public.direction d_to ON d_to.id_direction = g_to.id_direction
LEFT JOIN public.study_group g_from ON g_from.id_group = h.id_group_from
LEFT JOIN public.direction d_from ON d_from.id_direction = g_from.id_direction
LEFT JOIN public.teacher t_from ON t_from.id_teacher = h.id_teacher_from
LEFT JOIN public.teacher t_to ON t_to.id_teacher = h.id_teacher_to
LEFT JOIN public.app_user u ON u.id_user = h.id_changed_by
WHERE (@id_student IS NULL OR h.id_student = @id_student)
ORDER BY h.changed_at DESC
LIMIT @limit;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value =
            idStudent.HasValue ? idStudent.Value : DBNull.Value;
        command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new StudentGroupHistoryItem
            {
                IdHistory = reader.GetInt32(0),
                IdStudent = reader.GetInt32(1),
                StudentFullName = reader.GetString(2).Trim(),
                GroupFromTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
                GroupToTitle = reader.GetString(4),
                TeacherFromName = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                TeacherToName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim(),
                TransferDate = reader.GetDateTime(7),
                ChangedAt = reader.GetDateTime(8),
                ChangedByName = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                Reason = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }

        return result;
    }

    private static string? ExtractTeacherOld(string detail)
    {
        var idx = detail.IndexOf(" → ", StringComparison.Ordinal);
        return idx < 0 ? null : detail[..idx].Trim();
    }

    private static string ExtractTeacherNew(string detail)
    {
        var idx = detail.IndexOf(" → ", StringComparison.Ordinal);
        return idx < 0 ? detail : detail[(idx + 3)..].Trim();
    }
}
