#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;

namespace VKR.Services;

public sealed class ClientNoteRepository
{
    public async Task<IReadOnlyList<ClientNote>> GetNotesAsync(string entityType, int entityId)
    {
        FinanceSchema.Ensure();
        var result = new List<ClientNote>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
SELECT n.id_note, n.entity_type, n.entity_id, n.note_text, n.id_created_by,
       COALESCE(TRIM(u.last_name || ' ' || u.first_name), 'Администратор') AS author_name,
       n.created_at
FROM public.client_note n
LEFT JOIN public.app_user u ON u.id_user = n.id_created_by
WHERE n.entity_type = @type AND n.entity_id = @id
ORDER BY n.created_at DESC;", connection);
        cmd.Parameters.Add("type", NpgsqlDbType.Varchar).Value = entityType;
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = entityId;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ClientNote
            {
                IdNote = reader.GetInt32(0),
                EntityType = reader.GetString(1),
                EntityId = reader.GetInt32(2),
                NoteText = reader.GetString(3),
                IdCreatedBy = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                AuthorName = reader.IsDBNull(5) ? "Администратор" : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }

        return result;
    }

    public async Task<int> AddNoteAsync(string entityType, int entityId, string noteText, int? idCreatedBy)
    {
        if (string.IsNullOrWhiteSpace(noteText))
            noteText = "—";

        FinanceSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.client_note (entity_type, entity_id, note_text, id_created_by)
VALUES (@type, @id, @text, @author)
RETURNING id_note;", connection);
        cmd.Parameters.Add("type", NpgsqlDbType.Varchar).Value = entityType;
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = entityId;
        cmd.Parameters.Add("text", NpgsqlDbType.Text).Value = noteText.Trim();
        cmd.Parameters.Add("author", NpgsqlDbType.Integer).Value =
            idCreatedBy.HasValue ? idCreatedBy.Value : DBNull.Value;

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
