#nullable enable
using Npgsql;

namespace VKR.Services;

/// <summary>Таблицы прогнозной аналитики (если миграции ещё не применяли).</summary>
public static class AiAnalyticsSchema
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
CREATE TABLE IF NOT EXISTS public.prognoz_ottoka (
    id_prognoza_ottoka     SERIAL PRIMARY KEY,
    id_student             INTEGER NOT NULL REFERENCES public.student(id_student) ON DELETE CASCADE,
    veroyatnost_ottoka     NUMERIC(8,6) NOT NULL DEFAULT 0,
    uroven_riska           VARCHAR(20)  NOT NULL DEFAULT 'низкий',
    faktor_prognoza        TEXT,
    prioritet_obrabotki    INTEGER      NOT NULL DEFAULT 5,
    rekomenduemoe_deystvie TEXT,
    data_rascheta          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_prognoz_ottoka_student
    ON public.prognoz_ottoka (id_student, data_rascheta DESC);

CREATE TABLE IF NOT EXISTS public.prognoz_konversii_zayavok (
    id_prognoza_konversii  SERIAL PRIMARY KEY,
    id_lead                INTEGER NOT NULL REFERENCES public.lead_request(id_lead) ON DELETE CASCADE,
    veroyatnost_konversii  NUMERIC(8,6) NOT NULL DEFAULT 0,
    uroven_veroyatnosti    VARCHAR(20)  NOT NULL DEFAULT 'низкий',
    faktor_prognoza        TEXT,
    rekomenduemoe_deystvie TEXT,
    data_rascheta          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_prognoz_konversii_lead
    ON public.prognoz_konversii_zayavok (id_lead, data_rascheta DESC);

CREATE TABLE IF NOT EXISTS public.ai_analytics_metadata (
    analytics_type       VARCHAR(30) PRIMARY KEY,
    last_calculated_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    records_processed    INTEGER NOT NULL DEFAULT 0,
    status               VARCHAR(20) NOT NULL DEFAULT 'ok'
);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ai_recommendation_status'
          AND column_name = 'recommendation_key'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'ai_recommendation_status'
          AND column_name = 'entity_type'
    ) THEN
        DROP TABLE public.ai_recommendation_status;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.ai_recommendation_status (
    entity_type VARCHAR(20) NOT NULL,
    entity_id INTEGER NOT NULL,
    recommendation_text TEXT NOT NULL,
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    completed_at TIMESTAMP NULL,
    rule_code VARCHAR(50) NULL,
    recommendation_type VARCHAR(20) NULL,
    entity_display_name TEXT NULL,
    triggered_signs TEXT NULL,
    completed_by_user_id INTEGER NULL,
    completed_by_name VARCHAR(255) NULL,
    PRIMARY KEY (entity_type, entity_id, recommendation_text)
);";
}
