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

public sealed class TrialLessonRepository
{
    private readonly CalendarEventRepository _calendarEvents = new();

    public async Task<IReadOnlyList<TrialLessonSlotItem>> GetSlotsForDateAsync(
        DateTime date,
        IReadOnlyList<int>? directionIds = null)
    {
        var result = new List<TrialLessonSlotItem>();
        var dayOfWeek = ToDbDayOfWeek(date);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var sql = @"
SELECT
    sc.id_schedule,
    sc.id_group,
    g.id_direction,
    d.direction_name,
    g.group_number,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
    cr.room_number,
    sc.time_start,
    sc.time_end,
    COALESCE(sc.lesson_type, 'regular') AS lesson_type
FROM public.schedule sc
JOIN public.study_group g ON g.id_group = sc.id_group AND g.is_active = true
JOIN public.direction d ON d.id_direction = g.id_direction AND COALESCE(d.is_active, true) = true
JOIN public.teacher t ON t.id_teacher = g.id_teacher
JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
WHERE sc.is_active = true
  AND sc.day_of_week = @day_of_week
  AND (@filter_directions = false OR g.id_direction = ANY(@direction_ids))
ORDER BY sc.time_start, d.direction_name, g.group_number;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("day_of_week", NpgsqlDbType.Smallint).Value = dayOfWeek;
        var hasDirections = directionIds is { Count: > 0 };
        command.Parameters.Add("filter_directions", NpgsqlDbType.Boolean).Value = hasDirections;
        command.Parameters.Add("direction_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value =
            hasDirections ? directionIds!.ToArray() : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var timeStart = ReadTimeSpan(reader, "time_start");
            result.Add(new TrialLessonSlotItem
            {
                IdSchedule = ReadInt(reader, "id_schedule"),
                IdGroup = ReadInt(reader, "id_group"),
                IdDirection = ReadInt(reader, "id_direction"),
                DirectionName = ReadString(reader, "direction_name"),
                GroupNumber = ReadShort(reader, "group_number"),
                LessonType = ReadNullableString(reader, "lesson_type") ?? "regular",
                TeacherName = ReadString(reader, "teacher_name"),
                ClassroomNumber = ReadString(reader, "room_number"),
                ScheduledAt = date.Date.Add(timeStart),
                TimeEnd = ReadTimeSpan(reader, "time_end")
            });
        }

        var events = await _calendarEvents.GetTrialSlotsForDateAsync(date, directionIds);
        foreach (var ev in events)
            result.Add(ev);

        result.Sort((a, b) =>
        {
            var cmp = a.ScheduledAt.CompareTo(b.ScheduledAt);
            return cmp != 0 ? cmp : string.Compare(a.GroupTitle, b.GroupTitle, StringComparison.Ordinal);
        });

        return result;
    }

    public async Task EnrichSlotsWithOccupancyAsync(IList<TrialLessonSlotItem> slots)
    {
        if (slots.Count == 0)
            return;

        var scheduleIds = slots.Where(x => x.IdSchedule > 0).Select(x => x.IdSchedule).Distinct().ToArray();
        if (scheduleIds.Length > 0)
        {
            var date = slots[0].ScheduledAt.Date;
            var occupancy = await GetSlotOccupancyAsync(scheduleIds, date);

            foreach (var slot in slots)
            {
                if (slot.IdSchedule <= 0)
                    continue;
                if (!occupancy.TryGetValue(slot.IdSchedule, out var info))
                    continue;

                slot.Capacity = info.Capacity;
                slot.EnrolledCount = info.EnrolledCount;
                slot.TrialBookedCount = info.TrialBookedCount;
                slot.FreeSeats = info.FreeSeats;
                slot.IsFull = info.IsFull;
            }
        }

        await _calendarEvents.EnrichEventSlotsWithOccupancyAsync(slots);

        foreach (var slot in slots)
        {
            if (slot.Capacity <= 0)
                slot.Capacity = 12;

            if (slot.OccupiedSeats >= slot.Capacity && slot.Capacity > 0)
                slot.IsFull = true;

            if (slot.IsFull)
                slot.FreeSeats = 0;
            else if (slot.FreeSeats <= 0)
                slot.FreeSeats = Math.Max(0, slot.Capacity - slot.OccupiedSeats);
        }
    }

