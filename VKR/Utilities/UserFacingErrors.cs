#nullable enable
using System;
using System.Diagnostics;

namespace VKR.Utilities;

public static class UserFacingErrors
{
    public static string GetMessage(Exception ex, string fallback = "Не удалось выполнить операцию.")
    {
        Debug.WriteLine(ex);

        var pg = FindPostgresException(ex);
        if (pg != null)
            return MapPostgresException(pg, fallback);

        if (ex is InvalidOperationException or ArgumentException)
            return Sanitize(string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message);

        if (ex is System.Security.Cryptography.CryptographicException)
            return "Не удалось прочитать сохранённый токен бота. Введите токен заново в настройках MAX-бота.";

        return fallback;
    }

    private static Npgsql.NpgsqlException? FindPostgresException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is Npgsql.NpgsqlException pg)
                return pg;
        }
        return null;
    }

    private static string MapPostgresException(Npgsql.NpgsqlException pg, string fallback)
    {
        return pg.SqlState switch
        {
            "08006" or "08001" or "08003" or "57P03" => DatabaseConnection,
            "42P01" => "В базе данных не хватает таблиц для этой функции. Перезапустите приложение или выполните миграции.",
            "42703" => MapUndefinedColumn(pg),
            "23503" => "Запись связана с другими данными или выбрано неверное значение в списке.",
            "23505" => "Такая запись уже существует.",
            "23502" => MapNotNullViolation(pg),
            _ => string.IsNullOrWhiteSpace(pg.Message)
                ? fallback
                : Sanitize(pg.Message, allowDatabaseHints: true)
        };
    }

    private static string Sanitize(string message, bool allowDatabaseHints = false)
    {
        if (allowDatabaseHints)
            return message;

        if (message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || message.Contains("connection string", StringComparison.OrdinalIgnoreCase))
        {
            return "Операция не выполнена. Проверьте введённые данные или обратитесь к администратору.";
        }

        return message;
    }

    private static string MapUndefinedColumn(Npgsql.NpgsqlException pg)
    {
        var column = pg is Npgsql.PostgresException postgres ? postgres.ColumnName : null;
        if (string.IsNullOrWhiteSpace(column) && !string.IsNullOrWhiteSpace(pg.Message))
        {
            var start = pg.Message.IndexOf('«');
            var end = pg.Message.IndexOf('»');
            if (start >= 0 && end > start)
                column = pg.Message.Substring(start + 1, end - start - 1);
            else
            {
                var q1 = pg.Message.IndexOf('"');
                var q2 = q1 >= 0 ? pg.Message.IndexOf('"', q1 + 1) : -1;
                if (q1 >= 0 && q2 > q1)
                    column = pg.Message.Substring(q1 + 1, q2 - q1 - 1);
            }
        }

        return column switch
        {
            "contract_note" => "Не удалось сохранить примечание по договору. Перезапустите программу.",
            "workplace" => "Не удалось сохранить место работы. Перезапустите программу.",
            "is_adult" => "Не удалось сохранить тип гостя. Перезапустите программу.",
            "child_phone" => "Не удалось сохранить телефон. Перезапустите программу.",
            "child_residence_address" => "Не удалось сохранить адрес. Перезапустите программу.",
            _ => "В базе данных не хватает колонок. Перезапустите приложение — недостающие поля создадутся сами."
        };
    }

    private static string MapNotNullViolation(Npgsql.NpgsqlException pg)
    {
        var column = pg is Npgsql.PostgresException postgres ? postgres.ColumnName : null;
        return column switch
        {
            "contact_phone" or "child_phone" or "phone" => "Укажите телефон.",
            "child_first_name" or "first_name" => "Укажите имя.",
            "child_last_name" or "last_name" => "Укажите фамилию.",
            "status" => "Укажите статус.",
            _ => "Заполните все обязательные поля."
        };
    }

    public static string DatabaseConnection =>
        "Не удалось подключиться к базе данных. Проверьте настройки подключения.";

    public static string LoadFailed =>
        "Не удалось загрузить данные.";

    public static string SaveFailed =>
        "Не удалось сохранить данные.";
}
