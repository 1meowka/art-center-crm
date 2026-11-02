#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class GroupsRepository
{
    public GroupsRepository()
    {
        DatabaseSchema.EnsureIfPossible();
        AcademicSchema.Ensure();
    }

    public async Task<IReadOnlyList<StudyGroupListItem>> GetGroupsAsync(string? search = null, bool onlyActive = false)
    {
        var result = new List<StudyGroupListItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    g.id_group,
    g.group_number,
    g.id_direction,
    d.direction_name,
    g.id_teacher,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    g.is_active,
    g.age_min,
    g.age_max,
    g.age_band_label,
    g.max_students,
    COALESCE((
        SELECT COUNT(*) FROM public.group_membership gm
        WHERE gm.id_group = g.id_group AND gm.expulsion_date IS NULL
    ), 0)::int AS students_count,
    COALESCE((
        SELECT COUNT(*) FROM public.schedule s
        WHERE s.id_group = g.id_group AND s.is_active = true
    ), 0)::int AS lessons_per_week
FROM public.study_group g
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.teacher t ON t.id_teacher = g.id_teacher
WHERE (@search IS NULL OR
       LOWER(d.direction_name) LIKE LOWER('%' || @search || '%') OR
       LOWER(TRIM(t.last_name || ' ' || t.first_name)) LIKE LOWER('%' || @search || '%') OR
       CAST(g.group_number AS TEXT) LIKE '%' || @search || '%')
  AND (@only_active = false OR g.is_active = true)
ORDER BY g.is_active DESC, d.direction_name, g.group_number;", connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = 
            string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActive;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new StudyGroupListItem
            {
                IdGroup = ReadInt(reader, "id_group"),
                GroupNumber = ReadShort(reader, "group_number"),
                IdDirection = ReadInt(reader, "id_direction"),
                DirectionName = ReadString(reader, "direction_name"),
                IdTeacher = ReadInt(reader, "id_teacher"),
                TeacherName = ReadString(reader, "teacher_name"),
                IsActive = ReadBool(reader, "is_active"),
                AgeBandLabel = ReadNullableString(reader, "age_band_label"),
                MaxStudents = ReadNullableShort(reader, "max_students"),
                StudentsCount = ReadInt(reader, "students_count"),
                LessonsPerWeek = ReadInt(reader, "lessons_per_week")
            });
        }

        return result;
    }

    public async Task<StudyGroupEditModel?> GetGroupEditModelAsync(int idGroup)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT group_number, id_direction, id_teacher, is_active, age_min, age_max, age_band_label, max_students