    public async Task EnsureSlotAvailableAsync(TrialLessonBookModel booking)
    {
        if (booking.IdEvent > 0)
        {
            await _calendarEvents.EnsureEventAvailableAsync(booking.IdEvent, booking.ScheduledAt);
            await EnsureNoDuplicateBookingAsync(booking);
            return;
        }

        if (booking.IdSchedule <= 0 && booking.IdEvent <= 0)
            return;

        var map = await GetSlotOccupancyAsync(new[] { booking.IdSchedule }, booking.ScheduledAt.Date);
        if (!map.TryGetValue(booking.IdSchedule, out var info))
            throw new InvalidOperationException("Слот расписания не найден.");

        if (info.IsFull)
            throw new InvalidOperationException(
                $"В группе нет свободных мест на {booking.ScheduledAt:dd.MM.yyyy HH:mm}. " +
                "Выберите другую дату или время.");

        await EnsureNoDuplicateBookingAsync(booking);
    }

    private async Task EnsureNoDuplicateBookingAsync(TrialLessonBookModel booking)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var duplicateCmd = new NpgsqlCommand(@"
SELECT COUNT(*)::int
FROM public.trial_lesson
WHERE id_lead = @lead
  AND scheduled_at = @scheduled_at
  AND COALESCE(result, '') NOT IN ('перенесено', 'не_пришёл', 'не_пришел');", connection);
        duplicateCmd.Parameters.Add("lead", NpgsqlDbType.Integer).Value = booking.IdLead;
        duplicateCmd.Parameters.Add("scheduled_at", NpgsqlDbType.Timestamp).Value = booking.ScheduledAt;
        var duplicateCount = Convert.ToInt32(await duplicateCmd.ExecuteScalarAsync());
        if (duplicateCount > 0)
            throw new InvalidOperationException("Эта карточка гостя уже записана на выбранное пробное занятие.");
    }

    private async Task<Dictionary<int, TrialSlotOccupancyInfo>> GetSlotOccupancyAsync(
        IReadOnlyList<int> scheduleIds,
        DateTime date)
    {
        AcademicSchema.Ensure();
        var result = new Dictionary<int, TrialSlotOccupancyInfo>();
        if (scheduleIds.Count == 0)
            return result;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    sc.id_schedule,
    COALESCE(NULLIF(g.max_students, 0), NULLIF(cr.capacity, 0), 12) AS capacity,
    COALESCE((
        SELECT COUNT(*)::int
        FROM public.group_membership gm
        WHERE gm.id_group = sc.id_group
          AND gm.expulsion_date IS NULL
    ), 0) AS enrolled_count,
    COALESCE((
        SELECT COUNT(*)::int
        FROM public.trial_lesson tl
        WHERE tl.scheduled_at::date = @date
          AND COALESCE(tl.result, '') NOT IN ('перенесено', 'не_пришёл', 'не_пришел')
          AND (
              tl.id_schedule = sc.id_schedule
              OR (
                  tl.id_schedule IS NULL
                  AND tl.scheduled_at::time = sc.time_start
                  AND EXISTS (
                      SELECT 1
                      FROM public.lead_request lr
                      JOIN public.study_group g2 ON g2.id_direction = lr.id_direction
                      WHERE lr.id_lead = tl.id_lead
                        AND g2.id_group = sc.id_group
                  )
              )
          )
    ), 0) AS trial_count
FROM public.schedule sc
JOIN public.study_group g ON g.id_group = sc.id_group
JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
WHERE sc.id_schedule = ANY(@schedule_ids);";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("date", NpgsqlDbType.Date).Value = date.Date;
        command.Parameters.Add("schedule_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = scheduleIds.ToArray();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var idSchedule = ReadInt(reader, "id_schedule");
            var capacity = ReadInt(reader, "capacity");
            var enrolled = ReadInt(reader, "enrolled_count");
            var trials = ReadInt(reader, "trial_count");
            var occupied = enrolled + trials;

            result[idSchedule] = new TrialSlotOccupancyInfo
            {
                Capacity = capacity,
                EnrolledCount = enrolled,
                TrialBookedCount = trials,
                FreeSeats = Math.Max(0, capacity - occupied),
                IsFull = capacity > 0 && occupied >= capacity
            };
        }

        return result;
    }

