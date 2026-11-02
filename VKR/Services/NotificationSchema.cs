#nullable enable
using System;
using System.Diagnostics;
using Npgsql;

namespace VKR.Services;

/// <summary>Создаёт таблицы уведомлений при первом запуске (если миграции ещё не применяли).</summary>
public static class NotificationSchema
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

            // Каждый оператор отдельно: один сбой (индекс, дубликаты) не откатывает ADD COLUMN.
            foreach (var sql in SplitStatements(SchemaSql))
            {
                try
                {
                    using var command = new NpgsqlCommand(sql, connection);
                    command.ExecuteNonQuery();
                }
                catch (PostgresException ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            try
            {
                LeadFamilyMigrator.Backfill(connection);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            _ensured = true;
        }
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        foreach (var part in sql.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var statement = part.Trim();
            if (statement.Length > 0)
                yield return statement;
        }
    }

    private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS public.satisfaction_level (
    id_satisfaction_level SERIAL PRIMARY KEY,
    level_name            VARCHAR(100) NOT NULL,
    level_code            VARCHAR(32)  NOT NULL UNIQUE,
    is_positive           BOOLEAN      NOT NULL DEFAULT FALSE,
    sort_order            SMALLINT     NOT NULL DEFAULT 0,
    is_active             BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS public.dissatisfaction_reason (
    id_reason    SERIAL PRIMARY KEY,
    reason_name  VARCHAR(200) NOT NULL,
    sort_order   SMALLINT     NOT NULL DEFAULT 0,
    is_active    BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS public.lead_request_direction (
    id_lead      INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    id_direction INTEGER NOT NULL REFERENCES public.direction(id_direction),
    PRIMARY KEY (id_lead, id_direction)
);

ALTER TABLE public.student
    ADD COLUMN IF NOT EXISTS id_source_lead INTEGER REFERENCES public.lead_request(id_lead);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS id_representative INTEGER REFERENCES public.representative(id_representative);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS child_phone VARCHAR(32);

CREATE INDEX IF NOT EXISTS idx_lead_request_representative
    ON public.lead_request (id_representative)
    WHERE id_representative IS NOT NULL;

ALTER TABLE public.trial_lesson
    ADD COLUMN IF NOT EXISTS id_schedule INTEGER REFERENCES public.schedule(id_schedule);

CREATE INDEX IF NOT EXISTS idx_trial_lesson_schedule_at
    ON public.trial_lesson (id_schedule, scheduled_at)
    WHERE id_schedule IS NOT NULL;

CREATE TABLE IF NOT EXISTS public.student_satisfaction_survey (
    id_survey                 SERIAL PRIMARY KEY,
    id_student                INTEGER NOT NULL REFERENCES public.student(id_student),
    id_contract               INTEGER NOT NULL REFERENCES public.contract(id_contract),
    id_direction              INTEGER NOT NULL REFERENCES public.direction(id_direction),
    due_date                  DATE    NOT NULL,
    period_number             INTEGER NOT NULL CHECK (period_number > 0),
    id_satisfaction_level     INTEGER REFERENCES public.satisfaction_level(id_satisfaction_level),
    id_dissatisfaction_reason INTEGER REFERENCES public.dissatisfaction_reason(id_reason),
    suggested_direction_id    INTEGER REFERENCES public.direction(id_direction),
    comment                   TEXT,
    completed_at              TIMESTAMPTZ,
    id_completed_by           INTEGER REFERENCES public.app_user(id_user),
    UNIQUE (id_contract, period_number)
);

CREATE TABLE IF NOT EXISTS public.notification_snooze (
    id_snooze       SERIAL PRIMARY KEY,
    task_key        VARCHAR(80) NOT NULL UNIQUE,
    snoozed_until   DATE        NOT NULL,
    id_snoozed_by   INTEGER REFERENCES public.app_user(id_user),
    snoozed_at      TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.contract_renewal_reminder (
    id_reminder         SERIAL PRIMARY KEY,
    id_contract         INTEGER NOT NULL REFERENCES public.contract(id_contract) ON DELETE CASCADE,
    id_student          INTEGER NOT NULL REFERENCES public.student(id_student),
    contract_end_date   DATE    NOT NULL,
    will_renew          BOOLEAN,
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    comment             TEXT,
    completed_at        TIMESTAMPTZ,
    id_completed_by     INTEGER REFERENCES public.app_user(id_user),
    UNIQUE (id_contract, contract_end_date)
);

INSERT INTO public.satisfaction_level (level_name, level_code, is_positive, sort_order)
SELECT v.level_name, v.level_code, v.is_positive, v.sort_order
FROM (VALUES
    ('Нравится',           'like',      TRUE,  1),
    ('Скорее нравится',    'mostly_ok', TRUE,  2),
    ('Нейтрально',         'neutral',   FALSE, 3),
    ('Скорее не нравится', 'mostly_no', FALSE, 4),
    ('Не нравится',        'dislike',   FALSE, 5)
) AS v(level_name, level_code, is_positive, sort_order)
WHERE NOT EXISTS (SELECT 1 FROM public.satisfaction_level LIMIT 1);

INSERT INTO public.dissatisfaction_reason (reason_name, sort_order)
SELECT v.reason_name, v.sort_order
FROM (VALUES
    ('Не подходит расписание',       1),
    ('Не нравится преподаватель',    2),
    ('Сложно / не справляется',      3),
    ('Не интересно направление',     4),
    ('Финансовые причины',           5),
    ('Другое',                       99)
) AS v(reason_name, sort_order)
WHERE NOT EXISTS (SELECT 1 FROM public.dissatisfaction_reason LIMIT 1);

INSERT INTO public.lead_request_direction (id_lead, id_direction)
SELECT lr.id_lead, lr.id_direction
FROM public.lead_request lr
WHERE lr.id_direction IS NOT NULL
ON CONFLICT DO NOTHING;

CREATE INDEX IF NOT EXISTS idx_notification_snooze_until
    ON public.notification_snooze (snoozed_until);

CREATE INDEX IF NOT EXISTS idx_student_satisfaction_survey_due
    ON public.student_satisfaction_survey (due_date)
    WHERE completed_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_contract_renewal_reminder_open
    ON public.contract_renewal_reminder (contract_end_date)
    WHERE NOT is_completed;

CREATE TABLE IF NOT EXISTS public.lead_representative (
    id_lead_rep        SERIAL PRIMARY KEY,
    id_lead            INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    id_representative  INTEGER NOT NULL REFERENCES public.representative(id_representative),
    id_relation_type   INTEGER REFERENCES public.relation_type(id_relation_type),
    sort_order         SMALLINT NOT NULL DEFAULT 0,
    UNIQUE (id_lead, id_representative)
);

CREATE INDEX IF NOT EXISTS idx_lead_representative_lead
    ON public.lead_representative (id_lead);

CREATE TABLE IF NOT EXISTS public.lead_request_child (
    id_lead_child     SERIAL PRIMARY KEY,
    id_lead           INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    full_name         VARCHAR(200) NOT NULL,
    birth_date        DATE,
    phone             VARCHAR(32),
    residence_address TEXT,
    sort_order        SMALLINT NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_lead_request_child_lead
    ON public.lead_request_child (id_lead, sort_order);

ALTER TABLE public.lead_request
    ALTER COLUMN id_direction DROP NOT NULL;

ALTER TABLE public.lead_request
    ALTER COLUMN contact_phone DROP NOT NULL;

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS id_institution INTEGER REFERENCES public.educational_institution(id_institution);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS workplace VARCHAR(200);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS child_residence_address TEXT;

ALTER TABLE public.representative
    ADD COLUMN IF NOT EXISTS workplace VARCHAR(200);

ALTER TABLE public.representative
    ADD COLUMN IF NOT EXISTS comment TEXT;

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS is_adult BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS public.lead_relation (
    id_lead_relation         SERIAL PRIMARY KEY,
    id_lead                  INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    id_related_lead          INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    id_relation_type        INTEGER REFERENCES public.relation_type(id_relation_type),
    is_main_payer            BOOLEAN NOT NULL DEFAULT FALSE,
    is_legal_representative  BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order               SMALLINT NOT NULL DEFAULT 0,
    CHECK (id_lead <> id_related_lead),
    UNIQUE (id_lead, id_related_lead)
);

CREATE INDEX IF NOT EXISTS idx_lead_relation_lead
    ON public.lead_relation (id_lead);

CREATE INDEX IF NOT EXISTS idx_lead_relation_related
    ON public.lead_relation (id_related_lead);

CREATE UNIQUE INDEX IF NOT EXISTS idx_lead_relation_one_main_payer
    ON public.lead_relation (id_lead)
    WHERE is_main_payer;

INSERT INTO public.relation_type (relation_name)
SELECT v.relation_name
FROM (VALUES ('Брат'), ('Сестра'), ('Ребёнок')) AS v(relation_name)
WHERE NOT EXISTS (
    SELECT 1
      FROM public.relation_type rt
     WHERE LOWER(TRIM(rt.relation_name)) = LOWER(v.relation_name)
);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS contract_note TEXT;

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS mother_full_name VARCHAR(200);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS mother_phone VARCHAR(32);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS father_full_name VARCHAR(200);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS father_phone VARCHAR(32);

ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS school_shift_comment TEXT;
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent1_relation VARCHAR(50);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent1_full_name VARCHAR(200);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent1_phone VARCHAR(32);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent1_age SMALLINT;
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent2_relation VARCHAR(50);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent2_full_name VARCHAR(200);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent2_phone VARCHAR(32);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent2_age SMALLINT;
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent3_relation VARCHAR(50);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent3_full_name VARCHAR(200);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent3_phone VARCHAR(32);
ALTER TABLE public.lead_request
    ADD COLUMN IF NOT EXISTS parent3_age SMALLINT;

UPDATE public.lead_request
SET parent1_relation = COALESCE(NULLIF(BTRIM(parent1_relation), ''), 'мама'),
    parent1_full_name = COALESCE(NULLIF(BTRIM(parent1_full_name), ''), mother_full_name),
    parent1_phone = COALESCE(NULLIF(BTRIM(parent1_phone), ''), mother_phone)
WHERE mother_full_name IS NOT NULL AND BTRIM(mother_full_name) <> ''
  AND (parent1_full_name IS NULL OR BTRIM(parent1_full_name) = '');

UPDATE public.lead_request
SET parent2_relation = COALESCE(NULLIF(BTRIM(parent2_relation), ''), 'папа'),
    parent2_full_name = COALESCE(NULLIF(BTRIM(parent2_full_name), ''), father_full_name),
    parent2_phone = COALESCE(NULLIF(BTRIM(parent2_phone), ''), father_phone)
WHERE father_full_name IS NOT NULL AND BTRIM(father_full_name) <> ''
  AND (parent2_full_name IS NULL OR BTRIM(parent2_full_name) = '');";
}
