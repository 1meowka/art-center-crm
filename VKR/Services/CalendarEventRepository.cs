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

public sealed class CalendarEventRepository
{
    private readonly GroupsRepository _groups = new();

    public Task<IReadOnlyList<LookupItem>> GetDirectionsAsync() => _groups.GetDirectionsAsync();
    public Task<IReadOnlyList<LookupItem>> GetTeachersAsync() => _groups.GetTeachersAsync();
    public Task<IReadOnlyList<LookupItem>> GetClassroomsAsync(int? idBranch = null) =>
        _groups.GetClassroomsAsync(idBranch);

    public async Task<IReadOnlyList<CalendarEventListItem>> GetEventsAsync(
        DateTime? from = null,
        DateTime? to = null,
        bool onlyActive = false)
    {
        AcademicSchema.Ensure();
        var result = new List<CalendarEventListItem>();
        var dateFrom = from?.Date ?? DateTime.Today.AddMonths(-1);
        var dateTo = to?.Date ?? DateTime.Today.AddMonths(3);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    e.id_event,
    e.event_type,
    e.title,
    e.id_direction,
    COALESCE(d.direction_name, '—') AS direction_name,
    e.id_teacher,
    COALESCE(TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')), '—') AS teacher_name,
    e.id_classroom,
    COALESCE(cr.room_number, '') AS room_number,
    e.scheduled_at,
    e.time_end,
    e.max_participants,
    e.is_active
FROM public.calendar_event e
LEFT JOIN public.direction d ON d.id_direction = e.id_direction
LEFT JOIN public.teacher t ON t.id_teacher = e.id_teacher
LEFT JOIN public.classroom cr ON cr.id_classroom = e.id_classroom
WHERE e.scheduled_at::date BETWEEN @from AND @to
  AND (@only_active = false OR e.is_active = true)
ORDER BY e.scheduled_at, e.title;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo;
        command.Parameters.Add("only_active", NpgsqlDbType.Boolean).Value = onlyActive;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(ReadListItem(reader));

        return result;
    }

    public async Task<CalendarEventEditModel?> GetEditModelAsync(int idEvent)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
SELECT event_type, title, id_direction, id_teacher, id_classroom,
       scheduled_at, time_end, max_participants, description, is_active
FROM public.calendar_event
WHERE id_event = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idEvent;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new CalendarEventEditModel
        {
            IdEvent = idEvent,
            EventType = ReadString(reader, "event_type"),
            Title = ReadString(reader, "title"),
            IdDirection = ReadNullableInt(reader, "id_direction"),
            IdTeacher = ReadNullableInt(reader, "id_teacher"),
            IdClassroom = ReadNullableInt(reader, "id_classroom"),
            ScheduledAt = ReadDateTime(reader, "scheduled_at"),
            TimeEnd = ReadNullableTime(reader, "time_end"),
            MaxParticipants = ReadNullableShort(reader, "max_participants"),
            Description = ReadNullableString(reader, "description"),
            IsActive = ReadBool(reader, "is_active")
        };
    }