    public async Task<IReadOnlyList<TrialAvailableDateItem>> GetAvailableDatesAsync(
        IReadOnlyList<int> directionIds,
        DateTime from,
        DateTime to)
    {
        if (directionIds.Count == 0)
            return Array.Empty<TrialAvailableDateItem>();

        var patterns = await GetSchedulePatternsForDirectionsAsync(directionIds);

        var result = new List<TrialAvailableDateItem>();
        var start = from.Date;
        var end = to.Date;
        if (end < start)
            (start, end) = (end, start);

        if (patterns.Count > 0)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var dayOfWeek = ToDbDayOfWeek(date);
                var count = patterns.Count(x => x.DayOfWeek == dayOfWeek);
                if (count > 0)
                {
                    result.Add(new TrialAvailableDateItem
                    {
                        Date = date,
                        SlotsCount = count
                    });
                }
            }
        }

        var eventDates = await GetCalendarEventDatesAsync(directionIds, start, end);
        foreach (var (eventDate, count) in eventDates)
        {
            var existing = result.Find(x => x.Date == eventDate);
            if (existing != null)
                existing.SlotsCount += count;
            else
            {
                result.Add(new TrialAvailableDateItem
                {
                    Date = eventDate,
                    SlotsCount = count
                });
            }
        }

        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return result;
    }

    public async Task<IReadOnlyList<LookupItem>> GetDirectionsForDateAsync(DateTime date)
    {
        var slots = await GetSlotsForDateAsync(date);
        return slots
            .GroupBy(x => x.IdDirection)
            .Select(g => new LookupItem { Id = g.Key, Name = g.First().DirectionName })
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<TrialLessonListItem>> GetBookedTrialsAsync(
        DateTime? from = null,
        DateTime? to = null,
        IReadOnlyList<int>? directionIds = null)
    {
        var result = new List<TrialLessonListItem>();
        var dateFrom = from?.Date ?? DateTime.Today.AddDays(-7);
        var dateTo = to?.Date ?? DateTime.Today.AddDays(60);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    tl.id_trial,
    tl.id_lead,
    tl.scheduled_at,
    tl.result,
    TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')) AS child_full_name,
    lr.contact_phone,
    COALESCE(d.direction_name, '') AS direction_name,
    lr.status,
    COALESCE(slot.teacher_name, '—') AS teacher_name,
    COALESCE(slot.group_title, d.direction_name) AS group_title,
    COALESCE(slot.classroom_text, '—') AS classroom_text,
    slot.time_end
FROM public.trial_lesson tl
JOIN public.lead_request lr ON lr.id_lead = tl.id_lead
JOIN public.direction d ON d.id_direction = lr.id_direction
LEFT JOIN LATERAL (
    SELECT
        TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')) AS teacher_name,
        d2.direction_name || ' — гр. ' || g.group_number::text AS group_title,
        CASE WHEN cr.room_number IS NULL OR cr.room_number = '' THEN '—' ELSE 'каб. ' || cr.room_number END AS classroom_text,
        sc.time_end
    FROM public.schedule sc
    JOIN public.study_group g ON g.id_group = sc.id_group AND g.id_direction = lr.id_direction
    JOIN public.direction d2 ON d2.id_direction = g.id_direction
    JOIN public.teacher t ON t.id_teacher = g.id_teacher
    JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
    WHERE sc.is_active = true
      AND sc.day_of_week = CASE
            WHEN EXTRACT(ISODOW FROM tl.scheduled_at)::int = 7 THEN 7
            ELSE EXTRACT(ISODOW FROM tl.scheduled_at)::int
          END
      AND sc.time_start = tl.scheduled_at::time
    ORDER BY sc.id_schedule
    LIMIT 1
) slot ON TRUE
WHERE tl.scheduled_at::date BETWEEN @from AND @to
  AND (@filter_directions = false OR lr.id_direction = ANY(@direction_ids))
ORDER BY tl.scheduled_at, d.direction_name, child_full_name;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo;
        var hasDirections = directionIds is { Count: > 0 };
        command.Parameters.Add("filter_directions", NpgsqlDbType.Boolean).Value = hasDirections;
        command.Parameters.Add("direction_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value =
            hasDirections ? directionIds!.ToArray() : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TrialLessonListItem
            {
                IdTrial = ReadInt(reader, "id_trial"),
                IdLead = ReadInt(reader, "id_lead"),
                ScheduledAt = ReadDateTime(reader, "scheduled_at"),
                Result = ReadNullableString(reader, "result"),
                ChildFullName = ReadString(reader, "child_full_name"),
                ContactPhone = ReadString(reader, "contact_phone"),
                DirectionName = ReadString(reader, "direction_name"),
                LeadStatus = ReadString(reader, "status"),
                TeacherName = ReadString(reader, "teacher_name"),
                GroupTitle = ReadString(reader, "group_title"),
                ClassroomText = ReadString(reader, "classroom_text"),
                TimeEnd = reader.IsDBNull(reader.GetOrdinal("time_end"))
                    ? null
                    : ReadTimeSpan(reader, "time_end")
            });
        }

        return result;
    }

    public async Task<int> BookTrialAsync(TrialLessonBookModel model)
    {
        if (model.IdLead <= 0)
            model.IdLead = 1;
        if (model.ScheduledAt == default)
            model.ScheduledAt = DateTime.Today.AddHours(10);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var insert = new NpgsqlCommand(@"
INSERT INTO public.trial_lesson (id_lead, scheduled_at, result)
VALUES (@id_lead, @scheduled_at, NULL)
RETURNING id_trial;", connection, transaction))
        {
            insert.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = model.IdLead;
            insert.Parameters.Add("scheduled_at", NpgsqlDbType.Timestamp).Value = model.ScheduledAt;
            var idTrial = Convert.ToInt32(await insert.ExecuteScalarAsync());

            await using var updateLead = new NpgsqlCommand(@"
UPDATE public.lead_request
SET status = CASE WHEN status = 'новая' THEN 'подтверждена' ELSE status END
WHERE id_lead = @id_lead;", connection, transaction);
            updateLead.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = model.IdLead;
            await updateLead.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return idTrial;
        }
    }

    public async Task<int> BookTrialWithPaymentAsync(
        TrialLessonBookModel booking,
        string childFullName,
        string clientFullName,
        string contactPhone,
        string? childPhone,
        int? idPaymentMethod,
        string slotSummary,
        TimeSpan? lessonEnd = null,
        decimal? amount = null)
    {
        if (booking.IdLead <= 0)
            booking.IdLead = 1;
        if (booking.ScheduledAt == default)
            booking.ScheduledAt = DateTime.Today.AddHours(10);

        var price = amount ?? TrialLessonDefaults.Price;
        var isFree = price <= 0;
        if (!isFree && (idPaymentMethod is not > 0))
            idPaymentMethod = 1;

        AcademicSchema.Ensure();
        if (booking.IdSchedule > 0 || booking.IdEvent > 0)
            await EnsureSlotAvailableAsync(booking);
        else
            await EnsureNoDuplicateBookingAsync(booking);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        if (!isFree)
        {
            var saleComment =
                $"Пробное занятие. Карточка гостя №{booking.IdLead}, {childFullName}, {contactPhone}. {slotSummary}";

            await using var saleCmd = new NpgsqlCommand(@"
INSERT INTO public.shop_sale
(sale_date, id_student, id_product, item_name, quantity,
 sale_price, unit_price, total_amount, id_payment_method, comment, created_at)
VALUES
(@dt, NULL, NULL, @name, 1, @price, @price, @price, @method, @comment, CURRENT_TIMESTAMP)
RETURNING id_sale;", connection, transaction);
            saleCmd.Parameters.Add("dt", NpgsqlDbType.Date).Value = DateTime.Today;
            saleCmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = TrialLessonDefaults.SaleItemName;
            saleCmd.Parameters.Add("price", NpgsqlDbType.Numeric).Value = price;
            saleCmd.Parameters.Add("method", NpgsqlDbType.Integer).Value = idPaymentMethod!.Value;
            saleCmd.Parameters.Add("comment", NpgsqlDbType.Text).Value = saleComment;
            await saleCmd.ExecuteScalarAsync();
        }

        int idTrial;
        await using (var trialCmd = new NpgsqlCommand(@"
INSERT INTO public.trial_lesson (id_lead, scheduled_at, result, id_schedule, id_event, id_group, id_teacher)
VALUES (@id_lead, @scheduled_at, NULL, @id_schedule, @id_event, @id_group, @id_teacher)
RETURNING id_trial;", connection, transaction))
        {
            trialCmd.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = booking.IdLead;
            trialCmd.Parameters.Add("scheduled_at", NpgsqlDbType.Timestamp).Value = booking.ScheduledAt;
            trialCmd.Parameters.Add("id_schedule", NpgsqlDbType.Integer).Value =
                booking.IdSchedule > 0 ? booking.IdSchedule : DBNull.Value;
            trialCmd.Parameters.Add("id_event", NpgsqlDbType.Integer).Value =
                booking.IdEvent > 0 ? booking.IdEvent : DBNull.Value;
            trialCmd.Parameters.Add("id_group", NpgsqlDbType.Integer).Value =
                booking.IdGroup > 0 ? booking.IdGroup : DBNull.Value;
            trialCmd.Parameters.Add("id_teacher", NpgsqlDbType.Integer).Value =
                booking.IdTeacher > 0 ? booking.IdTeacher : DBNull.Value;
            idTrial = Convert.ToInt32(await trialCmd.ExecuteScalarAsync());
        }

        await using (var leadCmd = new NpgsqlCommand(@"
UPDATE public.lead_request
SET status = CASE WHEN status = 'новая' THEN 'подтверждена' ELSE status END
WHERE id_lead = @id_lead;", connection, transaction))
        {
            leadCmd.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = booking.IdLead;
            await leadCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        try
        {
            await new TrialFollowupRepository().ScheduleForTrialAsync(
                idTrial,
                booking.IdLead,
                booking.ScheduledAt,
                lessonEnd,
                childFullName,
                clientFullName,
                contactPhone,
                childPhone);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Follow-up not scheduled: {ex}");
        }

        return idTrial;
    }

    public async Task UpdateTrialResultAsync(int idTrial, string? result)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var update = new NpgsqlCommand(@"
UPDATE public.trial_lesson
SET result = @result
WHERE id_trial = @id_trial
RETURNING id_lead;", connection, transaction))
        {
            update.Parameters.Add("id_trial", NpgsqlDbType.Integer).Value = idTrial;
            update.Parameters.Add("result", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(result) ? DBNull.Value : result.Trim();
            var idLeadObj = await update.ExecuteScalarAsync();
            if (idLeadObj == null || idLeadObj == DBNull.Value)
                throw new InvalidOperationException("Пробное занятие не найдено.");

            var idLead = Convert.ToInt32(idLeadObj);
            if (!string.IsNullOrWhiteSpace(result))
            {
                var leadStatus = result.Trim() switch
                {
                    "заключил_договор" => "конвертирована",
                    "не_понравилось" or "не_пришёл" or "не_пришел" => "отказ",
                    _ => "посещено"
                };

                await using var updateLead = new NpgsqlCommand(@"
UPDATE public.lead_request
SET status = @status
WHERE id_lead = @id_lead;", connection, transaction);
                updateLead.Parameters.Add("status", NpgsqlDbType.Text).Value = leadStatus;
                updateLead.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = idLead;
                await updateLead.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
    }

    private async Task<IReadOnlyList<(DateTime Date, int Count)>> GetCalendarEventDatesAsync(
        IReadOnlyList<int> directionIds,
        DateTime start,
        DateTime end)
    {
        AcademicSchema.Ensure();
        var result = new List<(DateTime, int)>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT e.scheduled_at::date AS event_date, COUNT(*)::int AS cnt
FROM public.calendar_event e
WHERE e.is_active = true
  AND e.scheduled_at::date BETWEEN @from AND @to
  AND e.id_direction = ANY(@direction_ids)
GROUP BY e.scheduled_at::date;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = start;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = end;
        command.Parameters.Add("direction_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = directionIds.ToArray();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetDateTime(0), reader.GetInt32(1)));

        return result;
    }

    private async Task<IReadOnlyList<(int IdDirection, short DayOfWeek)>> GetSchedulePatternsForDirectionsAsync(
        IReadOnlyList<int> directionIds)
    {
        var result = new List<(int, short)>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT DISTINCT g.id_direction, sc.day_of_week
FROM public.schedule sc
JOIN public.study_group g ON g.id_group = sc.id_group AND g.is_active = true
WHERE sc.is_active = true
  AND g.id_direction = ANY(@direction_ids);";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("direction_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = directionIds.ToArray();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((ReadInt(reader, "id_direction"), ReadShort(reader, "day_of_week")));

        return result;
    }

    public async Task<IReadOnlyList<TrialLeadHistoryItem>> GetLeadTrialHistoryAsync(int idLead)
    {
        AcademicSchema.Ensure();
        var result = new List<TrialLeadHistoryItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    tl.id_trial,
    tl.scheduled_at,
    tl.result,
    COALESCE(d.direction_name, '') AS direction_name,
    COALESCE(
        TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, '')),
        '—'
    ) AS teacher_name,
    CASE WHEN sg.id_group IS NOT NULL
         THEN d2.direction_name || ' — гр.' || sg.group_number::text
         ELSE '—'
    END AS group_display,
    COALESCE(f.is_completed, false) AS has_feedback,
    COALESCE(NULLIF(f.feedback_text, ''), f.contact_note) AS feedback_text
FROM public.trial_lesson tl
LEFT JOIN public.lead_request lr ON lr.id_lead = tl.id_lead
LEFT JOIN public.direction d ON d.id_direction = lr.id_direction
LEFT JOIN public.study_group sg ON sg.id_group = tl.id_group
LEFT JOIN public.direction d2 ON d2.id_direction = sg.id_direction
LEFT JOIN public.teacher t ON t.id_teacher = COALESCE(tl.id_teacher, sg.id_teacher)
LEFT JOIN public.trial_lesson_followup f ON f.id_trial = tl.id_trial
WHERE tl.id_lead = @id_lead
ORDER BY tl.scheduled_at DESC;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("id_lead", NpgsqlDbType.Integer).Value = idLead;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TrialLeadHistoryItem
            {
                IdTrial = ReadInt(reader, "id_trial"),
                ScheduledAt = ReadDateTime(reader, "scheduled_at"),
                Result = ReadNullableString(reader, "result"),
                DirectionName = ReadString(reader, "direction_name"),
                TeacherName = ReadString(reader, "teacher_name"),
                GroupDisplay = ReadString(reader, "group_display"),
                HasFeedback = reader.GetBoolean(reader.GetOrdinal("has_feedback")),
                FeedbackText = ReadNullableString(reader, "feedback_text")
            });
        }

        return result;
    }

    public async Task<TrialLeadStats> GetLeadTrialStatsAsync(int idLead)
    {
        var history = await GetLeadTrialHistoryAsync(idLead);
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT status FROM public.lead_request WHERE id_lead = @id;", connection);
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
        var status = (await cmd.ExecuteScalarAsync()) as string ?? string.Empty;

        var converted = string.Equals(status, "конвертирована", StringComparison.OrdinalIgnoreCase)
            || history.Any(h => string.Equals(h.Result, "заключил_договор", StringComparison.OrdinalIgnoreCase));

        return new TrialLeadStats
        {
            TotalTrials = history.Count,
            WithFeedback = history.Count(h => h.HasFeedback),
            ConvertedToContract = converted
        };
    }

    public async Task<IReadOnlyList<TrialContractCandidateItem>> GetContractCandidatesAsync()
    {
        var result = new List<TrialContractCandidateItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT DISTINCT ON (tl.id_lead)
    tl.id_trial,
    tl.id_lead,
    lr.id_direction,
    tl.scheduled_at,
    tl.result,
    lr.status AS lead_status,
    TRIM(COALESCE(lr.client_last_name, '') || ' ' || COALESCE(lr.client_first_name, '')) AS client_name,
    TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')) AS child_name,
    COALESCE(lr.contact_phone, '') AS contact_phone,
    COALESCE(d.direction_name, '') AS direction_name,
    (
        SELECT f.feedback_code
        FROM public.trial_lesson_followup f
        WHERE f.id_trial = tl.id_trial AND f.is_completed
        ORDER BY f.completed_at DESC NULLS LAST
        LIMIT 1
    ) AS followup_feedback
FROM public.trial_lesson tl
JOIN public.lead_request lr ON lr.id_lead = tl.id_lead
LEFT JOIN public.direction d ON d.id_direction = lr.id_direction
WHERE lr.status NOT IN ('конвертирована', 'отказ')
  AND tl.scheduled_at <= CURRENT_TIMESTAMP
  AND COALESCE(tl.result, '') NOT IN ('не_пришёл', 'не_пришел', 'не_понравилось', 'перенесено')
  AND (
        tl.result IN ('понравилось', 'заключил_договор')
        OR EXISTS (
            SELECT 1
            FROM public.trial_lesson_followup f
            WHERE f.id_trial = tl.id_trial
              AND f.is_completed
              AND COALESCE(f.feedback_code, '') IN ('contacted_done', 'client_will_call', 'will_think')
        )
        OR (tl.result IS NULL AND tl.scheduled_at < CURRENT_TIMESTAMP - INTERVAL '30 minutes')
      )
ORDER BY tl.id_lead, tl.scheduled_at DESC;";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TrialContractCandidateItem
            {
                IdTrial = ReadInt(reader, "id_trial"),
                IdLead = ReadInt(reader, "id_lead"),
                IdDirection = reader.IsDBNull(reader.GetOrdinal("id_direction"))
                    ? null : ReadInt(reader, "id_direction"),
                ScheduledAt = ReadDateTime(reader, "scheduled_at"),
                Result = ReadNullableString(reader, "result"),
                ClientFullName = ReadString(reader, "client_name"),
                ChildFullName = ReadString(reader, "child_name"),
                ContactPhone = ReadString(reader, "contact_phone"),
                DirectionName = ReadString(reader, "direction_name"),
                LeadStatus = ReadString(reader, "lead_status"),
                FollowupFeedback = ReadNullableString(reader, "followup_feedback")
            });
        }

        return result;
    }

    private static short ToDbDayOfWeek(DateTime date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)date.DayOfWeek;

    private static int ReadInt(IDataRecord reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    private static short ReadShort(IDataRecord reader, string name) => reader.GetInt16(reader.GetOrdinal(name));
    private static string ReadString(IDataRecord reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static string? ReadNullableString(IDataRecord reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
    private static DateTime ReadDateTime(IDataRecord reader, string name) => reader.GetDateTime(reader.GetOrdinal(name));

    private static TimeSpan ReadTimeSpan(IDataRecord reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            TimeSpan ts => ts,
            DateTime dt => dt.TimeOfDay,
            _ => TimeSpan.Zero
        };
    }
}

public sealed class TrialSlotOccupancyInfo
{
    public int Capacity { get; set; }
    public int EnrolledCount { get; set; }
    public int TrialBookedCount { get; set; }
    public int FreeSeats { get; set; }
    public bool IsFull { get; set; }
}
