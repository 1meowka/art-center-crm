#nullable enable
using Npgsql;

namespace VKR.Services;

/// <summary>Поднаправления, лимиты групп, история, мастер-классы, обратная связь после пробного.</summary>
public static class AcademicSchema
{
    private static bool _ensured;
    private static readonly object Gate = new();

    public static void Ensure()
    {
        if (_ensured)
            return;

        lock (Gate)
        {
            if (_ensured)
                return;

            using var connection = Db.CreateConnection();
            connection.Open();
            // Колонки направления коммитятся отдельно: сбой остальной схемы
            // не должен откатывать их и ломать «Добавить направление».
            EnsureDirectionColumns(connection);
            using var command = new NpgsqlCommand(SchemaSql, connection);
            command.ExecuteNonQuery();
            _ensured = true;
        }
    }

    /// <summary>Родитель и порядок сортировки у направления. Идемпотентно.</summary>
    public static void EnsureDirectionColumns()
    {
        using var connection = Db.CreateConnection();
        connection.Open();
        EnsureDirectionColumns(connection);
    }

    private static void EnsureDirectionColumns(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(DirectionColumnsSql, connection);
        command.ExecuteNonQuery();
    }

    private const string DirectionColumnsSql = @"
ALTER TABLE public.direction
    ADD COLUMN IF NOT EXISTS parent_id_direction INTEGER REFERENCES public.direction(id_direction) ON DELETE SET NULL;

ALTER TABLE public.direction
    ADD COLUMN IF NOT EXISTS sort_order SMALLINT NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_direction_parent
    ON public.direction (parent_id_direction)
    WHERE parent_id_direction IS NOT NULL;";

