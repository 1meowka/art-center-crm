#nullable enable
using System;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace VKR.Services;

public sealed class NotificationSnoozeRepository
{
    public async Task SnoozeAsync(string taskKey, DateTime snoozedUntil, int? idUser)
    {
        if (snoozedUntil.Date < DateTime.Today)
            throw new InvalidOperationException("Дата отложения не может быть в прошлом.");

        NotificationSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO public.notification_snooze (task_key, snoozed_until, id_snoozed_by)
VALUES (@key, @until, @user)
ON CONFLICT (task_key)
DO UPDATE SET
    snoozed_until   = EXCLUDED.snoozed_until,
    id_snoozed_by   = EXCLUDED.id_snoozed_by,
    snoozed_at      = CURRENT_TIMESTAMP;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add("key", NpgsqlDbType.Varchar).Value = taskKey;
        cmd.Parameters.Add("until", NpgsqlDbType.Date).Value = snoozedUntil.Date;
        cmd.Parameters.Add("user", NpgsqlDbType.Integer).Value =
            idUser.HasValue ? idUser.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }
}