    public async Task<int> SaveAsync(CalendarEventEditModel model)
    {
        AcademicSchema.Ensure();
        if (string.IsNullOrWhiteSpace(model.Title))
            model.Title = OptionalFieldDefaults.DictionaryNameOrDefault(null);
        if (model.ScheduledAt == default)
            model.ScheduledAt = DateTime.Today.AddHours(10);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (model.IdEvent == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.calendar_event
    (event_type, title, id_direction, id_teacher, id_classroom,
     scheduled_at, time_end, max_participants, description, is_active)
VALUES
    (@event_type, @title, @id_direction, @id_teacher, @id_classroom,
     @scheduled_at, @time_end, @max_participants, @description, @is_active)
RETURNING id_event;", connection);
            FillParameters(command, model);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        await using var update = new NpgsqlCommand(@"
UPDATE public.calendar_event SET
    event_type = @event_type,
    title = @title,
    id_direction = @id_direction,
    id_teacher = @id_teacher,
    id_classroom = @id_classroom,
    scheduled_at = @scheduled_at,
    time_end = @time_end,
    max_participants = @max_participants,
    description = @description,
    is_active = @is_active
WHERE id_event = @id_event;", connection);
        update.Parameters.Add("id_event", NpgsqlDbType.Integer).Value = model.IdEvent;
        FillParameters(update, model);
        await update.ExecuteNonQueryAsync();
        return model.IdEvent;
    }

    public async Task DeleteAsync(int idEvent)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM public.calendar_event WHERE id_event = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idEvent;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TrialLessonSlotItem>> GetTrialSlotsForDateAsync(
        DateTime date,
        IReadOnlyList<int>? directionIds = null)
    {
        AcademicSchema.Ensure();
        var result = new List<TrialLessonSlotItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    e.id_event,
    e.event_type,
    e.title,
    COALESCE(e.id_direction, 0) AS id_direction,
    COALESCE(d.direction_name, e.title) AS direction_name,
    COALESCE(TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')), '—') AS teacher_name,
    COALESCE(cr.room_number, '') AS room_number,
    e.scheduled_at,
    e.time_end,
    COALESCE(e.max_participants, 0) AS max_participants
FROM public.calendar_event e
LEFT JOIN public.direction d ON d.id_direction = e.id_direction
LEFT JOIN public.teacher t ON t.id_teacher = e.id_teacher
LEFT JOIN public.classroom cr ON cr.id_classroom = e.id_classroom
WHERE e.is_active = true
  AND e.scheduled_at::date = @date
  AND (@filter_directions = false OR e.id_direction = ANY(@direction_ids))
ORDER BY e.scheduled_at, e.title;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("date", NpgsqlDbType.Date).Value = date.Date;
        var hasDirections = directionIds is { Count: > 0 };
        command.Parameters.Add("filter_directions", NpgsqlDbType.Boolean).Value = hasDirections;
        command.Parameters.Add("direction_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value =
            hasDirections ? directionIds!.ToArray() : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var scheduledAt = ReadDateTime(reader, "scheduled_at");
            var timeEnd = ReadNullableTime(reader, "time_end") ?? scheduledAt.TimeOfDay.Add(TimeSpan.FromHours(1.5));
            var maxParticipants = ReadInt(reader, "max_participants");

            result.Add(new TrialLessonSlotItem
            {
                IdEvent = ReadInt(reader, "id_event"),
                IdDirection = ReadInt(reader, "id_direction"),
                DirectionName = ReadString(reader, "direction_name"),
                EventTitle = ReadString(reader, "title"),
                EventType = ReadString(reader, "event_type"),
                TeacherName = ReadString(reader, "teacher_name"),
                ClassroomNumber = ReadString(reader, "room_number"),
                ScheduledAt = scheduledAt,
                TimeEnd = timeEnd,
                Capacity = maxParticipants > 0 ? maxParticipants : 12
            });
        }

        return result;
    }

    public async Task EnrichEventSlotsWithOccupancyAsync(IList<TrialLessonSlotItem> slots)
    {
        var eventSlots = new List<TrialLessonSlotItem>();
        foreach (var slot in slots)
        {
            if (slot.IsCalendarEvent)
                eventSlots.Add(slot);
        }

        if (eventSlots.Count == 0)
            return;

        var eventIds = new int[eventSlots.Count];
        for (var i = 0; i < eventSlots.Count; i++)
            eventIds[i] = eventSlots[i].IdEvent;

        var occupancy = await GetEventOccupancyAsync(eventIds);
        foreach (var slot in eventSlots)
        {
            if (!occupancy.TryGetValue(slot.IdEvent, out var info))
                continue;

            slot.Capacity = info.Capacity;
            slot.EnrolledCount = 0;
            slot.TrialBookedCount = info.TrialBookedCount;
            slot.FreeSeats = info.FreeSeats;
            slot.IsFull = info.IsFull;
        }
    }

    public async Task EnsureEventAvailableAsync(int idEvent, DateTime scheduledAt)
    {
        var map = await GetEventOccupancyAsync(new[] { idEvent });
        if (!map.TryGetValue(idEvent, out var info))
            throw new InvalidOperationException("Событие не найдено.");
        if (info.IsFull)
            throw new InvalidOperationException(
                $"На «{scheduledAt:dd.MM.yyyy HH:mm}» нет свободных мест. Выберите другую дату.");
    }

    private async Task<Dictionary<int, TrialSlotOccupancyInfo>> GetEventOccupancyAsync(IReadOnlyList<int> eventIds)
    {
        var result = new Dictionary<int, TrialSlotOccupancyInfo>();
        if (eventIds.Count == 0)
            return result;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    e.id_event,
    COALESCE(NULLIF(e.max_participants, 0), NULLIF(cr.capacity, 0), 12) AS capacity,
    COALESCE((
        SELECT COUNT(*)::int
        FROM public.trial_lesson tl
        WHERE tl.id_event = e.id_event
          AND COALESCE(tl.result, '') NOT IN ('перенесено', 'не_пришёл', 'не_пришел')
    ), 0) AS trial_count
FROM public.calendar_event e
LEFT JOIN public.classroom cr ON cr.id_classroom = e.id_classroom
WHERE e.id_event = ANY(@event_ids);";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("event_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = eventIds.ToArray();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var idEvent = ReadInt(reader, "id_event");
            var capacity = ReadInt(reader, "capacity");
            var trials = ReadInt(reader, "trial_count");
            result[idEvent] = new TrialSlotOccupancyInfo
            {
                Capacity = capacity,
                EnrolledCount = 0,
                TrialBookedCount = trials,
                FreeSeats = Math.Max(0, capacity - trials),
                IsFull = trials >= capacity
            };
        }

        return result;
    }

    private static void FillParameters(NpgsqlCommand command, CalendarEventEditModel model)
    {
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = model.EventType;
        command.Parameters.Add("title", NpgsqlDbType.Varchar).Value = model.Title.Trim();
        command.Parameters.Add("id_direction", NpgsqlDbType.Integer).Value =
            model.IdDirection is > 0 ? model.IdDirection.Value : DBNull.Value;
        command.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
            model.IdTeacher is > 0 ? model.IdTeacher.Value : DBNull.Value;
        command.Parameters.Add("id_classroom", NpgsqlDbType.Integer).Value =
            model.IdClassroom is > 0 ? model.IdClassroom.Value : DBNull.Value;
        command.Parameters.Add("scheduled_at", NpgsqlDbType.Timestamp).Value = model.ScheduledAt;
        command.Parameters.Add("time_end", NpgsqlDbType.Time).Value =
            model.TimeEnd.HasValue ? model.TimeEnd.Value : DBNull.Value;
        command.Parameters.Add("max_participants", NpgsqlDbType.Smallint).Value =
            model.MaxParticipants is > 0 ? model.MaxParticipants.Value : DBNull.Value;
        command.Parameters.Add("description", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(model.Description) ? DBNull.Value : model.Description.Trim();
        command.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = model.IsActive;
    }

    private static CalendarEventListItem ReadListItem(IDataRecord reader) =>
        new()
        {
            IdEvent = ReadInt(reader, "id_event"),
            EventType = ReadString(reader, "event_type"),
            Title = ReadString(reader, "title"),
            IdDirection = ReadNullableInt(reader, "id_direction"),
            DirectionName = ReadString(reader, "direction_name"),
            IdTeacher = ReadNullableInt(reader, "id_teacher"),
            TeacherName = ReadString(reader, "teacher_name"),
            IdClassroom = ReadNullableInt(reader, "id_classroom"),
            ClassroomNumber = ReadString(reader, "room_number"),
            ScheduledAt = ReadDateTime(reader, "scheduled_at"),
            TimeEnd = ReadNullableTime(reader, "time_end"),
            MaxParticipants = ReadNullableShort(reader, "max_participants"),
            IsActive = ReadBool(reader, "is_active")
        };

    private static int ReadInt(IDataRecord reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    private static short ReadShort(IDataRecord reader, string name) => reader.GetInt16(reader.GetOrdinal(name));
    private static short? ReadNullableShort(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt16(reader.GetOrdinal(name));
    private static int? ReadNullableInt(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(reader.GetOrdinal(name));
    private static string ReadString(IDataRecord reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static string? ReadNullableString(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static bool ReadBool(IDataRecord reader, string name) => reader.GetBoolean(reader.GetOrdinal(name));
    private static DateTime ReadDateTime(IDataRecord reader, string name) => reader.GetDateTime(reader.GetOrdinal(name));

    private static TimeSpan? ReadNullableTime(IDataRecord reader, string name)
    {
        if (reader.IsDBNull(reader.GetOrdinal(name)))
            return null;
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            TimeSpan ts => ts,
            DateTime dt => dt.TimeOfDay,
            _ => null
        };
    }
}
