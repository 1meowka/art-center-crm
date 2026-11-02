#nullable enable
using System;
using System.Collections.Generic;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

/// <summary>
/// Создаёт карточки гостей из старых вложенных детей/представителей
/// и связывает их через lead_relation.
/// </summary>
public static class LeadFamilyMigrator
{
    public static void Backfill(NpgsqlConnection connection)
    {
        var relationIds = LoadRelationIds(connection);
        relationIds.TryGetValue(RelationTypeCatalog.Other, out var otherId);
        relationIds.TryGetValue(RelationTypeCatalog.Brother, out var brotherId);

        var leadIds = new List<int>();
        using (var cmd = new NpgsqlCommand(@"
SELECT l.id_lead
FROM public.lead_request l
WHERE NOT EXISTS (
        SELECT 1 FROM public.lead_relation lr
        WHERE lr.id_lead = l.id_lead OR lr.id_related_lead = l.id_lead
      )
  AND (
        l.id_representative IS NOT NULL
     OR EXISTS (SELECT 1 FROM public.lead_representative x WHERE x.id_lead = l.id_lead)
     OR EXISTS (SELECT 1 FROM public.lead_request_child c WHERE c.id_lead = l.id_lead)
      )
ORDER BY l.id_lead;", connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                leadIds.Add(reader.GetInt32(0));
        }

        var adultByRep = new Dictionary<int, int>();
        foreach (var idLead in leadIds)
            BackfillOne(connection, idLead, otherId, brotherId, adultByRep);
    }

    private static Dictionary<string, int> LoadRelationIds(NpgsqlConnection connection)
    {
        var map = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        using var cmd = new NpgsqlCommand(
            "SELECT id_relation_type, relation_name FROM public.relation_type", connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            map[reader.GetString(1).Trim()] = reader.GetInt32(0);
        return map;
    }

    private static void BackfillOne(
        NpgsqlConnection connection,
        int idLead,
        int otherId,
        int brotherId,
        Dictionary<int, int> adultByRep)
    {
        int? primaryRepId = null;
        int? primaryRelTypeId = null;
        string childName = "";
        using (var cmd = new NpgsqlCommand(@"
SELECT l.id_representative, r.id_relation_type,
       TRIM(CONCAT_WS(' ', l.child_last_name, l.child_first_name))
FROM public.lead_request l
LEFT JOIN public.representative r ON r.id_representative = l.id_representative
WHERE l.id_lead = @id", connection))
        {
            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                primaryRepId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
                primaryRelTypeId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                childName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            }
        }

        var createdAdults = new HashSet<int>();
        if (primaryRepId is > 0)
        {
            var adultId = EnsureAdultLeadFromRepresentative(connection, primaryRepId.Value, idLead, adultByRep);
            if (adultId > 0 && adultId != idLead)
            {
                InsertRelation(connection, idLead, adultId, primaryRelTypeId is > 0 ? primaryRelTypeId : otherId,
                    isMainPayer: true, isLegal: true, sortOrder: 0);
                createdAdults.Add(primaryRepId.Value);
            }
        }

        short order = 1;
        using (var cmd = new NpgsqlCommand(@"
SELECT lr.id_representative, lr.id_relation_type
FROM public.lead_representative lr
WHERE lr.id_lead = @id
ORDER BY lr.sort_order, lr.id_lead_rep", connection))
        {
            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
            using var reader = cmd.ExecuteReader();
            var extras = new List<(int RepId, int? RelId)>();
            while (reader.Read())
                extras.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            reader.Close();

            foreach (var extra in extras)
            {
                if (createdAdults.Contains(extra.RepId))
                    continue;
                var adultId = EnsureAdultLeadFromRepresentative(connection, extra.RepId, idLead, adultByRep);
                if (adultId <= 0 || adultId == idLead)
                    continue;
                InsertRelation(connection, idLead, adultId, extra.RelId ?? otherId,
                    isMainPayer: false, isLegal: false, sortOrder: order++);
                createdAdults.Add(extra.RepId);
            }
        }

        using (var cmd = new NpgsqlCommand(@"
SELECT full_name, birth_date, phone, residence_address
FROM public.lead_request_child
WHERE id_lead = @id
ORDER BY sort_order, id_lead_child", connection))
        {
            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
            using var reader = cmd.ExecuteReader();
            var children = new List<(string Name, DateTime? Birth, string? Phone, string? Addr)>();
            while (reader.Read())
            {
                children.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
            reader.Close();

            foreach (var child in children)
            {
                if (string.IsNullOrWhiteSpace(child.Name))
                    continue;
                if (NamesMatch(childName, child.Name))
                    continue;
                var siblingId = InsertChildLead(connection, child.Name, child.Birth, child.Phone, child.Addr);
                if (siblingId <= 0 || siblingId == idLead)
                    continue;
                InsertRelation(connection, idLead, siblingId, brotherId > 0 ? brotherId : otherId,
                    isMainPayer: false, isLegal: false, sortOrder: order++);
            }
        }
    }

    private static bool NamesMatch(string a, string b)
    {
        static string Norm(string s) =>
            string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.Equals(Norm(a), Norm(b), StringComparison.CurrentCultureIgnoreCase);
    }

    private static int EnsureAdultLeadFromRepresentative(
        NpgsqlConnection connection,
        int idRepresentative,
        int excludeLeadId,
        Dictionary<int, int> adultByRep)
    {
        if (adultByRep.TryGetValue(idRepresentative, out var cached) && cached != excludeLeadId)
            return cached;

        using (var find = new NpgsqlCommand(@"
SELECT l.id_lead
FROM public.lead_request l
JOIN public.representative r ON r.id_representative = @rep
WHERE l.id_lead <> @exclude
  AND NULLIF(BTRIM(l.contact_phone), '') IS NOT NULL
  AND l.contact_phone = r.phone
  AND LOWER(TRIM(CONCAT_WS(' ', l.child_last_name, l.child_first_name)))
      = LOWER(TRIM(CONCAT_WS(' ', r.last_name, r.first_name, r.middle_name)))
ORDER BY l.id_lead
LIMIT 1", connection))
        {
            find.Parameters.Add("rep", NpgsqlDbType.Integer).Value = idRepresentative;
            find.Parameters.Add("exclude", NpgsqlDbType.Integer).Value = excludeLeadId;
            var existing = find.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
            {
                var id = Convert.ToInt32(existing);
                adultByRep[idRepresentative] = id;
                return id;
            }
        }

        string lastName = "";
        string firstName = "";
        string? middle = null;
        string? phone = null;
        string? address = null;
        string? workplace = null;
        using (var cmd = new NpgsqlCommand(@"
SELECT last_name, first_name, middle_name, phone, residence_address, workplace
FROM public.representative WHERE id_representative = @id", connection))
        {
            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idRepresentative;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return 0;
            lastName = reader.GetString(0);
            firstName = reader.GetString(1);
            middle = reader.IsDBNull(2) ? null : reader.GetString(2);
            phone = reader.IsDBNull(3) ? null : reader.GetString(3);
            address = reader.IsDBNull(4) ? null : reader.GetString(4);
            workplace = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;
        }

        var firstStored = string.IsNullOrWhiteSpace(middle) ? firstName : $"{firstName} {middle}".Trim();
        using var insert = new NpgsqlCommand(@"
INSERT INTO public.lead_request
    (child_first_name, child_last_name, child_birth_date, contact_phone, child_phone,
     child_residence_address, id_direction, consent_personal_data, status, comment,
     id_representative, workplace, is_adult)
VALUES (@fn, @ln, NULL, @ph, @ph, @addr, NULL, FALSE, 'новая', NULL, @rep, @work, TRUE)
RETURNING id_lead", connection);
        insert.Parameters.Add("fn", NpgsqlDbType.Varchar).Value = string.IsNullOrWhiteSpace(firstStored) ? lastName : firstStored;
        insert.Parameters.Add("ln", NpgsqlDbType.Varchar).Value = (object?)lastName ?? DBNull.Value;
        insert.Parameters.Add("ph", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone.Trim();
        insert.Parameters.Add("addr", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(address) ? DBNull.Value : address.Trim();
        insert.Parameters.Add("rep", NpgsqlDbType.Integer).Value = idRepresentative;
        insert.Parameters.Add("work", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(workplace) ? DBNull.Value : workplace.Trim();
        var newId = Convert.ToInt32(insert.ExecuteScalar());
        adultByRep[idRepresentative] = newId;
        return newId;
    }

    private static int InsertChildLead(
        NpgsqlConnection connection,
        string fullName,
        DateTime? birthDate,
        string? phone,
        string? address)
    {
        var fio = FioHelper.Parse(fullName);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
        var firstStored = string.IsNullOrWhiteSpace(fio.MiddleName)
            ? firstName
            : $"{firstName} {fio.MiddleName}".Trim();
        using var insert = new NpgsqlCommand(@"
INSERT INTO public.lead_request
    (child_first_name, child_last_name, child_birth_date, contact_phone, child_phone,
     child_residence_address, id_direction, consent_personal_data, status, is_adult)
VALUES (@fn, @ln, @bd, @ph, @ph, @addr, NULL, FALSE, 'новая', FALSE)
RETURNING id_lead", connection);
        insert.Parameters.Add("fn", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(firstStored) ? fullName.Trim() : firstStored;
        insert.Parameters.Add("ln", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lastName) ? DBNull.Value : lastName;
        insert.Parameters.Add("bd", NpgsqlDbType.Date).Value = (object?)birthDate ?? DBNull.Value;
        insert.Parameters.Add("ph", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone.Trim();
        insert.Parameters.Add("addr", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(address) ? DBNull.Value : address.Trim();
        return Convert.ToInt32(insert.ExecuteScalar());
    }

    private static void InsertRelation(
        NpgsqlConnection connection,
        int idLead,
        int idRelated,
        int? relationTypeId,
        bool isMainPayer,
        bool isLegal,
        short sortOrder)
    {
        using var cmd = new NpgsqlCommand(@"
INSERT INTO public.lead_relation
    (id_lead, id_related_lead, id_relation_type, is_main_payer, is_legal_representative, sort_order)
VALUES (@a, @b, @rel, @pay, @leg, @ord)
ON CONFLICT (id_lead, id_related_lead) DO NOTHING", connection);
        cmd.Parameters.Add("a", NpgsqlDbType.Integer).Value = idLead;
        cmd.Parameters.Add("b", NpgsqlDbType.Integer).Value = idRelated;
        cmd.Parameters.Add("rel", NpgsqlDbType.Integer).Value =
            relationTypeId is > 0 ? relationTypeId.Value : DBNull.Value;
        cmd.Parameters.Add("pay", NpgsqlDbType.Boolean).Value = isMainPayer;
        cmd.Parameters.Add("leg", NpgsqlDbType.Boolean).Value = isLegal;
        cmd.Parameters.Add("ord", NpgsqlDbType.Smallint).Value = sortOrder;
        cmd.ExecuteNonQuery();
    }
}