    private const string SchemaSql = @"
ALTER TABLE public.direction
    ADD COLUMN IF NOT EXISTS parent_id_direction INTEGER REFERENCES public.direction(id_direction) ON DELETE SET NULL;

ALTER TABLE public.direction
    ADD COLUMN IF NOT EXISTS sort_order SMALLINT NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_direction_parent
    ON public.direction (parent_id_direction)
    WHERE parent_id_direction IS NOT NULL;

ALTER TABLE public.study_group
    ADD COLUMN IF NOT EXISTS age_min SMALLINT;

ALTER TABLE public.study_group
    ADD COLUMN IF NOT EXISTS age_max SMALLINT;

ALTER TABLE public.study_group
    ADD COLUMN IF NOT EXISTS age_band_label VARCHAR(120);

ALTER TABLE public.study_group
    ADD COLUMN IF NOT EXISTS max_students SMALLINT;

ALTER TABLE public.schedule
    ADD COLUMN IF NOT EXISTS lesson_type VARCHAR(32) NOT NULL DEFAULT 'regular';

CREATE TABLE IF NOT EXISTS public.calendar_event (
    id_event           SERIAL PRIMARY KEY,
    event_type         VARCHAR(32)  NOT NULL DEFAULT 'master_class',
    title              VARCHAR(200) NOT NULL,
    id_direction       INTEGER REFERENCES public.direction(id_direction),
    id_teacher         INTEGER REFERENCES public.teacher(id_teacher),
    id_classroom       INTEGER REFERENCES public.classroom(id_classroom),
    scheduled_at       TIMESTAMP    NOT NULL,
    time_end           TIME,
    max_participants   SMALLINT,
    description        TEXT,
    is_active          BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_calendar_event_scheduled
    ON public.calendar_event (scheduled_at)
    WHERE is_active = TRUE;

CREATE TABLE IF NOT EXISTS public.group_teacher_history (
    id_history      SERIAL PRIMARY KEY,
    id_group        INTEGER NOT NULL REFERENCES public.study_group(id_group) ON DELETE CASCADE,
    id_teacher_old  INTEGER REFERENCES public.teacher(id_teacher),
    id_teacher_new  INTEGER NOT NULL REFERENCES public.teacher(id_teacher),
    changed_at      TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    id_changed_by   INTEGER REFERENCES public.app_user(id_user),
    comment         TEXT
);

CREATE INDEX IF NOT EXISTS idx_group_teacher_history_group
    ON public.group_teacher_history (id_group, changed_at DESC);

CREATE TABLE IF NOT EXISTS public.student_group_history (
    id_history       SERIAL PRIMARY KEY,
    id_student       INTEGER NOT NULL REFERENCES public.student(id_student) ON DELETE CASCADE,
    id_group_from    INTEGER REFERENCES public.study_group(id_group),
    id_group_to      INTEGER NOT NULL REFERENCES public.study_group(id_group),
    id_teacher_from  INTEGER REFERENCES public.teacher(id_teacher),
    id_teacher_to    INTEGER REFERENCES public.teacher(id_teacher),
    transfer_date    DATE NOT NULL,
    changed_at       TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    id_changed_by    INTEGER REFERENCES public.app_user(id_user),
    reason           VARCHAR(200),
    comment          TEXT
);

CREATE INDEX IF NOT EXISTS idx_student_group_history_student
    ON public.student_group_history (id_student, changed_at DESC);

ALTER TABLE public.student_group_history
    ALTER COLUMN id_group_to DROP NOT NULL;

ALTER TABLE public.student_group_history
    ADD COLUMN IF NOT EXISTS event_type VARCHAR(32) NOT NULL DEFAULT 'transfer';

CREATE INDEX IF NOT EXISTS idx_student_group_history_group_from
    ON public.student_group_history (id_group_from, changed_at DESC);

CREATE INDEX IF NOT EXISTS idx_student_group_history_group_to
    ON public.student_group_history (id_group_to, changed_at DESC);

CREATE TABLE IF NOT EXISTS public.group_daily_report (
    id_report          SERIAL PRIMARY KEY,
    id_group           INTEGER NOT NULL REFERENCES public.study_group(id_group) ON DELETE CASCADE,
    report_date        DATE NOT NULL,
    students_present   SMALLINT NOT NULL DEFAULT 0,
    trial_present      SMALLINT NOT NULL DEFAULT 0,
    comment            TEXT,
    id_updated_by      INTEGER REFERENCES public.app_user(id_user),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (id_group, report_date)
);

CREATE INDEX IF NOT EXISTS idx_group_daily_report_date
    ON public.group_daily_report (report_date DESC);

CREATE TABLE IF NOT EXISTS public.trial_lesson_followup (
    id_followup        SERIAL PRIMARY KEY,
    id_trial           INTEGER NOT NULL UNIQUE REFERENCES public.trial_lesson(id_trial) ON DELETE CASCADE,
    due_at             TIMESTAMPTZ NOT NULL,
    parent_phone       VARCHAR(32),
    child_phone        VARCHAR(32),
    child_full_name    VARCHAR(200),
    is_completed       BOOLEAN NOT NULL DEFAULT FALSE,
    feedback_text      TEXT,
    feedback_code      VARCHAR(32),
    completed_at       TIMESTAMPTZ,
    id_completed_by    INTEGER REFERENCES public.app_user(id_user)
);

CREATE INDEX IF NOT EXISTS idx_trial_followup_due
    ON public.trial_lesson_followup (due_at)
    WHERE NOT is_completed;

ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS id_lead INTEGER REFERENCES public.lead_request(id_lead);
ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS client_full_name VARCHAR(200);
ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS task_status VARCHAR(40) NOT NULL DEFAULT 'позвонить_после_пробного';
ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS contact_note TEXT;
ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS on_control BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE public.trial_lesson_followup
    ADD COLUMN IF NOT EXISTS next_reminder_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_trial_followup_control
    ON public.trial_lesson_followup (next_reminder_at)
    WHERE NOT is_completed AND on_control;

ALTER TABLE public.trial_lesson
    ADD COLUMN IF NOT EXISTS id_event INTEGER REFERENCES public.calendar_event(id_event) ON DELETE SET NULL;

ALTER TABLE public.trial_lesson
    ADD COLUMN IF NOT EXISTS id_group INTEGER REFERENCES public.study_group(id_group) ON DELETE SET NULL;
ALTER TABLE public.trial_lesson
    ADD COLUMN IF NOT EXISTS id_teacher INTEGER REFERENCES public.teacher(id_teacher) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_trial_lesson_event
    ON public.trial_lesson (id_event)
    WHERE id_event IS NOT NULL;";
}