FROM public.study_group WHERE id_group = @id;", connection);
        command.Parameters.AddWithValue("id", idGroup);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new StudyGroupEditModel
        {
            IdGroup = idGroup,
            GroupNumber = ReadShort(reader, "group_number"),
            IdDirection = ReadInt(reader, "id_direction"),
            IdTeacher = ReadInt(reader, "id_teacher"),
            IsActive = ReadBool(reader, "is_active"),
            AgeMin = ReadNullableShort(reader, "age_min"),
            AgeMax = ReadNullableShort(reader, "age_max"),
            AgeBandLabel = ReadNullableString(reader, "age_band_label"),
            MaxStudents = ReadNullableShort(reader, "max_students")
        };
    }

    public async Task<int> SaveGroupAsync(StudyGroupEditModel group)
    {
        var changedByUserId = UserSession.CurrentUserId;
        AcademicSchema.Ensure();
        if (group.GroupNumber <= 0)
            group.GroupNumber = OptionalFieldDefaults.GroupNumberOrDefault(0);

        var historyRepo = new AcademicHistoryRepository();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (group.IdGroup == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.study_group
    (group_number, id_direction, id_teacher, is_active, age_min, age_max, age_band_label, max_students)
VALUES
    (@group_number, @id_direction, @id_teacher, @is_active, @age_min, @age_max, @age_band_label, @max_students)
RETURNING id_group;", connection);
            FillGroupParameters(command, group);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        int? oldTeacherId = null;
        await using (var readCmd = new NpgsqlCommand(
            "SELECT id_teacher FROM public.study_group WHERE id_group = @id", connection))
        {
            readCmd.Parameters.AddWithValue("id", group.IdGroup);
            var val = await readCmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value)
                oldTeacherId = Convert.ToInt32(val);
        }

        await using (var command = new NpgsqlCommand(@"
UPDATE public.study_group
SET group_number = @group_number,
    id_direction = @id_direction,
    id_teacher = @id_teacher,
    is_active = @is_active,
    age_min = @age_min,
    age_max = @age_max,
    age_band_label = @age_band_label,
    max_students = @max_students
WHERE id_group = @id_group;", connection))
        {
            command.Parameters.AddWithValue("id_group", group.IdGroup);
            FillGroupParameters(command, group);
            await command.ExecuteNonQueryAsync();
        }

        if (oldTeacherId.HasValue && group.IdTeacher is > 0 && oldTeacherId.Value != group.IdTeacher.Value)
        {
            await historyRepo.LogTeacherChangeAsync(
                group.IdGroup,
                oldTeacherId,
                group.IdTeacher.Value,
                changedByUserId);
        }

        return group.IdGroup;
    }

    private static void FillGroupParameters(NpgsqlCommand command, StudyGroupEditModel group)
    {
        command.Parameters.AddWithValue("group_number", group.GroupNumber);
        command.Parameters.AddWithValue("id_direction",
            group.IdDirection is > 0 ? group.IdDirection.Value : DBNull.Value);
        command.Parameters.AddWithValue("id_teacher",
            group.IdTeacher is > 0 ? group.IdTeacher.Value : DBNull.Value);
        command.Parameters.AddWithValue("is_active", group.IsActive);
        command.Parameters.AddWithValue("age_min", (object?)group.AgeMin ?? DBNull.Value);
        command.Parameters.AddWithValue("age_max", (object?)group.AgeMax ?? DBNull.Value);
        command.Parameters.AddWithValue("age_band_label",
            string.IsNullOrWhiteSpace(group.AgeBandLabel) ? DBNull.Value : group.AgeBandLabel.Trim());
        command.Parameters.AddWithValue("max_students", (object?)group.MaxStudents ?? DBNull.Value);
    }

    public async Task DeleteGroupAsync(int idGroup)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM public.study_group WHERE id_group = @id;", connection);
        command.Parameters.AddWithValue("id", idGroup);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<GroupMemberItem>> GetGroupMembersAsync(int idGroup)
    {
        var result = new List<GroupMemberItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    gm.id_membership,
    gm.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
    gm.enrollment_date,
    gm.expulsion_date
FROM public.group_membership gm
JOIN public.student s ON s.id_student = gm.id_student
WHERE gm.id_group = @id_group
ORDER BY gm.expulsion_date NULLS FIRST, s.last_name, s.first_name;", connection);
        command.Parameters.AddWithValue("id_group", idGroup);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new GroupMemberItem
            {
                IdMembership = ReadInt(reader, "id_membership"),
                IdStudent = ReadInt(reader, "id_student"),
                StudentFullName = ReadString(reader, "student_full_name"),
                EnrollmentDate = ReadDate(reader, "enrollment_date"),
                ExpulsionDate = ReadNullableDate(reader, "expulsion_date")
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<ScheduleItem>> GetScheduleAsync(
        int? idGroup = null,
        bool onlyActive = false,
        int? idBranch = null)
    {
        var result = new List<ScheduleItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT
    sc.id_schedule,
    sc.id_group,
    g.group_number,
    d.direction_name,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    sc.id_classroom,
    cr.room_number,
    COALESCE(cr.id_branch, 1) AS id_branch,
    sc.day_of_week,
    sc.time_start,
    sc.time_end,
    sc.is_active,
    COALESCE(sc.lesson_type, 'regular') AS lesson_type
FROM public.schedule sc
JOIN public.study_group g ON g.id_group = sc.id_group
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.teacher t ON t.id_teacher = g.id_teacher
JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
WHERE (@id_group IS NULL OR sc.id_group = @id_group)
  AND (@only_active = false OR sc.is_active = true)
  AND (@id_branch IS NULL OR COALESCE(cr.id_branch, 1) = @id_branch)
ORDER BY sc.day_of_week, sc.time_start;", connection);
        command.Parameters.Add("id_group", NpgsqlDbType.Integer).Value =
            idGroup.HasValue ? (object)idGroup.Value : DBNull.Value;
        command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActive;
        command.Parameters.Add("id_branch", NpgsqlDbType.Smallint).Value =
            idBranch.HasValue ? (object)(short)idBranch.Value : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var direction = ReadString(reader, "direction_name");
            var groupNum = ReadShort(reader, "group_number");
            result.Add(new ScheduleItem
            {
                IdSchedule = ReadInt(reader, "id_schedule"),
                IdGroup = ReadInt(reader, "id_group"),
                DirectionName = direction,
                GroupTitle = $"{direction} — гр. {groupNum}",
                TeacherName = ReadString(reader, "teacher_name"),
                IdClassroom = ReadInt(reader, "id_classroom"),
                ClassroomNumber = ReadString(reader, "room_number"),
                IdBranch = ReadShort(reader, "id_branch"),
                DayOfWeek = ReadShort(reader, "day_of_week"),
                TimeStart = ReadTime(reader, "time_start"),
                TimeEnd = ReadTime(reader, "time_end"),
                IsActive = ReadBool(reader, "is_active"),
                LessonType = ReadNullableString(reader, "lesson_type") ?? "regular"
            });
        }
        return result;
    }

    public async Task<int> SaveScheduleAsync(ScheduleEditModel item)
    {
        if (item.IdGroup <= 0) item.IdGroup = 1;
        if (item.IdClassroom <= 0) item.IdClassroom = 1;
        if (item.DayOfWeek < 1 || item.DayOfWeek > 7) item.DayOfWeek = 1;
        if (item.TimeEnd <= item.TimeStart)
            item.TimeEnd = item.TimeStart.Add(TimeSpan.FromHours(1));

        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (item.IdSchedule == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.schedule (id_group, id_classroom, day_of_week, time_start, time_end, is_active, lesson_type)
VALUES (@id_group, @id_classroom, @day_of_week, @time_start, @time_end, @is_active, @lesson_type)
RETURNING id_schedule;", connection);
            command.Parameters.AddWithValue("id_group", item.IdGroup);
            command.Parameters.AddWithValue("id_classroom", item.IdClassroom);
            command.Parameters.AddWithValue("day_of_week", item.DayOfWeek);
            command.Parameters.AddWithValue("time_start", item.TimeStart);
            command.Parameters.AddWithValue("time_end", item.TimeEnd);
            command.Parameters.AddWithValue("is_active", item.IsActive);
            command.Parameters.AddWithValue("lesson_type", item.LessonType);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        else
        {
            await using var command = new NpgsqlCommand(@"
UPDATE public.schedule
SET id_group = @id_group,
    id_classroom = @id_classroom,
    day_of_week = @day_of_week,
    time_start = @time_start,
    time_end = @time_end,
    is_active = @is_active,
    lesson_type = @lesson_type
WHERE id_schedule = @id_schedule;", connection);
            command.Parameters.AddWithValue("id_schedule", item.IdSchedule);
            command.Parameters.AddWithValue("id_group", item.IdGroup);
            command.Parameters.AddWithValue("id_classroom", item.IdClassroom);
            command.Parameters.AddWithValue("day_of_week", item.DayOfWeek);
            command.Parameters.AddWithValue("time_start", item.TimeStart);
            command.Parameters.AddWithValue("time_end", item.TimeEnd);
            command.Parameters.AddWithValue("is_active", item.IsActive);
            command.Parameters.AddWithValue("lesson_type", item.LessonType);
            await command.ExecuteNonQueryAsync();
            return item.IdSchedule;
        }
    }

    public async Task DeleteScheduleAsync(int idSchedule)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM public.schedule WHERE id_schedule = @id;", connection);
        command.Parameters.AddWithValue("id", idSchedule);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LookupItem>> GetDirectionsAsync()
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT d.id_direction,
       CASE WHEN p.direction_name IS NOT NULL AND p.direction_name <> ''
            THEN p.direction_name || ' → ' || d.direction_name
            ELSE d.direction_name END AS direction_name
FROM public.direction d
LEFT JOIN public.direction p ON p.id_direction = d.parent_id_direction
WHERE d.is_active = true
ORDER BY COALESCE(p.direction_name, d.direction_name), d.sort_order, d.direction_name;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new LookupItem { Id = ReadInt(reader, "id_direction"), Name = ReadString(reader, "direction_name") });
        return LookupDeduper.ByName(result);
    }

    public static async Task EnsureGroupHasCapacityAsync(int idGroup, int extraSeats = 1)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    COALESCE(NULLIF(g.max_students, 0), NULLIF(cr.capacity, 0), 12) AS capacity,
    COALESCE((
        SELECT COUNT(*)::int FROM public.group_membership gm
        WHERE gm.id_group = g.id_group AND gm.expulsion_date IS NULL
    ), 0) AS enrolled
FROM public.study_group g
LEFT JOIN public.schedule s ON s.id_group = g.id_group AND s.is_active = true
LEFT JOIN public.classroom cr ON cr.id_classroom = s.id_classroom
WHERE g.id_group = @id
LIMIT 1;", connection);
        command.Parameters.AddWithValue("id", idGroup);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Группа не найдена.");

        var capacity = reader.GetInt32(0);
        var enrolled = reader.GetInt32(1);
        if (enrolled + extraSeats > capacity)
            throw new InvalidOperationException(
                $"В группе нет свободных мест (занято {enrolled} из {capacity}).");
    }

    public async Task<IReadOnlyList<LookupItem>> GetTeachersAsync()
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_teacher,
       TRIM(last_name || ' ' || first_name || COALESCE(' ' || middle_name, '')) AS name
FROM public.teacher
WHERE is_active = true
ORDER BY last_name, first_name;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new LookupItem { Id = ReadInt(reader, "id_teacher"), Name = ReadString(reader, "name") });
        return LookupDeduper.ByName(result);
    }

    public async Task<IReadOnlyList<LookupItem>> GetClassroomsAsync(int? idBranch = null)
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_classroom, room_number
FROM public.classroom
WHERE (@id_branch IS NULL OR COALESCE(id_branch, 1) = @id_branch)
ORDER BY room_number;", connection);
        command.Parameters.Add("id_branch", NpgsqlDbType.Smallint).Value =
            idBranch.HasValue ? (object)(short)idBranch.Value : DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new LookupItem { Id = ReadInt(reader, "id_classroom"), Name = $"каб. {ReadString(reader, "room_number")}" });
        return LookupDeduper.ByName(result);
    }

    public async Task<IReadOnlyList<LookupItem>> GetGroupsLookupAsync()
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT g.id_group, d.direction_name || ' — гр. ' || g.group_number AS name
FROM public.study_group g
JOIN public.direction d ON d.id_direction = g.id_direction
WHERE g.is_active = true
ORDER BY d.direction_name, g.group_number;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new LookupItem { Id = ReadInt(reader, "id_group"), Name = ReadString(reader, "name") });
        return LookupDeduper.ByName(result);
    }

    public async Task<IReadOnlyList<TeacherScheduleRow>> GetTeacherScheduleGridAsync(
        int? idTeacherFilter = null,
        bool onlyActiveTeachers = true)
    {
        var teachers = new List<(int IdTeacher, string Name)>();
        var slots = new List<TeacherScheduleSlot>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand(@"
SELECT t.id_teacher,
       TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name
FROM public.teacher t
WHERE (@only_active = false OR t.is_active = true)
  AND (@id_teacher IS NULL OR t.id_teacher = @id_teacher)
ORDER BY t.last_name, t.first_name, t.middle_name;", connection))
        {
            command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActiveTeachers;
            command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
                idTeacherFilter.HasValue ? idTeacherFilter.Value : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                teachers.Add((ReadInt(reader, "id_teacher"), ReadString(reader, "teacher_name")));
            }
        }

        await using (var command = new NpgsqlCommand(@"
SELECT
    t.id_teacher,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    s.day_of_week,
    s.time_start,
    s.time_end,
    d.direction_name,
    g.group_number,
    cr.room_number
FROM public.schedule s
JOIN public.study_group g ON g.id_group = s.id_group
JOIN public.teacher t ON t.id_teacher = g.id_teacher
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.classroom cr ON cr.id_classroom = s.id_classroom
WHERE s.is_active = true
  AND g.is_active = true
  AND (@only_active = false OR t.is_active = true)
  AND (@id_teacher IS NULL OR t.id_teacher = @id_teacher)
ORDER BY t.last_name, t.first_name, s.day_of_week, s.time_start;", connection))
        {
            command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActiveTeachers;
            command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
                idTeacherFilter.HasValue ? idTeacherFilter.Value : DBNull.Value;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                slots.Add(new TeacherScheduleSlot
                {
                    IdTeacher = ReadInt(reader, "id_teacher"),
                    TeacherName = ReadString(reader, "teacher_name"),
                    DayOfWeek = ReadShort(reader, "day_of_week"),
                    TimeStart = ReadTime(reader, "time_start"),
                    TimeEnd = ReadTime(reader, "time_end"),
                    DirectionName = ReadString(reader, "direction_name"),
                    GroupNumber = ReadShort(reader, "group_number"),
                    ClassroomNumber = ReadString(reader, "room_number")
                });
            }
        }

        var slotsByTeacher = slots
            .GroupBy(slot => slot.IdTeacher)
            .ToDictionary(group => group.Key, group => group.ToList());

        if (teachers.Count == 0 && idTeacherFilter.HasValue && slots.Count > 0)
        {
            teachers.Add((idTeacherFilter.Value, slots[0].TeacherName));
        }

        var result = new List<TeacherScheduleRow>();
        foreach (var teacher in teachers)
        {
            var row = new TeacherScheduleRow
            {
                IdTeacher = teacher.IdTeacher,
                TeacherName = teacher.Name
            };

            var totalHours = 0d;
            if (slotsByTeacher.TryGetValue(teacher.IdTeacher, out var teacherSlots))
            {
                foreach (var dayGroup in teacherSlots.GroupBy(slot => slot.DayOfWeek))
                {
                    var lines = dayGroup
                        .OrderBy(slot => slot.TimeStart)
                        .Select(slot => slot.CellLine);
                    row.SetDayText(dayGroup.Key, string.Join("\n\n", lines));
                    totalHours += dayGroup.Sum(slot => slot.DurationHours);
                }
            }

            row.WeeklyLoadText = FormatWeeklyHours(totalHours);
            result.Add(row);
        }

        return result;
    }

    private static string FormatWeeklyHours(double hours)
    {
        if (hours <= 0)
            return "0 ч/нед";

        var rounded = Math.Round(hours, 1);
        return Math.Abs(rounded - Math.Round(rounded)) < 0.05
            ? $"{rounded:0} ч/нед"
            : $"{rounded:0.#} ч/нед";
    }

    private static int ReadInt(IDataRecord r, string n) => r.GetInt32(r.GetOrdinal(n));
    private static short ReadShort(IDataRecord r, string n) => r.GetInt16(r.GetOrdinal(n));
    private static bool ReadBool(IDataRecord r, string n) => r.GetBoolean(r.GetOrdinal(n));
    private static string ReadString(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? string.Empty : r.GetString(i);
    }
    private static string? ReadNullableString(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }
    private static short? ReadNullableShort(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        return r.IsDBNull(i) ? null : r.GetInt16(i);
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
    private static DateTime? ReadNullableDate(IDataRecord r, string n)
    {
        var i = r.GetOrdinal(n);
        if (r.IsDBNull(i)) return null;
        return ReadDate(r, n);
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
