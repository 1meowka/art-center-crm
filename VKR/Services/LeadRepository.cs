using System.Linq;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public class LeadRepository
{
    public async Task<List<Lead>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        var list = new List<Lead>();
        const string sql = @"
                SELECT l.id_lead, l.created_date, l.child_first_name, l.child_last_name,
                       l.child_birth_date, l.contact_phone, l.child_phone, l.child_residence_address,
                       l.id_direction, d.direction_name,
                       l.id_source, s.source_name, l.consent_personal_data, l.status, l.comment,
                       COALESCE(dirs.all_names, d.direction_name) AS all_directions,
                       l.id_representative,
                       TRIM(CONCAT_WS(' ', r.last_name, r.first_name, r.middle_name)) AS rep_full_name,
                       l.id_institution,
                       ei.institution_name,
                       l.workplace,
                       l.is_adult,
                       payer.payer_name,
                       payer.payer_phone,
                       COALESCE(NULLIF(TRIM(CONCAT_WS(', ',
                            NULLIF(BTRIM(COALESCE(l.mother_full_name, '')), ''),
                            NULLIF(BTRIM(COALESCE(l.father_full_name, '')), ''))), ''), rel.relatives) AS relatives,
                       l.contract_note,
                       l.mother_full_name,
                       l.mother_phone,
                       l.father_full_name,
                       l.father_phone,
                       l.school_shift_comment,
                       l.parent1_relation, l.parent1_full_name, l.parent1_phone, l.parent1_age,
                       l.parent2_relation, l.parent2_full_name, l.parent2_phone, l.parent2_age,
                       l.parent3_relation, l.parent3_full_name, l.parent3_phone, l.parent3_age
                  FROM lead_request l
             LEFT JOIN direction d ON d.id_direction = l.id_direction
             LEFT JOIN source_of_arrival s ON s.id_source = l.id_source
             LEFT JOIN representative r ON r.id_representative = l.id_representative
             LEFT JOIN educational_institution ei ON ei.id_institution = l.id_institution
             LEFT JOIN LATERAL (
                    SELECT string_agg(d2.direction_name, ', ' ORDER BY d2.direction_name) AS all_names
                    FROM lead_request_direction lrd
                    JOIN direction d2 ON d2.id_direction = lrd.id_direction
                    WHERE lrd.id_lead = l.id_lead
                  ) dirs ON TRUE
             LEFT JOIN LATERAL (
                    SELECT TRIM(CONCAT_WS(' ', r2.child_last_name, r2.child_first_name)) AS payer_name,
                           NULLIF(BTRIM(COALESCE(r2.contact_phone, '')), '') AS payer_phone
                      FROM public.lead_relation lr
                      JOIN public.lead_request r2 ON r2.id_lead = lr.id_related_lead
                     WHERE lr.id_lead = l.id_lead AND lr.is_main_payer
                     LIMIT 1
                  ) payer ON TRUE
             LEFT JOIN LATERAL (
                    SELECT string_agg(x.name, ', ' ORDER BY x.name) AS relatives
                      FROM (
                            SELECT TRIM(CONCAT_WS(' ', r3.child_last_name, r3.child_first_name)) AS name
                              FROM public.lead_relation lr
                              JOIN public.lead_request r3 ON r3.id_lead = lr.id_related_lead
                             WHERE lr.id_lead = l.id_lead
                            UNION
                            SELECT TRIM(CONCAT_WS(' ', r4.child_last_name, r4.child_first_name))
                              FROM public.lead_relation lr
                              JOIN public.lead_request r4 ON r4.id_lead = lr.id_lead
                             WHERE lr.id_related_lead = l.id_lead
                      ) x
                  ) rel ON TRUE
              ORDER BY l.created_date DESC";

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new Lead
            {
                IdLead = reader.GetInt32(0),
                CreatedDate = reader.GetDateTime(1),
                ChildFirstName = reader.GetString(2),
                ChildLastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                ChildBirthDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                ContactPhone = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                ChildPhone = reader.IsDBNull(6) ? null : reader.GetString(6),
                ChildResidenceAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
                IdDirection = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                DirectionName = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                IdSource = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                SourceName = reader.IsDBNull(11) ? null : reader.GetString(11),
                ConsentPersonalData = reader.GetBoolean(12),
                Status = reader.GetString(13),
                Comment = reader.IsDBNull(14) ? null : reader.GetString(14),
                AllDirectionsText = reader.IsDBNull(15)
                    ? (reader.IsDBNull(9) ? string.Empty : reader.GetString(9))
                    : reader.GetString(15),
                IdRepresentative = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                RepresentativeFullName = reader.IsDBNull(17) ? null : reader.GetString(17),
                IdInstitution = reader.IsDBNull(18) ? null : reader.GetInt32(18),
                InstitutionName = reader.IsDBNull(19) ? null : reader.GetString(19),
                Workplace = reader.IsDBNull(20) ? null : reader.GetString(20),
                IsAdultRecord = !reader.IsDBNull(21) && reader.GetBoolean(21),
                MainPayerName = reader.IsDBNull(22) ? null : reader.GetString(22),
                MainPayerPhone = reader.IsDBNull(23) ? null : reader.GetString(23),
                RelativesSummary = reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
                ContractNote = reader.IsDBNull(25) ? null : reader.GetString(25),
                MotherFullName = reader.FieldCount > 26 && !reader.IsDBNull(26) ? reader.GetString(26) : null,
                MotherPhone = reader.FieldCount > 27 && !reader.IsDBNull(27) ? reader.GetString(27) : null,
                FatherFullName = reader.FieldCount > 28 && !reader.IsDBNull(28) ? reader.GetString(28) : null,
                FatherPhone = reader.FieldCount > 29 && !reader.IsDBNull(29) ? reader.GetString(29) : null,
                SchoolShiftComment = reader.FieldCount > 30 && !reader.IsDBNull(30) ? reader.GetString(30) : null,
                Parent1Relation = reader.FieldCount > 31 && !reader.IsDBNull(31) ? reader.GetString(31) : null,
                Parent1FullName = reader.FieldCount > 32 && !reader.IsDBNull(32) ? reader.GetString(32) : null,
                Parent1Phone = reader.FieldCount > 33 && !reader.IsDBNull(33) ? reader.GetString(33) : null,
                Parent1Age = reader.FieldCount > 34 && !reader.IsDBNull(34) ? reader.GetInt16(34) : null,
                Parent2Relation = reader.FieldCount > 35 && !reader.IsDBNull(35) ? reader.GetString(35) : null,
                Parent2FullName = reader.FieldCount > 36 && !reader.IsDBNull(36) ? reader.GetString(36) : null,
                Parent2Phone = reader.FieldCount > 37 && !reader.IsDBNull(37) ? reader.GetString(37) : null,
                Parent2Age = reader.FieldCount > 38 && !reader.IsDBNull(38) ? reader.GetInt16(38) : null,
                Parent3Relation = reader.FieldCount > 39 && !reader.IsDBNull(39) ? reader.GetString(39) : null,
                Parent3FullName = reader.FieldCount > 40 && !reader.IsDBNull(40) ? reader.GetString(40) : null,
                Parent3Phone = reader.FieldCount > 41 && !reader.IsDBNull(41) ? reader.GetString(41) : null,
                Parent3Age = reader.FieldCount > 42 && !reader.IsDBNull(42) ? reader.GetInt16(42) : null
            });
            list[^1].SyncParentBlocks();
        }

        return list;
    }

    public async Task<Lead?> GetByIdAsync(int idLead, CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        const string sql = @"
                SELECT l.id_lead, l.created_date, l.child_first_name, l.child_last_name,
                       l.child_birth_date, l.contact_phone, l.child_phone, l.child_residence_address,
                       l.id_direction, d.direction_name,
                       l.id_source, s.source_name, l.consent_personal_data, l.status, l.comment,
                       l.id_representative,
                       r.last_name AS rep_last_name,
                       r.first_name AS rep_first_name,
                       r.middle_name AS rep_middle_name,
                       r.email AS rep_email,
                       r.residence_address AS rep_residence_address,
                       r.id_relation_type AS rep_relation_type_id,
                       l.id_institution,
                       ei.institution_name,
                       l.workplace,
                       l.is_adult,
                       l.contract_note,
                       l.mother_full_name,
                       l.mother_phone,
                       l.father_full_name,
                       l.father_phone,
                       l.school_shift_comment,
                       l.parent1_relation, l.parent1_full_name, l.parent1_phone, l.parent1_age,
                       l.parent2_relation, l.parent2_full_name, l.parent2_phone, l.parent2_age,
                       l.parent3_relation, l.parent3_full_name, l.parent3_phone, l.parent3_age
                  FROM lead_request l
             LEFT JOIN direction d ON d.id_direction = l.id_direction
             LEFT JOIN source_of_arrival s ON s.id_source = l.id_source
             LEFT JOIN representative r ON r.id_representative = l.id_representative
             LEFT JOIN educational_institution ei ON ei.id_institution = l.id_institution
                 WHERE l.id_lead = @id";

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapLeadFromReader(reader);
    }

    private static Lead MapLeadFromReader(NpgsqlDataReader reader)
    {
        var lead = new Lead
        {
            IdLead = reader.GetInt32(0),
            CreatedDate = reader.GetDateTime(1),
            ChildFirstName = reader.GetString(2),
            ChildLastName = reader.IsDBNull(3) ? null : reader.GetString(3),
            ChildBirthDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            ContactPhone = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            ChildPhone = reader.IsDBNull(6) ? null : reader.GetString(6),
            ChildResidenceAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
            IdDirection = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
            DirectionName = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            IdSource = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            SourceName = reader.IsDBNull(11) ? null : reader.GetString(11),
            ConsentPersonalData = reader.GetBoolean(12),
            Status = reader.GetString(13),
            Comment = reader.IsDBNull(14) ? null : reader.GetString(14)
        };

        if (reader.FieldCount > 15)
        {
            lead.IdRepresentative = reader.IsDBNull(15) ? null : reader.GetInt32(15);
            if (reader.FieldCount > 21)
            {
                if (!reader.IsDBNull(16)) lead.RepLastName = reader.GetString(16);
                if (!reader.IsDBNull(17)) lead.RepFirstName = reader.GetString(17);
                lead.RepMiddleName = reader.IsDBNull(18) ? null : reader.GetString(18);
                lead.RepEmail = reader.IsDBNull(19) ? null : reader.GetString(19);
                lead.RepResidenceAddress = reader.IsDBNull(20) ? null : reader.GetString(20);
                lead.RepRelationTypeId = reader.IsDBNull(21) ? null : reader.GetInt32(21);
                lead.RepresentativeFullName = lead.RepresentativeDisplayName;
            }
        }

        if (reader.FieldCount > 22)
        {
            lead.IdInstitution = reader.IsDBNull(22) ? null : reader.GetInt32(22);
            lead.InstitutionName = reader.IsDBNull(23) ? null : reader.GetString(23);
            lead.Workplace = reader.IsDBNull(24) ? null : reader.GetString(24);
            lead.IsAdultRecord = reader.FieldCount > 25 && !reader.IsDBNull(25) && reader.GetBoolean(25);
            if (reader.FieldCount > 26)
                lead.ContractNote = reader.IsDBNull(26) ? null : reader.GetString(26);
            if (reader.FieldCount > 27)
                lead.MotherFullName = reader.IsDBNull(27) ? null : reader.GetString(27);
            if (reader.FieldCount > 28)
                lead.MotherPhone = reader.IsDBNull(28) ? null : reader.GetString(28);
            if (reader.FieldCount > 29)
                lead.FatherFullName = reader.IsDBNull(29) ? null : reader.GetString(29);
            if (reader.FieldCount > 30)
                lead.FatherPhone = reader.IsDBNull(30) ? null : reader.GetString(30);
            if (reader.FieldCount > 31)
                lead.SchoolShiftComment = reader.IsDBNull(31) ? null : reader.GetString(31);
            if (reader.FieldCount > 32)
                lead.Parent1Relation = reader.IsDBNull(32) ? null : reader.GetString(32);
            if (reader.FieldCount > 33)
                lead.Parent1FullName = reader.IsDBNull(33) ? null : reader.GetString(33);
            if (reader.FieldCount > 34)
                lead.Parent1Phone = reader.IsDBNull(34) ? null : reader.GetString(34);
            if (reader.FieldCount > 35)
                lead.Parent1Age = reader.IsDBNull(35) ? null : reader.GetInt16(35);
            if (reader.FieldCount > 36)
                lead.Parent2Relation = reader.IsDBNull(36) ? null : reader.GetString(36);
            if (reader.FieldCount > 37)
                lead.Parent2FullName = reader.IsDBNull(37) ? null : reader.GetString(37);
            if (reader.FieldCount > 38)
                lead.Parent2Phone = reader.IsDBNull(38) ? null : reader.GetString(38);
            if (reader.FieldCount > 39)
                lead.Parent2Age = reader.IsDBNull(39) ? null : reader.GetInt16(39);
            if (reader.FieldCount > 40)
                lead.Parent3Relation = reader.IsDBNull(40) ? null : reader.GetString(40);
            if (reader.FieldCount > 41)
                lead.Parent3FullName = reader.IsDBNull(41) ? null : reader.GetString(41);
            if (reader.FieldCount > 42)
                lead.Parent3Phone = reader.IsDBNull(42) ? null : reader.GetString(42);
            if (reader.FieldCount > 43)
                lead.Parent3Age = reader.IsDBNull(43) ? null : reader.GetInt16(43);
        }

        lead.SyncParentBlocks();
        return lead;
    }

    public async Task<int> InsertAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                INSERT INTO lead_request 
                    (child_first_name, child_last_name, child_birth_date, contact_phone, child_phone,
                     child_residence_address, id_direction, id_source, consent_personal_data, status, comment,
                     id_representative, id_institution, workplace, is_adult, contract_note,
                     mother_full_name, mother_phone, father_full_name, father_phone,
                     school_shift_comment,
                     parent1_relation, parent1_full_name, parent1_phone, parent1_age,
                     parent2_relation, parent2_full_name, parent2_phone, parent2_age,
                     parent3_relation, parent3_full_name, parent3_phone, parent3_age)
                VALUES (@fn, @ln, @bd, @ph, @cph, @addr, @dir, @src, @cons, @st, @cm, @repid, @inst, @work, @adult, @cnote,
                        @momn, @momp, @dadn, @dadp, @shift,
                        @p1r, @p1n, @p1p, @p1a, @p2r, @p2n, @p2p, @p2a, @p3r, @p3n, @p3p, @p3a)
                RETURNING id_lead";

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        FillLeadParameters(cmd, lead);

        return (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task SetLeadDirectionsAsync(int idLead, IReadOnlyList<int> directionIds, CancellationToken cancellationToken = default)
    {
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM public.lead_request_direction WHERE id_lead = @id", conn, tx))
        {
            deleteCmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idLead;
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var dirId in directionIds.Distinct())
        {
            await using var insertCmd = new NpgsqlCommand(
                @"INSERT INTO public.lead_request_direction (id_lead, id_direction)
                  VALUES (@lead, @dir) ON CONFLICT DO NOTHING", conn, tx);
            insertCmd.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = idLead;
            insertCmd.Parameters.Add("@dir", NpgsqlDbType.Integer).Value = dirId;
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<List<int>> GetLeadDirectionIdsAsync(int idLead, CancellationToken cancellationToken = default)
    {
        var list = new List<int>();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            @"SELECT id_direction FROM public.lead_request_direction
              WHERE id_lead = @id ORDER BY id_direction", conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(reader.GetInt32(0));
        return list;
    }

    public async Task UpdateAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                UPDATE lead_request SET
                    child_first_name         = @fn,
                    child_last_name          = @ln,
                    child_birth_date         = @bd,
                    contact_phone            = @ph,
                    child_phone              = @cph,
                    child_residence_address  = @addr,
                    id_direction             = @dir,
                    id_source                = @src,
                    consent_personal_data    = @cons,
                    status                   = @st,
                    comment                  = @cm,
                    id_representative        = @repid,
                    id_institution            = @inst,
                    workplace                = @work,
                    is_adult                  = @adult,
                    contract_note            = @cnote,
                    mother_full_name         = @momn,
                    mother_phone             = @momp,
                    father_full_name         = @dadn,
                    father_phone             = @dadp,
                    school_shift_comment     = @shift,
                    parent1_relation         = @p1r,
                    parent1_full_name        = @p1n,
                    parent1_phone            = @p1p,
                    parent1_age              = @p1a,
                    parent2_relation         = @p2r,
                    parent2_full_name        = @p2n,
                    parent2_phone            = @p2p,
                    parent2_age              = @p2a,
                    parent3_relation         = @p3r,
                    parent3_full_name        = @p3n,
                    parent3_phone            = @p3p,
                    parent3_age              = @p3a
                WHERE id_lead = @id";

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = lead.IdLead;
        FillLeadParameters(cmd, lead);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int idLead, CancellationToken cancellationToken = default)
    {
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("DELETE FROM lead_request WHERE id_lead = @id", conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idLead;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<DirectionRef>> GetDirectionsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DirectionRef>();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT id_direction, direction_name FROM direction WHERE is_active = TRUE ORDER BY direction_name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new DirectionRef { IdDirection = reader.GetInt32(0), Name = reader.GetString(1) });
        return LookupDeduper.ByName(list, item => item.Name, item => item.IdDirection);
    }

    public async Task<List<SourceRef>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<SourceRef>();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT id_source, source_name FROM source_of_arrival ORDER BY source_name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new SourceRef { IdSource = reader.GetInt32(0), Name = reader.GetString(1) });
        return LookupDeduper.ByName(list, item => item.Name, item => item.IdSource);
    }

    public async Task<string?> GetContractNoteByStudentIdAsync(int idStudent, CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT lr.contract_note
FROM public.student s
JOIN public.lead_request lr ON lr.id_lead = s.id_source_lead
WHERE s.id_student = @id
LIMIT 1", conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idStudent;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value)
            return null;
        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public async Task<List<StudentTypeRef>> GetStudentTypesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<StudentTypeRef>();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT id_student_type, type_name FROM student_type ORDER BY id_student_type", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new StudentTypeRef { IdStudentType = reader.GetInt32(0), Name = reader.GetString(1) });
        return LookupDeduper.ByName(list, item => item.Name, item => item.IdStudentType);
    }

    public async Task<int> ConvertLeadAsync(
        int idLead,
        Student student,
        int? idRepresentative = null,
        int? idRelationType = null,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        const string insertSql = @"
        INSERT INTO student 
            (last_name, first_name, middle_name, birth_date, phone, residence_address,
             status, id_representative, id_student_type, arrival_date, id_institution, comment)
        VALUES (@ln, @fn, @mn, @bd, @ph, @addr, @st, @rep, @stype, @ad, @inst, @cm)
        RETURNING id_student";

        const string updateLeadSql =
            "UPDATE lead_request SET status = 'конвертирована' WHERE id_lead = @id";

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        int newStudentId;
        await using (var insertCmd = new NpgsqlCommand(insertSql, conn, tx))
        {
            insertCmd.Parameters.Add("@ln", NpgsqlDbType.Varchar).Value = student.LastName;
            insertCmd.Parameters.Add("@fn", NpgsqlDbType.Varchar).Value = student.FirstName;
            insertCmd.Parameters.Add("@mn", NpgsqlDbType.Varchar).Value = (object?)student.MiddleName ?? DBNull.Value;
            insertCmd.Parameters.Add("@bd", NpgsqlDbType.Date).Value = student.BirthDate;
            insertCmd.Parameters.Add("@ph", NpgsqlDbType.Varchar).Value = (object?)student.Phone ?? DBNull.Value;
            insertCmd.Parameters.Add("@addr", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(student.ResidenceAddress) ? (object)DBNull.Value : student.ResidenceAddress.Trim();
            insertCmd.Parameters.Add("@st", NpgsqlDbType.Varchar).Value = student.Status;
            insertCmd.Parameters.Add("@rep", NpgsqlDbType.Integer).Value =
                (object?)(idRepresentative ?? student.IdRepresentative) ?? DBNull.Value;
            insertCmd.Parameters.Add("@stype", NpgsqlDbType.Integer).Value = student.IdStudentType;
            insertCmd.Parameters.Add("@ad", NpgsqlDbType.Date).Value = student.ArrivalDate;
            insertCmd.Parameters.Add("@inst", NpgsqlDbType.Integer).Value =
                student.IdInstitution is > 0 ? student.IdInstitution.Value : DBNull.Value;
            insertCmd.Parameters.Add("@cm", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(student.Comment) ? DBNull.Value : student.Comment.Trim();

            newStudentId = (int)(await insertCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var updateCmd = new NpgsqlCommand(updateLeadSql, conn, tx)
        {
            Parameters =
            {
                new("@id", NpgsqlDbType.Integer) { Value = idLead }
            }
        };
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        await using (var linkCmd = new NpgsqlCommand(
            "UPDATE public.student SET id_source_lead = @lead WHERE id_student = @student", conn, tx))
        {
            linkCmd.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = idLead;
            linkCmd.Parameters.Add("@student", NpgsqlDbType.Integer).Value = newStudentId;
            await linkCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var repId = idRepresentative ?? student.IdRepresentative;
        if (repId.HasValue && repId.Value > 0)
        {
            var relationTypeId = idRelationType ?? await GetDefaultRelationTypeIdAsync(conn, tx, cancellationToken);
            if (relationTypeId > 0)
            {
                await AttachStudentRepresentativeAsync(
                    conn, tx, newStudentId, repId.Value, relationTypeId, true, true, cancellationToken);

                await using var updateStudentRepCmd = new NpgsqlCommand(
                    "UPDATE public.student SET id_representative = @rep WHERE id_student = @student", conn, tx);
                updateStudentRepCmd.Parameters.Add("@rep", NpgsqlDbType.Integer).Value = repId.Value;
                updateStudentRepCmd.Parameters.Add("@student", NpgsqlDbType.Integer).Value = newStudentId;
                await updateStudentRepCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var extraCmd = new NpgsqlCommand(@"
SELECT lr.id_representative, lr.id_relation_type
FROM public.lead_representative lr
WHERE lr.id_lead = @lead", conn, tx))
        {
            extraCmd.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = idLead;
            await using var reader = await extraCmd.ExecuteReaderAsync(cancellationToken);
            var extras = new List<(int RepId, int RelId)>();
            while (await reader.ReadAsync(cancellationToken))
            {
                extras.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? 0 : reader.GetInt32(1)));
            }

            reader.Close();
            foreach (var extra in extras)
            {
                if (repId.HasValue && extra.RepId == repId.Value)
                    continue;
                var extraRelId = extra.RelId > 0
                    ? extra.RelId
                    : await GetDefaultRelationTypeIdAsync(conn, tx, cancellationToken);
                await AttachStudentRepresentativeAsync(
                    conn, tx, newStudentId, extra.RepId, extraRelId, false, false, cancellationToken);
            }
        }

        await AttachFamilyAdultsOnConvertAsync(conn, tx, idLead, newStudentId, repId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return newStudentId;
    }

    private static async Task AttachStudentRepresentativeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        int studentId,
        int representativeId,
        int relationTypeId,
        bool isMainPayer,
        bool isLegal,
        CancellationToken cancellationToken)
    {
        if (representativeId <= 0)
            return;
        if (relationTypeId <= 0)
            relationTypeId = await GetDefaultRelationTypeIdAsync(conn, tx, cancellationToken);
        if (relationTypeId <= 0)
            return;

        await using var attachCmd = new NpgsqlCommand(@"
INSERT INTO public.student_representative
    (id_student, id_representative, id_relation_type, is_main_payer, is_legal_representative)
SELECT @student, @rep, @rel, @pay, @leg
WHERE NOT EXISTS (
    SELECT 1 FROM public.student_representative
    WHERE id_student = @student AND id_representative = @rep
);", conn, tx);
        attachCmd.Parameters.Add("@student", NpgsqlDbType.Integer).Value = studentId;
        attachCmd.Parameters.Add("@rep", NpgsqlDbType.Integer).Value = representativeId;
        attachCmd.Parameters.Add("@rel", NpgsqlDbType.Integer).Value = relationTypeId;
        attachCmd.Parameters.Add("@pay", NpgsqlDbType.Boolean).Value = isMainPayer;
        attachCmd.Parameters.Add("@leg", NpgsqlDbType.Boolean).Value = isLegal;
        await attachCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AttachFamilyAdultsOnConvertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        int idLead,
        int studentId,
        int? alreadyAttachedRepId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT r.id_representative, lr.id_relation_type, lr.is_main_payer, lr.is_legal_representative
FROM public.lead_relation lr
JOIN public.lead_request related ON related.id_lead = lr.id_related_lead
JOIN public.representative r ON r.id_representative = related.id_representative
WHERE lr.id_lead = @lead
  AND related.id_representative IS NOT NULL", conn, tx);
        cmd.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(int RepId, int RelId, bool Payer, bool Legal)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3)));
        }

        reader.Close();
        foreach (var row in rows)
        {
            if (alreadyAttachedRepId is > 0 && row.RepId == alreadyAttachedRepId.Value)
                continue;
            await AttachStudentRepresentativeAsync(
                conn, tx, studentId, row.RepId, row.RelId, row.Payer, row.Legal, cancellationToken);
        }
    }

    private static void FillLeadParameters(NpgsqlCommand cmd, Lead lead)
    {
        lead.SyncParentBlocks();
        cmd.Parameters.Add("@fn", NpgsqlDbType.Varchar).Value = lead.ChildFirstName;
        cmd.Parameters.Add("@ln", NpgsqlDbType.Varchar).Value = (object?)lead.ChildLastName ?? DBNull.Value;
        cmd.Parameters.Add("@bd", NpgsqlDbType.Date).Value = (object?)lead.ChildBirthDate ?? DBNull.Value;
        cmd.Parameters.Add("@ph", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.ContactPhone) ? (object)DBNull.Value : lead.ContactPhone.Trim();
        cmd.Parameters.Add("@cph", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.ChildPhone) ? (object)DBNull.Value : lead.ChildPhone.Trim();
        cmd.Parameters.Add("@addr", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(lead.ChildResidenceAddress) ? (object)DBNull.Value : lead.ChildResidenceAddress.Trim();
        cmd.Parameters.Add("@dir", NpgsqlDbType.Integer).Value =
            lead.IdDirection is > 0 ? lead.IdDirection : DBNull.Value;
        cmd.Parameters.Add("@src", NpgsqlDbType.Integer).Value = (object?)lead.IdSource ?? DBNull.Value;
        cmd.Parameters.Add("@cons", NpgsqlDbType.Boolean).Value = lead.ConsentPersonalData;
        cmd.Parameters.Add("@st", NpgsqlDbType.Varchar).Value = lead.Status;
        cmd.Parameters.Add("@cm", NpgsqlDbType.Text).Value = (object?)lead.Comment ?? DBNull.Value;
        cmd.Parameters.Add("@repid", NpgsqlDbType.Integer).Value =
            lead.IdRepresentative.HasValue && lead.IdRepresentative.Value > 0
                ? lead.IdRepresentative.Value
                : DBNull.Value;
        cmd.Parameters.Add("@inst", NpgsqlDbType.Integer).Value =
            lead.IdInstitution is > 0 ? lead.IdInstitution.Value : DBNull.Value;
        cmd.Parameters.Add("@work", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.Workplace) ? DBNull.Value : lead.Workplace.Trim();
        cmd.Parameters.Add("@adult", NpgsqlDbType.Boolean).Value = lead.IsAdultGuest;
        cmd.Parameters.Add("@cnote", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(lead.ContractNote) ? DBNull.Value : lead.ContractNote.Trim();
        cmd.Parameters.Add("@momn", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.MotherFullName) ? DBNull.Value : lead.MotherFullName.Trim();
        cmd.Parameters.Add("@momp", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.MotherPhone) ? DBNull.Value : lead.MotherPhone.Trim();
        cmd.Parameters.Add("@dadn", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.FatherFullName) ? DBNull.Value : lead.FatherFullName.Trim();
        cmd.Parameters.Add("@dadp", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(lead.FatherPhone) ? DBNull.Value : lead.FatherPhone.Trim();
        cmd.Parameters.Add("@shift", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(lead.SchoolShiftComment) ? DBNull.Value : lead.SchoolShiftComment.Trim();
        BindParent(cmd, "p1r", "p1n", "p1p", "p1a", lead.Parent1Relation, lead.Parent1FullName, lead.Parent1Phone, lead.Parent1Age);
        BindParent(cmd, "p2r", "p2n", "p2p", "p2a", lead.Parent2Relation, lead.Parent2FullName, lead.Parent2Phone, lead.Parent2Age);
        BindParent(cmd, "p3r", "p3n", "p3p", "p3a", lead.Parent3Relation, lead.Parent3FullName, lead.Parent3Phone, lead.Parent3Age);
    }

    private static void BindParent(
        NpgsqlCommand cmd, string rel, string name, string phone, string age,
        string? relationValue, string? nameValue, string? phoneValue, short? ageValue)
    {
        cmd.Parameters.Add("@" + rel, NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(relationValue) ? DBNull.Value : relationValue.Trim();
        cmd.Parameters.Add("@" + name, NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(nameValue) ? DBNull.Value : nameValue.Trim();
        cmd.Parameters.Add("@" + phone, NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(phoneValue) ? DBNull.Value : phoneValue.Trim();
        cmd.Parameters.Add("@" + age, NpgsqlDbType.Smallint).Value =
            ageValue.HasValue ? ageValue.Value : DBNull.Value;
    }

    private static async Task<int> GetDefaultRelationTypeIdAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id_relation_type FROM public.relation_type ORDER BY id_relation_type LIMIT 1", conn, tx);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    public async Task<List<Lead>> GetLeadsForTrialAsync(int idDirection, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        var result = new List<Lead>();

        foreach (var lead in all)
        {
            if (lead.Status is "конвертирована" or "отказ")
                continue;

            if (idDirection <= 0)
            {
                result.Add(lead);
                continue;
            }

            if (lead.IdDirection == idDirection)
            {
                result.Add(lead);
                continue;
            }

            var directionIds = await GetLeadDirectionIdsAsync(lead.IdLead, cancellationToken);
            if (directionIds.Contains(idDirection))
                result.Add(lead);
        }

        return result;
    }

    public async Task EnsureDirectionIdsAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        if (lead.IdLead <= 0) return;
        var ids = await GetLeadDirectionIdsAsync(lead.IdLead, cancellationToken);
        if (ids.Count > 0)
            lead.DirectionIds = ids;
        else if (lead.IdDirection > 0)
            lead.DirectionIds = new List<int> { lead.IdDirection };
    }

    public async Task<int> SaveWithRepresentativeAsync(
        Lead lead,
        StudentRepository studentRepository,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        return await SaveWithFamilyAsync(lead, studentRepository, cancellationToken);
    }

    public async Task<IReadOnlyList<LeadRepresentativeItem>> GetAdditionalRepresentativesAsync(
        int idLead,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        var result = new List<LeadRepresentativeItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT
    r.id_representative,
    r.last_name,
    r.first_name,
    r.middle_name,
    r.phone,
    r.email,
    r.residence_address,
    lr.id_relation_type,
    COALESCE(rt.relation_name, '') AS relation_name
FROM public.lead_representative lr
JOIN public.representative r ON r.id_representative = lr.id_representative
LEFT JOIN public.relation_type rt ON rt.id_relation_type = lr.id_relation_type
WHERE lr.id_lead = @id
ORDER BY lr.sort_order, r.last_name, r.first_name;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LeadRepresentativeItem
            {
                IdRepresentative = reader.GetInt32(0),
                LastName = reader.GetString(1),
                FirstName = reader.GetString(2),
                MiddleName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.GetString(4),
                Email = reader.IsDBNull(5) ? null : reader.GetString(5),
                ResidenceAddress = reader.IsDBNull(6) ? null : reader.GetString(6),
                RelationTypeId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                RelationName = reader.GetString(8)
            });
        }

        return result;
    }

    public async Task SaveAdditionalRepresentativesAsync(
        int idLead,
        IReadOnlyList<LeadRepresentativeItem> representatives,
        StudentRepository studentRepository,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM public.lead_representative WHERE id_lead = @id;", connection, transaction))
        {
            deleteCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        short order = 0;
        foreach (var item in representatives)
        {
            var (lastName, firstName) = FioHelper.NormalizeStoredNames(item.LastName, item.FirstName);
            if (string.IsNullOrWhiteSpace(lastName))
                continue;
            if (string.IsNullOrWhiteSpace(item.Phone))
                continue;

            var repModel = new RepresentativeEditModel
            {
                IdRepresentative = item.IdRepresentative,
                LastName = lastName,
                FirstName = firstName,
                MiddleName = item.MiddleName,
                Phone = item.Phone,
                Email = item.Email,
                ResidenceAddress = item.ResidenceAddress,
                FallbackRelationTypeId = item.RelationTypeId
            };

            if (repModel.IdRepresentative == 0)
            {
                var existingId = await studentRepository.FindRepresentativeIdByPhoneAsync(item.Phone);
                if (existingId.HasValue)
                    repModel.IdRepresentative = existingId.Value;
            }

            var idRep = await studentRepository.SaveRepresentativeAsync(repModel);

            await using var insertCmd = new NpgsqlCommand(@"
INSERT INTO public.lead_representative (id_lead, id_representative, id_relation_type, sort_order)
VALUES (@lead, @rep, @rel, @ord)
ON CONFLICT (id_lead, id_representative) DO UPDATE SET
    id_relation_type = EXCLUDED.id_relation_type,
    sort_order = EXCLUDED.sort_order;", connection, transaction);
            insertCmd.Parameters.Add("lead", NpgsqlDbType.Integer).Value = idLead;
            insertCmd.Parameters.Add("rep", NpgsqlDbType.Integer).Value = idRep;
            insertCmd.Parameters.Add("rel", NpgsqlDbType.Integer).Value =
                item.RelationTypeId is > 0 ? item.RelationTypeId.Value : DBNull.Value;
            insertCmd.Parameters.Add("ord", NpgsqlDbType.Smallint).Value = order++;
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertRepresentativeAsync(
        Lead lead,
        StudentRepository studentRepository,
        CancellationToken cancellationToken)
    {
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(lead.RepLastName, lead.RepFirstName);
        lead.RepLastName = lastName;
        lead.RepFirstName = firstName;

        if (string.IsNullOrWhiteSpace(lastName) &&
            string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            lead.IdRepresentative = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            lead.IdRepresentative = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            lead.IdRepresentative = null;
            return;
        }

        var repModel = new RepresentativeEditModel
        {
            IdRepresentative = lead.IdRepresentative ?? 0,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = lead.RepMiddleName,
            Phone = lead.ContactPhone,
            Email = lead.RepEmail,
            ResidenceAddress = lead.RepResidenceAddress,
            FallbackRelationTypeId = lead.RepRelationTypeId
        };

        if (repModel.IdRepresentative == 0 && !string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            var existingId = await studentRepository.FindRepresentativeIdByPhoneAsync(lead.ContactPhone);
            if (existingId.HasValue)
                repModel.IdRepresentative = existingId.Value;
        }

        lead.IdRepresentative = await studentRepository.SaveRepresentativeAsync(repModel);
        lead.ContactPhone = repModel.Phone.Trim();
        lead.RepresentativeFullName = $"{lead.RepLastName} {lead.RepFirstName} {lead.RepMiddleName}".Trim();
    }

    public async Task<IReadOnlyList<LeadChildItem>> GetChildrenAsync(
        int idLead,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        var result = new List<LeadChildItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(@"
SELECT id_lead_child, full_name, birth_date, phone, residence_address, sort_order
FROM public.lead_request_child
WHERE id_lead = @id
ORDER BY sort_order, id_lead_child;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LeadChildItem
            {
                IdLeadChild = reader.GetInt32(0),
                FullName = reader.GetString(1),
                BirthDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                ResidenceAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.GetInt16(5)
            });
        }

        return result;
    }

    public async Task SaveChildrenAsync(
        int idLead,
        IReadOnlyList<LeadChildItem> children,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM public.lead_request_child WHERE id_lead = @id;", connection, transaction))
        {
            deleteCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        short order = 0;
        foreach (var child in children.Where(c => !string.IsNullOrWhiteSpace(c.FullName)))
        {
            await using var insertCmd = new NpgsqlCommand(@"
INSERT INTO public.lead_request_child
    (id_lead, full_name, birth_date, phone, residence_address, sort_order)
VALUES (@lead, @name, @bd, @ph, @addr, @ord);", connection, transaction);
            insertCmd.Parameters.Add("lead", NpgsqlDbType.Integer).Value = idLead;
            insertCmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = child.FullName.Trim();
            insertCmd.Parameters.Add("bd", NpgsqlDbType.Date).Value =
                (object?)child.BirthDate ?? DBNull.Value;
            insertCmd.Parameters.Add("ph", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(child.Phone) ? DBNull.Value : child.Phone.Trim();
            insertCmd.Parameters.Add("addr", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(child.ResidenceAddress) ? DBNull.Value : child.ResidenceAddress.Trim();
            insertCmd.Parameters.Add("ord", NpgsqlDbType.Smallint).Value = order++;
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> SaveWithFamilyAsync(
        Lead lead,
        StudentRepository studentRepository,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        lead.ChildPhone = string.IsNullOrWhiteSpace(lead.ContactPhone) ? lead.ChildPhone : lead.ContactPhone;

        if (lead.IsAdultGuest)
            lead.IdInstitution = null;
        else
            lead.Workplace = null;

        int id;
        if (lead.IdLead <= 0)
        {
            id = await InsertAsync(lead, cancellationToken);
            lead.IdLead = id;
        }
        else
        {
            await UpdateAsync(lead, cancellationToken);
            id = lead.IdLead;
        }

        await SetLeadDirectionsAsync(id, lead.DirectionIds, cancellationToken);
        await UpsertStudentFromLeadAsync(lead, cancellationToken);

        return id;
    }

    public async Task<int?> GetLeadIdByStudentIdAsync(int idStudent, CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT id_source_lead FROM public.student WHERE id_student = @id", conn);
        cmd.Parameters.Add("@id", NpgsqlDbType.Integer).Value = idStudent;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value)
            return null;
        var id = Convert.ToInt32(value);
        return id > 0 ? id : null;
    }

    /// <summary>
    /// Подтягивает маму, папу и других взрослых из карточки гостя и привязывает их к обучающемуся,
    /// чтобы администратор мог выбрать родителя. Договор при этом остаётся на ребёнке.
    /// </summary>
    public async Task EnsureGuestCardParentsOnStudentAsync(
        int idStudent,
        StudentRepository students,
        CancellationToken cancellationToken = default)
    {
        var leadId = await GetLeadIdByStudentIdAsync(idStudent, cancellationToken);
        if (leadId is not > 0)
            return;

        var lead = await GetByIdAsync(leadId.Value, cancellationToken);
        if (lead == null)
            return;

        var existing = await students.GetStudentRepresentativesAsync(idStudent);
        var knownNames = new HashSet<string>(
            existing.Select(x => x.RepresentativeFullName.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var pending = new List<(string? Relation, string Name, string? Phone, int? RelationTypeId)>();
        void Consider(string? relation, string? name, string? phone, int? relationTypeId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            var trimmed = name.Trim();
            if (trimmed is "—" or "-")
                return;
            if (!knownNames.Add(trimmed))
                return;
            pending.Add((relation, trimmed, phone, relationTypeId));
        }

        int? adultCardRepresentativeId = null;
        if (!lead.IsAdultGuest)
        {
            Consider(lead.Parent1Relation, lead.Parent1FullName, lead.Parent1Phone, null);
            Consider(lead.Parent2Relation, lead.Parent2FullName, lead.Parent2Phone, null);
            Consider(lead.Parent3Relation, lead.Parent3FullName, lead.Parent3Phone, null);
        }
        else if (lead.IdRepresentative is > 0
                 && existing.All(x => x.IdRepresentative != lead.IdRepresentative.Value))
        {
            adultCardRepresentativeId = lead.IdRepresentative;
        }

        foreach (var relative in (await GetRelationsAsync(leadId.Value, cancellationToken)).Where(r => r.IsAdult))
            Consider(relative.RelationName, relative.FullName, relative.Phone, relative.RelationTypeId);

        if (pending.Count == 0 && adultCardRepresentativeId is not > 0)
            return;

        var relationTypes = await LoadRelationTypesAsync(cancellationToken);
        var markAsMain = existing.Count == 0;
        if (adultCardRepresentativeId is > 0)
        {
            await students.AttachRepresentativeAsync(new StudentRepresentativeEditModel
            {
                IdStudent = idStudent,
                IdRepresentative = adultCardRepresentativeId.Value,
                IdRelationType = lead.RepRelationTypeId is > 0
                    ? lead.RepRelationTypeId.Value
                    : ResolveRelationTypeId(relationTypes, "родитель"),
                IsLegalRepresentative = true,
                IsMainPayer = markAsMain
            });
            markAsMain = false;
        }

        if (pending.Count == 0)
            return;
        foreach (var parent in pending)
        {
            var fio = FioHelper.Parse(parent.Name);
            var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
            int? representativeId = null;
            if (!string.IsNullOrWhiteSpace(parent.Phone))
                representativeId = await students.FindRepresentativeIdByPhoneAsync(parent.Phone);
            if (representativeId is not > 0)
                representativeId = await students.FindRepresentativeIdByNameAsync(lastName, firstName);
            if (representativeId is not > 0)
            {
                representativeId = await students.SaveRepresentativeAsync(new RepresentativeEditModel
                {
                    LastName = lastName,
                    FirstName = firstName,
                    MiddleName = fio.MiddleName,
                    Phone = parent.Phone ?? string.Empty
                });
            }

            var relationTypeId = parent.RelationTypeId is > 0
                ? parent.RelationTypeId.Value
                : ResolveRelationTypeId(relationTypes, parent.Relation);
            await students.AttachRepresentativeAsync(new StudentRepresentativeEditModel
            {
                IdStudent = idStudent,
                IdRepresentative = representativeId.Value,
                IdRelationType = relationTypeId,
                IsLegalRepresentative = true,
                IsMainPayer = markAsMain
            });
            markAsMain = false;
        }
    }

    private static async Task<List<LookupItem>> LoadRelationTypesAsync(CancellationToken cancellationToken)
    {
        var list = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id_relation_type, relation_name FROM public.relation_type ORDER BY id_relation_type",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new LookupItem { Id = reader.GetInt32(0), Name = reader.GetString(1) });
        return list;
    }

    private static int ResolveRelationTypeId(IReadOnlyList<LookupItem> types, string? relation)
    {
        if (types.Count == 0)
            return 1;

        var name = (relation ?? string.Empty).Trim();
        var exact = types.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (exact != null)
            return exact.Id;

        LookupItem? Match(string fragment) =>
            types.FirstOrDefault(t => t.Name.Contains(fragment, StringComparison.CurrentCultureIgnoreCase));

        if (name.Contains("мам", StringComparison.CurrentCultureIgnoreCase)
            || name.Contains("мат", StringComparison.CurrentCultureIgnoreCase))
            return Match("мам")?.Id ?? types[0].Id;

        if (name.Contains("пап", StringComparison.CurrentCultureIgnoreCase)
            || name.Contains("отец", StringComparison.CurrentCultureIgnoreCase)
            || name.Contains("отц", StringComparison.CurrentCultureIgnoreCase))
            return Match("пап")?.Id ?? types[0].Id;

        return types.FirstOrDefault(t =>
                   string.Equals(t.Name, RelationTypeCatalog.Other, StringComparison.CurrentCultureIgnoreCase))
               ?.Id
               ?? types[0].Id;
    }

    private async Task UpsertStudentFromLeadAsync(Lead lead, CancellationToken cancellationToken)
    {
        if (lead.IdLead <= 0)
            return;

        var fio = FioHelper.Parse(lead.ChildFullName);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(
            string.IsNullOrWhiteSpace(fio.LastName) ? lead.ChildLastName : fio.LastName,
            string.IsNullOrWhiteSpace(fio.FirstName) ? lead.ChildFirstName : fio.FirstName);
        if (string.IsNullOrWhiteSpace(lastName))
            lastName = lead.ChildFirstName;
        var middle = string.IsNullOrWhiteSpace(fio.MiddleName) ? null : fio.MiddleName.Trim();
        var birth = lead.ChildBirthDate ?? DateTime.Today.AddYears(-7);
        var phone = string.IsNullOrWhiteSpace(lead.ContactPhone) ? lead.ChildPhone : lead.ContactPhone;

        await using var conn = Db.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        int studentTypeId = 1;
        await using (var typeCmd = new NpgsqlCommand(
                         "SELECT id_student_type FROM public.student_type ORDER BY id_student_type LIMIT 1", conn))
        {
            var typeObj = await typeCmd.ExecuteScalarAsync(cancellationToken);
            if (typeObj != null && typeObj != DBNull.Value)
                studentTypeId = Convert.ToInt32(typeObj);
        }

        int? existingId = null;
        await using (var findCmd = new NpgsqlCommand(
                         "SELECT id_student FROM public.student WHERE id_source_lead = @lead LIMIT 1", conn))
        {
            findCmd.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = lead.IdLead;
            var found = await findCmd.ExecuteScalarAsync(cancellationToken);
            if (found != null && found != DBNull.Value)
                existingId = Convert.ToInt32(found);
        }

        if (existingId is > 0)
        {
            await using var update = new NpgsqlCommand(@"
UPDATE public.student
SET last_name = @ln, first_name = @fn, middle_name = @mn, birth_date = @bd,
    phone = @ph, residence_address = @addr, id_institution = @inst, comment = @cm
WHERE id_student = @id", conn);
            update.Parameters.Add("@id", NpgsqlDbType.Integer).Value = existingId.Value;
            update.Parameters.Add("@ln", NpgsqlDbType.Varchar).Value = lastName;
            update.Parameters.Add("@fn", NpgsqlDbType.Varchar).Value = firstName;
            update.Parameters.Add("@mn", NpgsqlDbType.Varchar).Value = (object?)middle ?? DBNull.Value;
            update.Parameters.Add("@bd", NpgsqlDbType.Date).Value = birth.Date;
            update.Parameters.Add("@ph", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone.Trim();
            update.Parameters.Add("@addr", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(lead.ChildResidenceAddress) ? DBNull.Value : lead.ChildResidenceAddress.Trim();
            update.Parameters.Add("@inst", NpgsqlDbType.Integer).Value =
                lead.IdInstitution is > 0 ? lead.IdInstitution.Value : DBNull.Value;
            update.Parameters.Add("@cm", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(lead.Comment) ? DBNull.Value : lead.Comment.Trim();
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var insert = new NpgsqlCommand(@"
INSERT INTO public.student
    (last_name, first_name, middle_name, birth_date, phone, residence_address,
     status, id_student_type, arrival_date, id_institution, comment, id_source_lead)
VALUES (@ln, @fn, @mn, @bd, @ph, @addr, 'активный', @stype, CURRENT_DATE, @inst, @cm, @lead)
RETURNING id_student", conn);
        insert.Parameters.Add("@ln", NpgsqlDbType.Varchar).Value = lastName;
        insert.Parameters.Add("@fn", NpgsqlDbType.Varchar).Value = firstName;
        insert.Parameters.Add("@mn", NpgsqlDbType.Varchar).Value = (object?)middle ?? DBNull.Value;
        insert.Parameters.Add("@bd", NpgsqlDbType.Date).Value = birth.Date;
        insert.Parameters.Add("@ph", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(phone) ? DBNull.Value : phone.Trim();
        insert.Parameters.Add("@addr", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(lead.ChildResidenceAddress) ? DBNull.Value : lead.ChildResidenceAddress.Trim();
        insert.Parameters.Add("@stype", NpgsqlDbType.Integer).Value = studentTypeId;
        insert.Parameters.Add("@inst", NpgsqlDbType.Integer).Value =
            lead.IdInstitution is > 0 ? lead.IdInstitution.Value : DBNull.Value;
        insert.Parameters.Add("@cm", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(lead.Comment) ? DBNull.Value : lead.Comment.Trim();
        insert.Parameters.Add("@lead", NpgsqlDbType.Integer).Value = lead.IdLead;
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeadRelationItem>> GetRelationsAsync(
        int idLead,
        CancellationToken cancellationToken = default)
    {
        NotificationSchema.Ensure();
        var result = new List<LeadRelationItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(@"
SELECT
    lr.id_lead_relation,
    related.id_lead,
    related.child_first_name,
    related.child_last_name,
    related.child_birth_date,
    COALESCE(NULLIF(BTRIM(COALESCE(related.contact_phone, '')), ''), related.child_phone) AS phone,
    related.child_residence_address,
    related.id_institution,
    ei.institution_name,
    related.workplace,
    related.is_adult,
    lr.id_relation_type,
    COALESCE(rt.relation_name, '') AS relation_name,
    lr.is_main_payer,
    lr.is_legal_representative,
    lr.sort_order,
    FALSE AS is_incoming
FROM public.lead_relation lr
JOIN public.lead_request related ON related.id_lead = lr.id_related_lead
LEFT JOIN public.relation_type rt ON rt.id_relation_type = lr.id_relation_type
LEFT JOIN public.educational_institution ei ON ei.id_institution = related.id_institution
WHERE lr.id_lead = @id

UNION ALL

SELECT
    lr.id_lead_relation,
    owner.id_lead,
    owner.child_first_name,
    owner.child_last_name,
    owner.child_birth_date,
    COALESCE(NULLIF(BTRIM(COALESCE(owner.contact_phone, '')), ''), owner.child_phone),
    owner.child_residence_address,
    owner.id_institution,
    ei2.institution_name,
    owner.workplace,
    owner.is_adult,
    lr.id_relation_type,
    COALESCE(rt.relation_name, '') AS relation_name,
    lr.is_main_payer,
    lr.is_legal_representative,
    lr.sort_order,
    TRUE AS is_incoming
FROM public.lead_relation lr
JOIN public.lead_request owner ON owner.id_lead = lr.id_lead
LEFT JOIN public.relation_type rt ON rt.id_relation_type = lr.id_relation_type
LEFT JOIN public.educational_institution ei2 ON ei2.id_institution = owner.id_institution
WHERE lr.id_related_lead = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idLead;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var seen = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var relatedId = reader.GetInt32(1);
            if (relatedId == idLead || !seen.Add(relatedId))
                continue;

            var fio = FioHelper.Parse(FioHelper.FormatFromStored(
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
            var isIncoming = reader.GetBoolean(16);
            var storedRelation = reader.GetString(12);
            var birth = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
            result.Add(new LeadRelationItem
            {
                IdLeadRelation = reader.GetInt32(0),
                IdRelatedLead = relatedId,
                IsIncoming = isIncoming,
                LastName = fio.LastName,
                FirstName = fio.FirstName,
                MiddleName = fio.MiddleName,
                BirthDate = birth,
                Phone = reader.IsDBNull(5) ? null : reader.GetString(5),
                ResidenceAddress = reader.IsDBNull(6) ? null : reader.GetString(6),
                IdInstitution = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                InstitutionName = reader.IsDBNull(8) ? null : reader.GetString(8),
                Workplace = reader.IsDBNull(9) ? null : reader.GetString(9),
                RelationTypeId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                RelationName = isIncoming ? LeadGuestRules.InvertRelationName(storedRelation) : storedRelation,
                IsMainPayer = reader.GetBoolean(13),
                IsLegalRepresentative = reader.GetBoolean(14),
                SortOrder = reader.GetInt16(15),
                IsAdult = (!reader.IsDBNull(10) && reader.GetBoolean(10)) || LeadGuestRules.IsAdult(birth)
            });
        }

        return result;
    }

    private async Task SaveRelationsAsync(
        Lead lead,
        StudentRepository studentRepository,
        CancellationToken cancellationToken)
    {
        var keep = new List<(int From, int To)>();
        short order = 0;
        foreach (var item in lead.Relations)
        {
            if (string.IsNullOrWhiteSpace(item.FullName) && item.IdRelatedLead <= 0)
                continue;

            var relatedId = item.IdRelatedLead;
            if (relatedId <= 0)
            {
                var relatedLead = BuildRelatedLead(item);
                if (relatedLead.IsAdultGuest)
                    await UpsertSelfAsRepresentativeAsync(relatedLead, studentRepository, cancellationToken);
                relatedId = await InsertAsync(relatedLead, cancellationToken);
                item.IdRelatedLead = relatedId;
            }

            if (relatedId == lead.IdLead)
                continue;

            int fromId;
            int toId;
            bool payer = item.IsMainPayer;
            bool legal = item.IsLegalRepresentative;
            if (lead.IsAdultGuest && !item.IsAdult)
            {
                fromId = relatedId;
                toId = lead.IdLead;
            }
            else
            {
                fromId = lead.IdLead;
                toId = relatedId;
                if (lead.IsAdultGuest || item.IsAdult == false)
                {
                    payer = false;
                    legal = false;
                }
            }

            await UpsertRelationRowAsync(fromId, toId, item.RelationTypeId, payer, legal, order++, cancellationToken);
            keep.Add((fromId, toId));
        }

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var existingCmd = new NpgsqlCommand(@"
SELECT id_lead, id_related_lead FROM public.lead_relation
WHERE id_lead = @id OR id_related_lead = @id", connection);
        existingCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = lead.IdLead;
        var toDelete = new List<(int From, int To)>();
        await using (var reader = await existingCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var pair = (reader.GetInt32(0), reader.GetInt32(1));
                if (!keep.Contains(pair))
                    toDelete.Add(pair);
            }
        }

        foreach (var pair in toDelete)
        {
            await using var drop = new NpgsqlCommand(@"
DELETE FROM public.lead_relation
WHERE id_lead = @a AND id_related_lead = @b", connection);
            drop.Parameters.Add("a", NpgsqlDbType.Integer).Value = pair.From;
            drop.Parameters.Add("b", NpgsqlDbType.Integer).Value = pair.To;
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static Lead BuildRelatedLead(LeadRelationItem item)
    {
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(item.LastName, item.FirstName);
        var firstStored = string.IsNullOrWhiteSpace(item.MiddleName)
            ? firstName
            : $"{firstName} {item.MiddleName}".Trim();
        var phone = string.IsNullOrWhiteSpace(item.Phone) ? string.Empty : item.Phone.Trim();
        return new Lead
        {
            CreatedDate = DateTime.Now,
            ChildLastName = lastName,
            ChildFirstName = string.IsNullOrWhiteSpace(firstStored) ? lastName : firstStored,
            ChildBirthDate = item.BirthDate,
            ContactPhone = phone,
            ChildPhone = phone,
            ChildResidenceAddress = item.ResidenceAddress,
            IdInstitution = item.IsAdult ? null : item.IdInstitution,
            Workplace = item.IsAdult ? item.Workplace : null,
            IsAdultRecord = item.IsAdult,
            Status = "новая",
            ConsentPersonalData = false
        };
    }

    private static async Task UpsertRelationRowAsync(
        int fromId,
        int toId,
        int? relationTypeId,
        bool isMainPayer,
        bool isLegal,
        short sortOrder,
        CancellationToken cancellationToken)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO public.lead_relation
    (id_lead, id_related_lead, id_relation_type, is_main_payer, is_legal_representative, sort_order)
VALUES (@a, @b, @rel, @pay, @leg, @ord)
ON CONFLICT (id_lead, id_related_lead) DO UPDATE SET
    id_relation_type = EXCLUDED.id_relation_type,
    is_main_payer = EXCLUDED.is_main_payer,
    is_legal_representative = EXCLUDED.is_legal_representative,
    sort_order = EXCLUDED.sort_order;", connection);
        cmd.Parameters.Add("a", NpgsqlDbType.Integer).Value = fromId;
        cmd.Parameters.Add("b", NpgsqlDbType.Integer).Value = toId;
        cmd.Parameters.Add("rel", NpgsqlDbType.Integer).Value =
            relationTypeId is > 0 ? relationTypeId.Value : DBNull.Value;
        cmd.Parameters.Add("pay", NpgsqlDbType.Boolean).Value = isMainPayer;
        cmd.Parameters.Add("leg", NpgsqlDbType.Boolean).Value = isLegal;
        cmd.Parameters.Add("ord", NpgsqlDbType.Smallint).Value = sortOrder;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSelfAsRepresentativeAsync(
        Lead lead,
        StudentRepository studentRepository,
        CancellationToken cancellationToken)
    {
        var fio = FioHelper.Parse(lead.ChildFullName);
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(fio.LastName, fio.FirstName);
        if (string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(lead.ContactPhone))
            return;

        var repModel = new RepresentativeEditModel
        {
            IdRepresentative = lead.IdRepresentative ?? 0,
            LastName = lastName,
            FirstName = firstName,
            MiddleName = fio.MiddleName,
            Phone = lead.ContactPhone,
            ResidenceAddress = lead.ChildResidenceAddress,
            Workplace = lead.Workplace,
            FallbackRelationTypeId = lead.RepRelationTypeId
        };

        if (repModel.IdRepresentative == 0)
        {
            var existingId = await studentRepository.FindRepresentativeIdByPhoneAsync(lead.ContactPhone);
            if (existingId.HasValue)
                repModel.IdRepresentative = existingId.Value;
        }

        lead.IdRepresentative = await studentRepository.SaveRepresentativeAsync(repModel);
    }

    public static void SyncPrimaryChildFields(Lead lead)
    {
        if (lead.Children.Count == 0)
            return;

        var primary = lead.Children.OrderBy(c => c.SortOrder).First();
        lead.ChildFirstName = primary.FullName.Trim();
        lead.ChildLastName = null;
        lead.ChildBirthDate = primary.BirthDate;
        lead.ChildPhone = primary.Phone;
        lead.ChildResidenceAddress = primary.ResidenceAddress;
    }

    public static LeadChildItem ChildFromLegacyFields(Lead lead) =>
        new()
        {
            FullName = lead.ChildFullName,
            BirthDate = lead.ChildBirthDate,
            Phone = lead.ChildPhone,
            ResidenceAddress = lead.ChildResidenceAddress
        };
}
