#nullable enable
using Npgsql;

namespace VKR.Services;

/// <summary>Финансовые справочники и расторжения договоров (если миграции ещё не применяли).</summary>
public static class FinanceSchema
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
            using var command = new NpgsqlCommand(SchemaSql, connection);
            command.ExecuteNonQuery();
            _ensured = true;
        }
    }

    private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS public.leaving_reason (
    id_reason    SERIAL PRIMARY KEY,
    reason_name  VARCHAR(200) NOT NULL UNIQUE
);

INSERT INTO public.leaving_reason (reason_name)
SELECT v.reason_name
FROM (VALUES
    ('Переезд'),
    ('Финансовые причины'),
    ('Не подошло расписание'),
    ('Другое')
) AS v(reason_name)
WHERE NOT EXISTS (SELECT 1 FROM public.leaving_reason LIMIT 1);

CREATE TABLE IF NOT EXISTS public.contract_termination (
    id_termination    SERIAL PRIMARY KEY,
    id_contract       INTEGER NOT NULL REFERENCES public.contract(id_contract) ON DELETE CASCADE,
    id_reason         INTEGER REFERENCES public.leaving_reason(id_reason),
    termination_date  DATE NOT NULL,
    comment           TEXT,
    UNIQUE (id_contract)
);

CREATE INDEX IF NOT EXISTS idx_contract_termination_date
    ON public.contract_termination (termination_date);

CREATE TABLE IF NOT EXISTS public.course_package (
    id_course_package  SERIAL PRIMARY KEY,
    course_name        VARCHAR(200) NOT NULL,
    id_direction       INTEGER REFERENCES public.direction(id_direction) ON DELETE SET NULL,
    duration_months    SMALLINT NOT NULL DEFAULT 9,
    lessons_count      INTEGER,
    times_per_week     SMALLINT NOT NULL DEFAULT 1,
    price              NUMERIC(10,2),
    is_active          BOOLEAN NOT NULL DEFAULT true
);

CREATE INDEX IF NOT EXISTS idx_course_package_active
    ON public.course_package (is_active, duration_months);

INSERT INTO public.course_package (course_name, duration_months, lessons_count, times_per_week, price, is_active)
SELECT v.course_name, v.duration_months, v.lessons_count, v.times_per_week, v.price, true
FROM (VALUES
    ('Курс 9 месяцев', 9::smallint, 36, 1::smallint, NULL::numeric),
    ('Курс 6 месяцев', 6::smallint, 24, 1::smallint, NULL::numeric),
    ('Курс 3 месяца', 3::smallint, 12, 1::smallint, NULL::numeric)
) AS v(course_name, duration_months, lessons_count, times_per_week, price)
WHERE NOT EXISTS (SELECT 1 FROM public.course_package LIMIT 1);

ALTER TABLE public.course_package
    ALTER COLUMN duration_months TYPE NUMERIC(4,1)
    USING duration_months::numeric;

ALTER TABLE public.contract
    ADD COLUMN IF NOT EXISTS direction_name_snapshot VARCHAR(200);

UPDATE public.contract c
SET direction_name_snapshot = d.direction_name
FROM public.direction d
WHERE d.id_direction = c.id_direction
  AND (c.direction_name_snapshot IS NULL OR c.direction_name_snapshot = '');

ALTER TABLE public.debt
    ADD COLUMN IF NOT EXISTS due_date DATE;
ALTER TABLE public.debt
    ADD COLUMN IF NOT EXISTS defer_until DATE;
ALTER TABLE public.debt
    ADD COLUMN IF NOT EXISTS defer_comment TEXT;

UPDATE public.debt
SET due_date = (DATE_TRUNC('month', occurrence_date) + INTERVAL '1 month' + INTERVAL '19 days')::date
WHERE due_date IS NULL AND occurrence_date IS NOT NULL;

CREATE TABLE IF NOT EXISTS public.client_note (
    id_note        SERIAL PRIMARY KEY,
    entity_type    VARCHAR(20) NOT NULL,
    entity_id      INTEGER NOT NULL,
    note_text      TEXT NOT NULL,
    id_created_by  INTEGER REFERENCES public.app_user(id_user) ON DELETE SET NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_client_note_entity
    ON public.client_note (entity_type, entity_id, created_at DESC);

ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS id_student INTEGER REFERENCES public.student(id_student) ON DELETE SET NULL;
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS discount_percent NUMERIC(6,2);
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS amount_base NUMERIC(12,2);

UPDATE public.payment
SET id_student = c.id_student
FROM public.contract c
WHERE c.id_contract = payment.id_contract
  AND payment.id_student IS NULL;

ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS actual_date DATE;
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS payment_kind VARCHAR(80);
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS event_text VARCHAR(300);
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS period_label VARCHAR(80);
ALTER TABLE public.payment
    ADD COLUMN IF NOT EXISTS id_manager INTEGER REFERENCES public.app_user(id_user) ON DELETE SET NULL;

UPDATE public.payment
SET actual_date = payment_date
WHERE actual_date IS NULL AND payment_date IS NOT NULL;

UPDATE public.payment
SET id_manager = id_created_by
WHERE id_manager IS NULL AND id_created_by IS NOT NULL;

INSERT INTO public.payment_method (method_code, method_name)
SELECT v.code, v.name
FROM (VALUES
    ('cash', 'Наличные'),
    ('terminal', 'Терминал'),
    ('qr', 'QR-код'),
    ('transfer', 'Перевод')
) AS v(code, name)
WHERE NOT EXISTS (
    SELECT 1 FROM public.payment_method pm
    WHERE LOWER(BTRIM(pm.method_name)) = LOWER(v.name)
       OR LOWER(BTRIM(pm.method_code)) = LOWER(v.code)
);";
}
