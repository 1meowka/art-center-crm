#nullable enable
using System.Diagnostics;
using Npgsql;

namespace VKR.Services;

public static class MaxBotSchema
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
CREATE TABLE IF NOT EXISTS public.max_bot_settings (
    id_settings SMALLINT PRIMARY KEY DEFAULT 1 CHECK (id_settings = 1),
    bot_token TEXT NOT NULL DEFAULT '',
    bot_name VARCHAR(200) NULL,
    bot_username VARCHAR(200) NULL,
    org_name VARCHAR(300) NULL,
    org_address TEXT NULL,
    org_inn VARCHAR(20) NULL,
    support_phone VARCHAR(50) NULL,
    support_email VARCHAR(200) NULL,
    privacy_url TEXT NULL,
    terms_url TEXT NULL,
    welcome_text TEXT NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by INTEGER NULL
);

CREATE TABLE IF NOT EXISTS public.max_bot_template_folder (
    id_folder SERIAL PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.max_bot_template (
    id_template SERIAL PRIMARY KEY,
    id_folder INTEGER NOT NULL REFERENCES public.max_bot_template_folder(id_folder) ON DELETE CASCADE,
    title VARCHAR(200) NOT NULL,
    body TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.max_bot_subscriber (
    id_subscriber SERIAL PRIMARY KEY,
    max_chat_id BIGINT NOT NULL UNIQUE,
    max_user_id BIGINT NULL,
    display_name VARCHAR(200) NOT NULL DEFAULT '',
    id_representative INTEGER NULL,
    id_student INTEGER NULL,
    subscribed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_message_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS public.max_bot_message (
    id_message SERIAL PRIMARY KEY,
    max_chat_id BIGINT NOT NULL,
    id_subscriber INTEGER NULL REFERENCES public.max_bot_subscriber(id_subscriber) ON DELETE SET NULL,
    direction VARCHAR(8) NOT NULL CHECK (direction IN ('in', 'out')),
    body TEXT NOT NULL,
    sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    max_update_id BIGINT NULL
);

CREATE INDEX IF NOT EXISTS idx_max_bot_message_chat ON public.max_bot_message(max_chat_id, sent_at);
", connection))
            {
                command.ExecuteNonQuery();
            }

            SeedDefaults(connection);

            using (var markerCmd = new NpgsqlCommand(@"
ALTER TABLE public.max_bot_settings
    ADD COLUMN IF NOT EXISTS updates_marker BIGINT NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS org_name VARCHAR(300) NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS org_address TEXT NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS org_inn VARCHAR(20) NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS support_phone VARCHAR(50) NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS support_email VARCHAR(200) NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS privacy_url TEXT NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS terms_url TEXT NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS welcome_text TEXT NULL;
ALTER TABLE public.max_bot_settings ADD COLUMN IF NOT EXISTS discounts_text TEXT NULL;

CREATE TABLE IF NOT EXISTS public.max_bot_branch (
    id_branch SMALLINT PRIMARY KEY,
    short_name VARCHAR(120) NOT NULL,
    address_line TEXT NOT NULL,
    support_phone VARCHAR(50) NULL,
    support_email VARCHAR(200) NULL,
    contacts_note TEXT NULL,
    updated_at TIMESTAMPTZ NULL
);", connection))
            {
                markerCmd.ExecuteNonQuery();
            }

            SeedBranches(connection);

            _ensured = true;
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

    private static void SeedDefaults(NpgsqlConnection connection)
    {
        using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM public.max_bot_template_folder;", connection);
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        if (count > 0)
            return;

        using var tx = connection.BeginTransaction();

        using (var folderCmd = new NpgsqlCommand(@"
INSERT INTO public.max_bot_template_folder (name, sort_order) VALUES
    ('Поздравления', 1),
    ('Напоминания', 2),
    ('Информация', 3)
RETURNING id_folder, name;", connection, tx))
        {
            using var reader = folderCmd.ExecuteReader();
            var folderIds = new System.Collections.Generic.List<int>();
            while (reader.Read())
                folderIds.Add(reader.GetInt32(0));
            reader.Close();

            if (folderIds.Count >= 3)
            {
                InsertTemplate(connection, tx, folderIds[0], "С днём рождения!",
                    "Здравствуйте! 🎂\n\nТворческий центр «Арт-студия» поздравляет вас с днём рождения!\n\nЖелаем вдохновения, радости и ярких творческих открытий!");
                InsertTemplate(connection, tx, folderIds[0], "Новый год",
                    "С Новым годом! 🎄\n\nКоманда «Арт-студии» желает вам волшебства, творчества и счастья в новом году!");
                InsertTemplate(connection, tx, folderIds[0], "8 Марта",
                    "С 8 Марта! 🌷\n\nЖелаем вдохновения, красоты и радости каждый день!");

                InsertTemplate(connection, tx, folderIds[1], "Напоминание об оплате",
                    "Здравствуйте!\n\nНапоминаем об оплате занятий в центре «Арт-студия».\n\nПо вопросам: +7 (___) ___-__-__");
                InsertTemplate(connection, tx, folderIds[1], "Напоминание о занятии",
                    "Завтра состоится занятие в центре «Арт-студия». Ждём вас!\n\nАдрес: г. Примерск, ул. Центральная, д. 1");

                InsertTemplate(connection, tx, folderIds[2], "Приветствие",
                    "Здравствуйте!\n\nВы написали боту творческого центра «Арт-студия».\n\nКоманды: /help — список возможностей, /contacts — адрес и телефон.\nНапишите «Запись» — чтобы записаться на пробное занятие.");
                InsertTemplate(connection, tx, folderIds[2], "Контакты центра",
                    "📍 г. Примерск, ул. Центральная, д. 1\n\n📞 Телефон: +7 (___) ___-__-__\n\n🌐 Сайт: (скоро)\n\nЖдём вас в «Арт-студии»!");
            }
        }

        tx.Commit();
    }

    private static void SeedBranches(NpgsqlConnection connection)
    {
        using var cmd = new NpgsqlCommand(@"
INSERT INTO public.max_bot_branch (id_branch, short_name, address_line, support_phone, support_email, contacts_note, updated_at)
VALUES
    (1, 'Центральная', 'ул. Центральная, 1', NULL, NULL,
     'Шаблон контактов филиала на Центральной. Укажите телефон и e-mail в таблице max_bot_branch.',
     NOW()),
    (2, 'Парковая', 'ул. Парковая, 10', NULL, NULL,
     'Шаблон контактов филиала на Парковой. Укажите телефон и e-mail в таблице max_bot_branch.',
     NOW()),
    (3, 'Школьная 5', 'ул. Школьная, 5', NULL, NULL,
     'Шаблон контактов филиала на Школьной.',
     NOW()),
    (4, 'Садовая 3', 'ул. Садовая, 3', NULL, NULL,
     'Шаблон контактов филиала на Садовой.',
     NOW())
ON CONFLICT (id_branch) DO NOTHING;

", connection);
        cmd.ExecuteNonQuery();
    }

    private static void InsertTemplate(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        int idFolder,
        string title,
        string body)
    {
        using var cmd = new NpgsqlCommand(@"
INSERT INTO public.max_bot_template (id_folder, title, body)
VALUES (@id_folder, @title, @body);", connection, tx);
        cmd.Parameters.AddWithValue("id_folder", idFolder);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("body", body);
        cmd.ExecuteNonQuery();
    }
}
