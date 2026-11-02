#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Npgsql;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public static class DatabaseSchema
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

            using (var command = new NpgsqlCommand(@"
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS id_teacher INTEGER NULL;", connection))
            {
                command.ExecuteNonQuery();
            }

            try
            {
                using var command = new NpgsqlCommand(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conname = 'app_user_id_teacher_fkey'
    ) THEN
        ALTER TABLE app_user
            ADD CONSTRAINT app_user_id_teacher_fkey
            FOREIGN KEY (id_teacher) REFERENCES public.teacher(id_teacher);
    END IF;
END $$;", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                // Не блокируем запуск приложения, если FK по какой-то причине не создался.
                Debug.WriteLine(ex);
            }

            using (var command = new NpgsqlCommand(@"
ALTER TABLE public.classroom
    ADD COLUMN IF NOT EXISTS id_branch SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE public.study_group
    ADD COLUMN IF NOT EXISTS id_branch SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE public.contract
    ADD COLUMN IF NOT EXISTS id_branch SMALLINT NOT NULL DEFAULT 1;", connection))
            {
                command.ExecuteNonQuery();
            }

            try
            {
                using var command = new NpgsqlCommand(@"
UPDATE public.study_group g
SET id_branch = sub.id_branch
FROM (
    SELECT DISTINCT ON (sc.id_group)
           sc.id_group,
           COALESCE(cr.id_branch, 1) AS id_branch
    FROM public.schedule sc
    JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
    ORDER BY sc.id_group, sc.id_schedule
) sub
WHERE g.id_group = sub.id_group;", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                Debug.WriteLine(ex);
            }

            try
            {
                using var command = new NpgsqlCommand(@"
UPDATE public.contract c
SET id_branch = g.id_branch
FROM public.study_group g
WHERE c.id_group = g.id_group;", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                Debug.WriteLine(ex);
            }

            using (var command = new NpgsqlCommand(@"
UPDATE app_user u
SET login = LOWER(TRIM(login))
WHERE login <> LOWER(TRIM(login))
  AND NOT EXISTS (
      SELECT 1
        FROM app_user other
       WHERE other.id_user <> u.id_user
         AND LOWER(TRIM(other.login)) = LOWER(TRIM(u.login))
  );", connection))
            {
                command.ExecuteNonQuery();
            }

            EnsureDirectorAccount(connection);
            EnsureReferenceCatalogs(connection);

            try
            {
                using var command = new NpgsqlCommand(@"
ALTER TABLE public.contract
    ALTER COLUMN id_direction DROP NOT NULL;
ALTER TABLE public.study_group
    ALTER COLUMN id_direction DROP NOT NULL;
ALTER TABLE public.study_group
    ALTER COLUMN id_teacher DROP NOT NULL;", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                Debug.WriteLine(ex);
            }

            try
            {
                using var command = new NpgsqlCommand(@"
UPDATE public.contract
SET contract_number = id_contract::text
WHERE contract_number IS DISTINCT FROM id_contract::text;", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex)
            {
                Debug.WriteLine(ex);
            }

            _ensured = true;
        }
    }

    private static void EnsureDirectorAccount(NpgsqlConnection connection)
    {
        using (var command = new NpgsqlCommand(@"
INSERT INTO public.role (role_code, role_name, description)
SELECT 'director', 'Руководитель', 'Директор центра, управление учётными записями'
WHERE NOT EXISTS (
    SELECT 1
      FROM public.role
     WHERE LOWER(TRIM(role_code)) IN ('director', 'директор', 'руководитель')
);", connection))
        {
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(@"
INSERT INTO public.role (role_code, role_name, description)
SELECT 'senior_manager', 'Старший менеджер', 'Сводка продаж на главной и полный набор Excel-отчётов'
WHERE NOT EXISTS (
    SELECT 1
      FROM public.role
     WHERE LOWER(TRIM(role_code)) IN (
         'senior_manager', 'senior-manager', 'head_manager',
         'старший менеджер', 'старший_менеджер', 'старшийменеджер')
);", connection))
        {
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(@"
UPDATE public.app_user u
SET id_role = r.id_role,
    is_active = TRUE
FROM public.role r
WHERE LOWER(TRIM(u.login)) = 'admin'
  AND LOWER(TRIM(r.role_code)) IN ('director', 'директор', 'руководитель');", connection))
        {
            command.ExecuteNonQuery();
        }

        var bootstrapHash = PasswordHasher.Hash("Studio123");
        using (var command = new NpgsqlCommand(@"
INSERT INTO public.app_user (login, password_hash, last_name, first_name, id_role, is_active)
SELECT 'admin', @password_hash, 'Руководитель', 'Центра', r.id_role, TRUE
FROM public.role r
WHERE LOWER(TRIM(r.role_code)) IN ('director', 'директор', 'руководитель')
  AND NOT EXISTS (
      SELECT 1 FROM public.app_user WHERE LOWER(TRIM(login)) = 'admin'
  )
ORDER BY r.id_role
LIMIT 1;", connection))
        {
            command.Parameters.AddWithValue("password_hash", bootstrapHash);
            command.ExecuteNonQuery();
        }
    }

    private static void EnsureReferenceCatalogs(NpgsqlConnection connection)
    {
        using (var command = new NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS public.study_branch (
    id_branch SMALLINT PRIMARY KEY,
    short_name VARCHAR(80) NOT NULL,
    address_line VARCHAR(200) NOT NULL,
    button_label VARCHAR(80) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO public.study_branch (id_branch, short_name, address_line, button_label, is_active)
VALUES
    (1, 'Центральная 1', 'ул. Центральная, 1', 'Центральная, 1', TRUE),
    (2, 'Парковая 10', 'ул. Парковая, 10', 'Парковая, 10', TRUE),
    (3, 'Школьная 5', 'ул. Школьная, 5', 'Школьная, 5', TRUE),
    (4, 'Садовая 3', 'ул. Садовая, 3', 'Садовая, 3', TRUE)
ON CONFLICT (id_branch) DO NOTHING;



CREATE TABLE IF NOT EXISTS public.academic_year (
    id_academic_year SERIAL PRIMARY KEY,
    start_year SMALLINT NOT NULL UNIQUE,
    year_label VARCHAR(16) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS public.street (
    id_street SERIAL PRIMARY KEY,
    street_name VARCHAR(200) NOT NULL UNIQUE
);

ALTER TABLE public.educational_institution
    ADD COLUMN IF NOT EXISTS institution_number VARCHAR(40) NULL;

ALTER TABLE public.educational_institution
    DROP CONSTRAINT IF EXISTS educational_institution_institution_type_check;
ALTER TABLE public.educational_institution
    ADD CONSTRAINT educational_institution_institution_type_check
    CHECK (institution_type IN (
        'школа', 'гимназия', 'лицей', 'детский сад',
        'детсад', 'вуз', 'колледж', 'другое'
    ));

ALTER TABLE public.representative
    ADD COLUMN IF NOT EXISTS age INTEGER NULL;
ALTER TABLE public.representative
    ADD COLUMN IF NOT EXISTS workplace VARCHAR(200) NULL;
ALTER TABLE public.representative
    ADD COLUMN IF NOT EXISTS comment TEXT NULL;", connection))
        {
            command.ExecuteNonQuery();
        }

        using (var seedYears = new NpgsqlCommand(@"
INSERT INTO public.academic_year (start_year, year_label, is_active)
SELECT y, y::text || '/' || (y + 1)::text, (y = EXTRACT(YEAR FROM CURRENT_DATE)::int
    OR (EXTRACT(MONTH FROM CURRENT_DATE) < 8 AND y = EXTRACT(YEAR FROM CURRENT_DATE)::int - 1)
    OR (EXTRACT(MONTH FROM CURRENT_DATE) >= 8 AND y = EXTRACT(YEAR FROM CURRENT_DATE)::int))
FROM generate_series(
    EXTRACT(YEAR FROM CURRENT_DATE)::int - 4,
    EXTRACT(YEAR FROM CURRENT_DATE)::int + 1
) AS y
ON CONFLICT (start_year) DO NOTHING;", connection))
        {
            seedYears.ExecuteNonQuery();
        }

        StudyBranch.ReplaceCatalog(LoadStudyBranches(connection));

        try
        {
            using var command = new NpgsqlCommand(@"
CREATE UNIQUE INDEX IF NOT EXISTS educational_institution_name_lower_uidx
    ON public.educational_institution ((LOWER(TRIM(institution_name))));", connection);
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex)
        {
            Debug.WriteLine(ex);
        }

        using (var command = new NpgsqlCommand(@"
INSERT INTO public.relation_type (relation_name)
SELECT v.relation_name
FROM (VALUES ('Мама'), ('Папа'), ('Брат'), ('Сестра'), ('Ребёнок'), ('Другой')) AS v(relation_name)
WHERE NOT EXISTS (
    SELECT 1
      FROM public.relation_type rt
     WHERE LOWER(TRIM(rt.relation_name)) = LOWER(v.relation_name)
);", connection))
        {
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(@"
UPDATE public.contract
SET contract_number = id_contract::text
WHERE contract_number IS DISTINCT FROM id_contract::text;", connection))
        {
            command.ExecuteNonQuery();
        }
    }

    private static List<StudyBranch> LoadStudyBranches(NpgsqlConnection connection)
    {
        var list = new List<StudyBranch>();
        using var command = new NpgsqlCommand(@"
SELECT id_branch, short_name, address_line, button_label, is_active
FROM public.study_branch
ORDER BY id_branch;", connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StudyBranch
            {
                IdBranch = reader.GetInt16(0),
                ShortName = reader.GetString(1),
                AddressLine = reader.GetString(2),
                ButtonLabel = reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        }

        return list;
    }

    public static void ReloadStudyBranches()
    {
        try
        {
            using var connection = Db.CreateConnection();
            connection.Open();
            StudyBranch.ReplaceCatalog(LoadStudyBranches(connection));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static void EnsureIfPossible()
    {
        try
        {
            Ensure();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
