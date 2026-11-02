#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class TrialFollowupRepository
{
    public async Task ScheduleForTrialAsync(
        int idTrial,
        int idLead,
        DateTime scheduledAt,
        TimeSpan? lessonEnd,
        string childFullName,
        string clientFullName,
        string parentPhone,
        string? childPhone)
    {
        AcademicSchema.Ensure();
        var lessonEndAt = lessonEnd.HasValue
            ? scheduledAt.Date.Add(lessonEnd.Value)
            : scheduledAt.AddHours(1);
        var dueAt = lessonEndAt.AddMinutes(30);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
INSERT INTO public.trial_lesson_followup
    (id_trial, id_lead, due_at, parent_phone, child_phone, child_full_name, client_full_name, task_status)
VALUES (@trial, @lead, @due, @parent, @child, @name, @client, 'позвонить_после_пробного')
ON CONFLICT (id_trial) DO UPDATE SET
    id_lead = EXCLUDED.id_lead,
    due_at = EXCLUDED.due_at,
    parent_phone = EXCLUDED.parent_phone,
    child_phone = EXCLUDED.child_phone,
    child_full_name = EXCLUDED.child_full_name,
    client_full_name = EXCLUDED.client_full_name,
    task_status = 'позвонить_после_пробного',
    is_completed = FALSE,
    on_control = FALSE,
    next_reminder_at = NULL;", connection);
        command.Parameters.Add("trial", NpgsqlDbType.Integer).Value = idTrial;
        command.Parameters.Add("lead", NpgsqlDbType.Integer).Value = idLead;
        command.Parameters.Add("due", NpgsqlDbType.TimestampTz).Value = dueAt;
        command.Parameters.Add("parent", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(parentPhone) ? (object)DBNull.Value : parentPhone.Trim();
        command.Parameters.Add("child", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(childPhone) ? (object)DBNull.Value : childPhone.Trim();
        command.Parameters.Add("name", NpgsqlDbType.Varchar).Value = childFullName.Trim();
        command.Parameters.Add("client", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(clientFullName) ? (object)DBNull.Value : clientFullName.Trim();
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountDueAsync()
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT COUNT(*)::int
FROM public.trial_lesson_followup f
WHERE NOT f.is_completed
  AND (
        f.due_at <= CURRENT_TIMESTAMP + INTERVAL '1 day'
        OR (f.on_control AND f.next_reminder_at IS NOT NULL AND f.next_reminder_at <= CURRENT_TIMESTAMP + INTERVAL '1 day')
      );", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<TrialFollowupDueItem>> GetDueAsync()
    {
        AcademicSchema.Ensure();
        var result = new List<TrialFollowupDueItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    f.id_followup,
    f.id_trial,
    f.id_lead,
    f.due_at,
    f.child_full_name,
    COALESCE(f.client_full_name, '') AS client_full_name,
    COALESCE(f.parent_phone, '') AS parent_phone,
    f.child_phone,
    tl.scheduled_at,
    COALESCE(d.direction_name, '') AS direction_name,
    f.is_completed,
    COALESCE(f.task_status, 'позвонить_после_пробного') AS task_status,
    f.on_control,
    f.next_reminder_at
FROM public.trial_lesson_followup f
JOIN public.trial_lesson tl ON tl.id_trial = f.id_trial
JOIN public.lead_request lr ON lr.id_lead = tl.id_lead
LEFT JOIN public.direction d ON d.id_direction = lr.id_direction
WHERE NOT f.is_completed
  AND (
        f.due_at <= CURRENT_TIMESTAMP + INTERVAL '1 day'
        OR (f.on_control AND f.next_reminder_at IS NOT NULL AND f.next_reminder_at <= CURRENT_TIMESTAMP + INTERVAL '1 day')
      )
ORDER BY COALESCE(f.next_reminder_at, f.due_at), f.child_full_name;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TrialFollowupDueItem
            {
                IdFollowup = reader.GetInt32(0),
                IdTrial = reader.GetInt32(1),
                IdLead = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DueAt = reader.GetDateTime(3),
                ChildFullName = reader.GetString(4),
                ClientFullName = reader.GetString(5),
                ParentPhone = reader.GetString(6),
                ChildPhone = reader.IsDBNull(7) ? null : reader.GetString(7),
                ScheduledAt = reader.GetDateTime(8),
                DirectionName = reader.GetString(9),
                IsCompleted = reader.GetBoolean(10),
                TaskStatus = reader.GetString(11),
                OnControl = reader.GetBoolean(12),
                NextReminderAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
            });
        }

        return result;
    }

    public async Task CompleteAsync(TrialFollowupCompleteInput input)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (input.RescheduleNextDay)
        {
            await using var reschedule = new NpgsqlCommand(@"
UPDATE public.trial_lesson_followup
SET task_status = 'не_дозвонились',
    contact_note = @text,
    due_at = CURRENT_TIMESTAMP + INTERVAL '1 day',
    next_reminder_at = CURRENT_TIMESTAMP + INTERVAL '1 day',
    on_control = FALSE
WHERE id_followup = @id;", connection);
            reschedule.Parameters.Add("id", NpgsqlDbType.Integer).Value = input.IdFollowup;
            reschedule.Parameters.Add("text", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(input.FeedbackText) ? (object)DBNull.Value : input.FeedbackText.Trim();
            await reschedule.ExecuteNonQueryAsync();
            return;
        }

        if (input.OnControl && !input.MarkCompleted)
        {
            await using var control = new NpgsqlCommand(@"
UPDATE public.trial_lesson_followup
SET on_control = TRUE,
    task_status = 'на_контроле',
    contact_note = @text,
    next_reminder_at = CURRENT_TIMESTAMP + INTERVAL '7 days',
    due_at = CURRENT_TIMESTAMP + INTERVAL '7 days'
WHERE id_followup = @id;", connection);
            control.Parameters.Add("id", NpgsqlDbType.Integer).Value = input.IdFollowup;
            control.Parameters.Add("text", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(input.FeedbackText) ? (object)DBNull.Value : input.FeedbackText.Trim();
            await control.ExecuteNonQueryAsync();
            return;
        }

        await using var command = new NpgsqlCommand(@"
UPDATE public.trial_lesson_followup
SET is_completed = TRUE,
    feedback_code = @code,
    feedback_text = @text,
    contact_note = @text,
    completed_at = CURRENT_TIMESTAMP,
    id_completed_by = @user,
    task_status = @status,
    on_control = @control
WHERE id_followup = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = input.IdFollowup;
        command.Parameters.Add("code", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(input.FeedbackCode) ? (object)DBNull.Value : input.FeedbackCode.Trim();
        command.Parameters.Add("text", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(input.FeedbackText) ? (object)DBNull.Value : input.FeedbackText.Trim();
        command.Parameters.Add("user", NpgsqlDbType.Integer).Value =
            input.IdCompletedBy.HasValue ? input.IdCompletedBy.Value : DBNull.Value;
        command.Parameters.Add("status", NpgsqlDbType.Varchar).Value =
            input.OnControl ? "на_контроле" : "закрыто";
        command.Parameters.Add("control", NpgsqlDbType.Boolean).Value = input.OnControl;
        await command.ExecuteNonQueryAsync();
    }
}
