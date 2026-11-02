#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class StudentRepository
{
    public async Task<IReadOnlyList<StudentListItem>> GetStudentsAsync(
        string? search = null,
        string? segment = null,
        bool onlyMinors = false,
        DateTime? arrivalFrom = null,
        DateTime? arrivalTo = null)
    {
        var result = new List<StudentListItem>();
        NotificationSchema.Ensure();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var sql = @"
WITH current_group AS (
    SELECT DISTINCT ON (gm.id_student)
        gm.id_student,
        d.direction_name || '-' || sg.group_number::text AS group_display
    FROM public.group_membership gm
    JOIN public.study_group sg ON sg.id_group = gm.id_group
    JOIN public.direction d ON d.id_direction = sg.id_direction
    WHERE gm.expulsion_date IS NULL
    ORDER BY gm.id_student, gm.enrollment_date DESC
), rep_summary AS (
    SELECT
        sr.id_student,
        COUNT(*)::int AS representative_count,
        STRING_AGG(
            TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')),
            ', ' ORDER BY sr.is_main_payer DESC, r.last_name, r.first_name
        ) AS representatives,
        MAX(CASE WHEN LOWER(rt.relation_name) LIKE '%мам%' THEN
            TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, ''))
            || COALESCE(' · ' || r.age::text || ' л.', '')
            || COALESCE(' · ' || NULLIF(r.phone, ''), '')
        END) AS mother_text,
        MAX(CASE WHEN LOWER(rt.relation_name) LIKE '%пап%' OR LOWER(rt.relation_name) LIKE '%отец%' THEN
            TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, ''))
            || COALESCE(' · ' || r.age::text || ' л.', '')
            || COALESCE(' · ' || NULLIF(r.phone, ''), '')
        END) AS father_text,
        MAX(CASE WHEN sr.is_main_payer THEN TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) END) AS main_payer
    FROM public.student_representative sr
    JOIN public.representative r ON r.id_representative = sr.id_representative
    LEFT JOIN public.relation_type rt ON rt.id_relation_type = sr.id_relation_type
    GROUP BY sr.id_student
), debt_summary AS (
    SELECT
        c.id_student,
        COALESCE(SUM(d.debt_amount - d.paid_amount), 0)::numeric(10,2) AS debt_amount
    FROM public.contract c
    JOIN public.debt d ON d.id_contract = c.id_contract
    -- Согласно CHECK debt_status_check значения: 'активна',
    -- 'частично_погашена', 'погашена', 'списана'.
    -- Долгом считаем непогашенные.
    WHERE d.status IN ('активна', 'частично_погашена')
    GROUP BY c.id_student
), attendance_summary AS (
    SELECT
        a.id_student,
        CASE WHEN COUNT(*) = 0 THEN NULL ELSE
            SUM(CASE WHEN LOWER(a.status) IN ('присутствовал', 'посетил', 'был', 'есть', 'да') THEN 1 ELSE 0 END)::numeric / COUNT(*)::numeric
        END AS attendance_percent
    FROM public.attendance a
    GROUP BY a.id_student
)
SELECT
    s.id_student,
    s.id_source_lead,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS full_name,
    s.birth_date,
    DATE_PART('year', AGE(CURRENT_DATE, s.birth_date))::int AS age,
    COALESCE(s.phone, '') AS phone,
    s.status,
    COALESCE(st.type_name, '') AS student_type,
    COALESCE(cg.group_display, '—') AS group_display,
    COALESCE(ds.debt_amount, 0)::numeric(10,2) AS debt_amount,
    ats.attendance_percent,
    COALESCE(rs.representative_count, 0) AS representative_count,
    COALESCE(rs.representatives, '—') AS representatives,
    COALESCE(rs.main_payer, '—') AS main_payer,
    s.arrival_date,
    COALESCE(s.residence_address, '') AS residence_address,
    COALESCE(s.comment, '') AS comment,
    COALESCE(s.school_number, '') AS school_number,
    COALESCE(ei.institution_name, '') AS institution_name,
    COALESCE(NULLIF(BTRIM(COALESCE(lr.mother_full_name, '')), ''), rs.mother_text, '—') AS mother_text,
    COALESCE(NULLIF(BTRIM(COALESCE(lr.father_full_name, '')), ''), rs.father_text, '—') AS father_text
FROM public.student s
LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
LEFT JOIN public.educational_institution ei ON ei.id_institution = s.id_institution
LEFT JOIN public.lead_request lr ON lr.id_lead = s.id_source_lead
LEFT JOIN current_group cg ON cg.id_student = s.id_student
LEFT JOIN rep_summary rs ON rs.id_student = s.id_student
LEFT JOIN debt_summary ds ON ds.id_student = s.id_student
LEFT JOIN attendance_summary ats ON ats.id_student = s.id_student
WHERE (@search IS NULL OR
       LOWER(TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, ''))) LIKE LOWER('%' || @search || '%') OR
       COALESCE(s.phone, '') LIKE '%' || @search || '%' OR
       COALESCE(rs.representatives, '') ILIKE '%' || @search || '%')
  AND (@segment IS NULL OR @segment = '' OR @segment = 'все'
       OR (@segment = 'действующие' AND s.status = 'активный'
           AND EXISTS (
               SELECT 1 FROM public.group_membership gm
               WHERE gm.id_student = s.id_student AND gm.expulsion_date IS NULL))
       OR (@segment = 'ушедшие' AND (
               EXISTS (
                   SELECT 1 FROM public.contract c
                   JOIN public.contract_termination ct ON ct.id_contract = c.id_contract
                   WHERE c.id_student = s.id_student)
               OR (s.status = 'архивный' AND NOT EXISTS (
                   SELECT 1 FROM public.group_membership gm
                   WHERE gm.id_student = s.id_student AND gm.expulsion_date IS NULL))))
       OR (@segment = 'приостановленные' AND s.status = 'приостановлен')
       OR (@segment = 'архивные' AND s.status = 'архивный'))
  AND (@only_minors = false OR DATE_PART('year', AGE(CURRENT_DATE, s.birth_date))::int < 18)
  AND (@arrival_from IS NULL OR s.arrival_date::date >= @arrival_from::date)
  AND (@arrival_to IS NULL OR s.arrival_date::date <= @arrival_to::date)
ORDER BY s.arrival_date DESC, s.last_name, s.first_name, s.middle_name;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();
        command.Parameters.Add("segment", NpgsqlDbType.Text).Value =
            (string.IsNullOrWhiteSpace(segment) || segment == "все") ? (object)DBNull.Value : segment.Trim();
        command.Parameters.Add("only_minors", NpgsqlDbType.Boolean).Value = onlyMinors;
        command.Parameters.Add("arrival_from", NpgsqlDbType.Date).Value = arrivalFrom.HasValue ? arrivalFrom.Value.Date : DBNull.Value;
        command.Parameters.Add("arrival_to", NpgsqlDbType.Date).Value = arrivalTo.HasValue ? arrivalTo.Value.Date : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var arrivalDate = ReadDateTime(reader, "arrival_date");
            result.Add(new StudentListItem
            {
                IdStudent = ReadInt(reader, "id_student"),
                IdLead = ReadNullableInt(reader, "id_source_lead"),
                FullName = ReadString(reader, "full_name"),
                BirthDate = ReadDateTime(reader, "birth_date"),
                Age = ReadInt(reader, "age"),
                Phone = ReadString(reader, "phone"),
                Status = ReadString(reader, "status"),
                StudentType = ReadString(reader, "student_type"),
                GroupDisplay = ReadString(reader, "group_display"),
                DebtAmount = ReadDecimal(reader, "debt_amount"),
                AttendancePercent = ReadNullableDecimal(reader, "attendance_percent"),
                RepresentativeCount = ReadInt(reader, "representative_count"),
                Representatives = ReadString(reader, "representatives"),
                MainPayer = ReadString(reader, "main_payer"),
                ArrivalDate = arrivalDate,
                ArrivalPeriodText = IntakePeriodHelper.GetLabel(arrivalDate),
                ResidenceAddress = ReadString(reader, "residence_address"),
                Comment = ReadString(reader, "comment"),
                SchoolNumber = ReadString(reader, "school_number"),
                InstitutionName = ReadString(reader, "institution_name"),
                MotherText = ReadString(reader, "mother_text"),
                FatherText = ReadString(reader, "father_text")
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<StudentListItem>> GetTrialProspectsAsync(string? search = null)
    {
        var result = new List<StudentListItem>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"
SELECT DISTINCT ON (lr.id_lead)
    lr.id_lead,
    TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, '')) AS child_name,
    TRIM(COALESCE(lr.client_last_name, '') || ' ' || COALESCE(lr.client_first_name, '')) AS client_name,
    COALESCE(lr.contact_phone, '') AS phone,
    COALESCE(d.direction_name, '—') AS direction_name,
    tl.scheduled_at,
    lr.child_birth_date
FROM public.lead_request lr
JOIN public.trial_lesson tl ON tl.id_lead = lr.id_lead
LEFT JOIN public.direction d ON d.id_direction = lr.id_direction
WHERE lr.status NOT IN ('конвертирована', 'отказ')
  AND (@search IS NULL OR
       LOWER(TRIM(COALESCE(lr.child_last_name, '') || ' ' || COALESCE(lr.child_first_name, ''))) LIKE LOWER('%' || @search || '%') OR
       COALESCE(lr.contact_phone, '') LIKE '%' || @search || '%' OR
       LOWER(TRIM(COALESCE(lr.client_last_name, '') || ' ' || COALESCE(lr.client_first_name, ''))) LIKE LOWER('%' || @search || '%'))
ORDER BY lr.id_lead, tl.scheduled_at DESC;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTime birthDate = default;
            var birthOrdinal = reader.GetOrdinal("child_birth_date");
            if (!reader.IsDBNull(birthOrdinal))
                birthDate = reader.GetDateTime(birthOrdinal);

            var trialDate = ReadDateTime(reader, "scheduled_at");
            var age = birthDate == default
                ? 0
                : (int)((DateTime.Today - birthDate.Date).TotalDays / 365.25);

            result.Add(new StudentListItem
            {
                IdStudent = 0,
                IdLead = ReadInt(reader, "id_lead"),
                FullName = ReadString(reader, "child_name"),
                BirthDate = birthDate,
                Age = age,
                Phone = ReadString(reader, "phone"),
                Status = "пробный",
                GroupDisplay = ReadString(reader, "direction_name"),
                Representatives = ReadString(reader, "client_name"),
                MainPayer = ReadString(reader, "client_name"),
                RepresentativeCount = 1,
                ArrivalDate = trialDate,
                ArrivalPeriodText = "пробное",
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<IntakePeriod>> GetIntakePeriodsAsync()
    {
        var dates = new List<DateTime>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT arrival_date FROM public.student WHERE arrival_date IS NOT NULL ORDER BY arrival_date DESC;",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dates.Add(ReadDateTime(reader, "arrival_date"));

        var fromDb = IntakePeriodHelper.BuildPeriodList(dates);
        if (fromDb.Count > 0)
            return fromDb;

        return IntakePeriodHelper.BuildRecentPeriods();
    }

    // Совместимость с простыми вызовами из старого кода.
    public Task<IReadOnlyList<StudentListItem>> GetStudentsAsync() => GetStudentsAsync(null, null, false);

    public async Task<StudentDetails> GetStudentDetailsAsync(int idStudent)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var details = new StudentDetails();

        await using (var command = new NpgsqlCommand(@"
SELECT
    s.id_student,
    s.last_name,
    s.first_name,
    s.middle_name,
    s.birth_date,
    s.phone,
    s.email,
    s.residence_address,
    s.birth_certificate,
    s.status,
    s.id_representative,
    s.id_institution,
    COALESCE(ei.institution_name, '—') AS institution_name,
    s.school_number,
    s.school_shift,
    s.id_student_type,
    COALESCE(st.type_name, '') AS student_type_name,
    s.arrival_date,
    s.created_at,
    s.comment,
    DATE_PART('year', AGE(CURRENT_DATE, s.birth_date))::int AS age
FROM public.student s
LEFT JOIN public.student_type st ON st.id_student_type = s.id_student_type
LEFT JOIN public.educational_institution ei ON ei.id_institution = s.id_institution
WHERE s.id_student = @id_student;", connection))
        {
            command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Обучающийся не найден.");

            details.Student = new Student
            {
                IdStudent = ReadInt(reader, "id_student"),
                LastName = ReadString(reader, "last_name"),
                FirstName = ReadString(reader, "first_name"),
                MiddleName = ReadNullableString(reader, "middle_name"),
                BirthDate = ReadDateTime(reader, "birth_date"),
                Phone = ReadNullableString(reader, "phone"),
                Email = ReadNullableString(reader, "email"),
                ResidenceAddress = ReadNullableString(reader, "residence_address"),
                BirthCertificate = ReadNullableString(reader, "birth_certificate"),
                Status = ReadString(reader, "status"),
                IdRepresentative = ReadNullableInt(reader, "id_representative"),
                IdInstitution = ReadNullableInt(reader, "id_institution"),
                SchoolNumber = ReadNullableString(reader, "school_number"),
                SchoolShift = ReadNullableShort(reader, "school_shift"),
                IdStudentType = ReadInt(reader, "id_student_type"),
                ArrivalDate = ReadDateTime(reader, "arrival_date"),
                CreatedAt = ReadDateTime(reader, "created_at"),
                Comment = ReadNullableString(reader, "comment")
            };
            details.StudentTypeName = ReadString(reader, "student_type_name");
            details.InstitutionName = ReadString(reader, "institution_name");
            details.Age = ReadInt(reader, "age");
        }

        foreach (var representative in await GetStudentRepresentativesAsync(idStudent, connection))
            details.Representatives.Add(representative);

        foreach (var contract in await GetStudentContractsAsync(idStudent, connection))
            details.Contracts.Add(contract);

        foreach (var contact in await GetStudentContactsAsync(idStudent, connection))
            details.Contacts.Add(contact);

        // Группы, в которых обучающийся состоит/состоял (с датами по договору)
        foreach (var grp in await GetStudentGroupsAsync(idStudent, connection))
            details.Groups.Add(grp);

        return details;
    }

    public async Task<IReadOnlyList<StudentRepresentativeLink>> GetStudentRepresentativesAsync(int idStudent)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        return await GetStudentRepresentativesAsync(idStudent, connection);
    }

    private static async Task<IReadOnlyList<StudentRepresentativeLink>> GetStudentRepresentativesAsync(int idStudent, NpgsqlConnection connection)
    {
        var result = new List<StudentRepresentativeLink>();
        await using var command = new NpgsqlCommand(@"
SELECT
    sr.id_student_representative,
    sr.id_student,
    sr.id_representative,
    TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS representative_full_name,
    r.phone,
    r.email,
    r.residence_address,
    sr.id_relation_type,
    rt.relation_name,
    sr.is_main_payer,
    sr.is_legal_representative,
    sr.note,
    r.age
FROM public.student_representative sr
JOIN public.representative r ON r.id_representative = sr.id_representative
JOIN public.relation_type rt ON rt.id_relation_type = sr.id_relation_type
WHERE sr.id_student = @id_student
ORDER BY sr.is_main_payer DESC, r.last_name, r.first_name;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new StudentRepresentativeLink
            {
                IdStudentRepresentative = ReadInt(reader, "id_student_representative"),
                IdStudent = ReadInt(reader, "id_student"),
                IdRepresentative = ReadInt(reader, "id_representative"),
                RepresentativeFullName = ReadString(reader, "representative_full_name"),
                Phone = ReadString(reader, "phone"),
                Email = ReadNullableString(reader, "email"),
                ResidenceAddress = ReadNullableString(reader, "residence_address"),
                IdRelationType = ReadInt(reader, "id_relation_type"),
                RelationName = ReadString(reader, "relation_name"),
                IsMainPayer = ReadBool(reader, "is_main_payer"),
                IsLegalRepresentative = ReadBool(reader, "is_legal_representative"),
                Note = ReadNullableString(reader, "note"),
                Age = reader.IsDBNull(reader.GetOrdinal("age")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("age")))
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<StudentContractInfo>> GetStudentContractsAsync(int idStudent, NpgsqlConnection connection)
    {
        var result = new List<StudentContractInfo>();
        await using var command = new NpgsqlCommand(@"
SELECT
    c.id_contract,
    c.contract_number,
    c.contract_date,
    d.direction_name,
    c.total_amount,
    c.status,
    c.end_date
FROM public.contract c
JOIN public.direction d ON d.id_direction = c.id_direction
WHERE c.id_student = @id_student
ORDER BY c.contract_date DESC, c.id_contract DESC;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new StudentContractInfo
            {
                IdContract = ReadInt(reader, "id_contract"),
                ContractNumber = ReadString(reader, "contract_number"),
                ContractDate = ReadDateTime(reader, "contract_date"),
                DirectionName = ReadString(reader, "direction_name"),
                TotalAmount = ReadDecimal(reader, "total_amount"),
                Status = ReadString(reader, "status"),
                EndDate = ReadDateTime(reader, "end_date")
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<ContactHistoryItem>> GetStudentContactsAsync(int idStudent, NpgsqlConnection connection)
    {
        FinanceSchema.Ensure();
        var result = new List<ContactHistoryItem>();

        await using var cmd = new NpgsqlCommand(@"
SELECT n.id_note, n.note_text, n.created_at,
       COALESCE(TRIM(u.last_name || ' ' || u.first_name), 'Администратор') AS author_name
FROM public.client_note n
LEFT JOIN public.app_user u ON u.id_user = n.id_created_by
WHERE n.entity_type = 'student' AND n.entity_id = @id
ORDER BY n.created_at DESC;", connection);
        cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idStudent;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ContactHistoryItem
            {
                IdContact = reader.GetInt32(0),
                ContactDate = reader.GetDateTime(2),
                Channel = "заметка",
                Reason = "—",
                Result = reader.GetString(3),
                ManagerName = reader.GetString(3),
                Comment = reader.GetString(1)
            });
        }

        return result;
    }

    public async Task<int> SaveStudentWithRepresentativesAsync(
        StudentEditModel student,
        IEnumerable<StudentRepresentativeEditModel> representatives)
    {
        if (string.IsNullOrWhiteSpace(student.LastName))
            student.LastName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(student.FirstName))
            student.FirstName = OptionalFieldDefaults.EmptyName;

        if (student.BirthDate > DateTime.Today)
            student.BirthDate = OptionalFieldDefaults.BirthDateOrDefault(null);

        if (student.IdStudentType <= 0)
            student.IdStudentType = OptionalFieldDefaults.StudentTypeOrDefault(0);

        var representativeList = new List<StudentRepresentativeEditModel>(representatives);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var mainRepresentativeId = GetMainRepresentativeId(representativeList);
            int idStudent;

            if (student.IdStudent == 0)
            {
                await using var command = new NpgsqlCommand(@"
INSERT INTO public.student
(last_name, first_name, middle_name, birth_date, phone, email, residence_address, birth_certificate, status, id_representative, id_institution,
 school_number, school_shift, id_student_type, arrival_date, comment)
VALUES
(@last_name, @first_name, @middle_name, @birth_date, @phone, @email, @residence_address, @birth_certificate, @status, @id_representative, @id_institution,
 @school_number, @school_shift, @id_student_type, @arrival_date, @comment)
RETURNING id_student;", connection, transaction);
                FillStudentParameters(command, student, mainRepresentativeId);
                idStudent = Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            else
            {
                await using var command = new NpgsqlCommand(@"
UPDATE public.student
SET last_name = @last_name,
    first_name = @first_name,
    middle_name = @middle_name,
    birth_date = @birth_date,
    phone = @phone,
    email = @email,
    residence_address = @residence_address,
    birth_certificate = @birth_certificate,
    status = @status,
    id_representative = @id_representative,
    id_institution = @id_institution,
    school_number = @school_number,
    school_shift = @school_shift,
    id_student_type = @id_student_type,
    arrival_date = @arrival_date,
    comment = @comment
WHERE id_student = @id_student;", connection, transaction);
                command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = student.IdStudent;
                FillStudentParameters(command, student, mainRepresentativeId);
                await command.ExecuteNonQueryAsync();
                idStudent = student.IdStudent;

                await using var clearCommand = new NpgsqlCommand(@"
DELETE FROM public.student_representative
WHERE id_student = @id_student;", connection, transaction);
                clearCommand.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
                await clearCommand.ExecuteNonQueryAsync();
            }

            foreach (var representative in representativeList)
            {
                representative.IdStudent = idStudent;
                await UpsertStudentRepresentativeAsync(representative, connection, transaction, updateStudentDeprecatedField: false);
            }

            await RefreshDeprecatedMainRepresentativeAsync(idStudent, connection, transaction);
            await transaction.CommitAsync();
            return idStudent;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> SaveStudentAsync(Student student)
    {
        var edit = new StudentEditModel
        {
            IdStudent = student.IdStudent,
            LastName = student.LastName,
            FirstName = student.FirstName,
            MiddleName = student.MiddleName,
            BirthDate = student.BirthDate,
            Phone = student.Phone,
            Email = student.Email,
            ResidenceAddress = student.ResidenceAddress,
            BirthCertificate = student.BirthCertificate,
            Status = student.Status,
            IdInstitution = student.IdInstitution,
            SchoolNumber = student.SchoolNumber,
            SchoolShift = student.SchoolShift,
            IdStudentType = student.IdStudentType,
            ArrivalDate = student.ArrivalDate,
            Comment = student.Comment
        };
        return await SaveStudentWithRepresentativesAsync(edit, Array.Empty<StudentRepresentativeEditModel>());
    }

    public async Task DeleteStudentAsync(int idStudent)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM public.student WHERE id_student = @id_student;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<RepresentativeListItem>> GetRepresentativesAsync(string? search = null)
    {
        var result = new List<RepresentativeListItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
WITH children AS (
    SELECT
        sr.id_representative,
        COUNT(*)::int AS children_count,
        SUM(CASE WHEN sr.is_main_payer THEN 1 ELSE 0 END)::int AS payer_count,
        SUM(CASE WHEN sr.is_legal_representative THEN 1 ELSE 0 END)::int AS legal_count,
        STRING_AGG(
            TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')),
            ', ' ORDER BY s.last_name, s.first_name
        ) AS children_names,
        STRING_AGG(
            CASE WHEN sr.is_main_payer THEN TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) END,
            ', ' ORDER BY s.last_name, s.first_name
        ) AS main_payer_for
    FROM public.student_representative sr
    JOIN public.student s ON s.id_student = sr.id_student
    GROUP BY sr.id_representative
)
SELECT
    r.id_representative,
    TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, '')) AS full_name,
    r.phone,
    r.email,
    r.age,
    COALESCE(r.workplace, '—') AS workplace,
    COALESCE(NULLIF(TRIM(r.email), ''), '—') AS email,
    COALESCE(c.children_count, 0) AS children_count,
    COALESCE(c.payer_count, 0) AS payer_count,
    COALESCE(c.legal_count, 0) AS legal_count,
    COALESCE(c.children_names, '—') AS children_names,
    COALESCE(c.main_payer_for, '—') AS main_payer_for
FROM public.representative r
LEFT JOIN children c ON c.id_representative = r.id_representative
WHERE (@search IS NULL OR
       LOWER(TRIM(r.last_name || ' ' || r.first_name || COALESCE(' ' || r.middle_name, ''))) LIKE LOWER('%' || @search || '%') OR
       r.phone LIKE '%' || @search || '%' OR
       COALESCE(r.email, '') ILIKE '%' || @search || '%' OR
       COALESCE(c.children_names, '') ILIKE '%' || @search || '%')
ORDER BY r.last_name, r.first_name;", connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new RepresentativeListItem
            {
                IdRepresentative = ReadInt(reader, "id_representative"),
                FullName = ReadString(reader, "full_name"),
                Phone = ReadString(reader, "phone"),
                Email = ReadString(reader, "email") is var email && email != "—" ? email : string.Empty,
                Age = ReadNullableInt(reader, "age"),
                Workplace = ReadString(reader, "workplace"),
                ChildrenCount = ReadInt(reader, "children_count"),
                PayerCount = ReadInt(reader, "payer_count"),
                LegalCount = ReadInt(reader, "legal_count"),
                ChildrenNames = ReadString(reader, "children_names"),
                MainPayerFor = ReadString(reader, "main_payer_for")
            });
        }

        return result;
    }

    public async Task<RepresentativeDetails> GetRepresentativeDetailsAsync(int idRepresentative)
    {
        var details = new RepresentativeDetails();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand(@"
SELECT
    id_representative,
    last_name,
    first_name,
    middle_name,
    phone,
    email,
    residence_address,
    age,
    workplace,
    is_main_payer,
    id_relation_type,
    created_at,
    comment
FROM public.representative
WHERE id_representative = @id_representative;", connection))
        {
            command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = idRepresentative;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Представитель не найден.");

            details.Representative = new Representative
            {
                IdRepresentative = ReadInt(reader, "id_representative"),
                LastName = ReadString(reader, "last_name"),
                FirstName = ReadString(reader, "first_name"),
                MiddleName = ReadNullableString(reader, "middle_name"),
                Phone = ReadString(reader, "phone"),
                Email = ReadNullableString(reader, "email"),
                ResidenceAddress = ReadNullableString(reader, "residence_address"),
                Age = ReadNullableInt(reader, "age"),
                Workplace = ReadNullableString(reader, "workplace"),
                IsMainPayerLegacy = ReadBool(reader, "is_main_payer"),
                IdRelationTypeLegacy = ReadNullableInt(reader, "id_relation_type"),
                CreatedAt = ReadDateTime(reader, "created_at"),
                Comment = ReadNullableString(reader, "comment")
            };
        }

        await using (var command = new NpgsqlCommand(@"
SELECT
    s.id_student,
    TRIM(s.last_name || ' ' || s.first_name || COALESCE(' ' || s.middle_name, '')) AS student_full_name,
    s.birth_date,
    DATE_PART('year', AGE(CURRENT_DATE, s.birth_date))::int AS age,
    rt.relation_name,
    sr.is_main_payer,
    sr.is_legal_representative,
    s.status
FROM public.student_representative sr
JOIN public.student s ON s.id_student = sr.id_student
JOIN public.relation_type rt ON rt.id_relation_type = sr.id_relation_type
WHERE sr.id_representative = @id_representative
ORDER BY s.last_name, s.first_name;", connection))
        {
            command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = idRepresentative;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                details.Children.Add(new RepresentativeChildItem
                {
                    IdStudent = ReadInt(reader, "id_student"),
                    StudentFullName = ReadString(reader, "student_full_name"),
                    BirthDate = ReadDateTime(reader, "birth_date"),
                    Age = ReadInt(reader, "age"),
                    RelationName = ReadString(reader, "relation_name"),
                    IsMainPayer = ReadBool(reader, "is_main_payer"),
                    IsLegalRepresentative = ReadBool(reader, "is_legal_representative"),
                    Status = ReadString(reader, "status")
                });
            }
        }

        return details;
    }

    public async Task<int?> FindRepresentativeIdByPhoneAsync(string phone)
    {
        var digits = NormalizePhoneDigits(phone);
        if (digits.Length < 10)
            return null;

        var last10 = digits.Length > 10 ? digits[^10..] : digits;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_representative
FROM public.representative
WHERE regexp_replace(phone, '[^0-9]', '', 'g') = @digits
   OR right(regexp_replace(phone, '[^0-9]', '', 'g'), 10) = @last10
ORDER BY id_representative
LIMIT 1;", connection);
        command.Parameters.Add("digits", NpgsqlDbType.Varchar).Value = digits;
        command.Parameters.Add("last10", NpgsqlDbType.Varchar).Value = last10;
        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public async Task<int?> FindRepresentativeIdByNameAsync(string lastName, string firstName)
    {
        if (string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(firstName))
            return null;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_representative
FROM public.representative
WHERE lower(trim(last_name)) = lower(trim(@ln))
  AND lower(trim(first_name)) = lower(trim(@fn))
ORDER BY id_representative
LIMIT 1;", connection);
        command.Parameters.Add("ln", NpgsqlDbType.Varchar).Value = lastName.Trim();
        command.Parameters.Add("fn", NpgsqlDbType.Varchar).Value = firstName.Trim();
        var result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public async Task<RepresentativeEditModel?> GetRepresentativeEditModelAsync(int idRepresentative)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_representative, last_name, first_name, middle_name, phone, email, residence_address, id_relation_type
FROM public.representative
WHERE id_representative = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idRepresentative;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new RepresentativeEditModel
        {
            IdRepresentative = ReadInt(reader, "id_representative"),
            LastName = ReadString(reader, "last_name"),
            FirstName = ReadString(reader, "first_name"),
            MiddleName = ReadNullableString(reader, "middle_name"),
            Phone = ReadString(reader, "phone"),
            Email = ReadNullableString(reader, "email"),
            ResidenceAddress = ReadNullableString(reader, "residence_address"),
            FallbackRelationTypeId = ReadNullableInt(reader, "id_relation_type")
        };
    }

    private static string NormalizePhoneDigits(string phone) =>
        new string(phone.Where(char.IsDigit).ToArray());

    public async Task<int> SaveRepresentativeAsync(RepresentativeEditModel representative)
    {
        var (lastName, firstName) = FioHelper.NormalizeStoredNames(representative.LastName, representative.FirstName);
        representative.LastName = lastName;
        representative.FirstName = firstName;
        if (string.IsNullOrWhiteSpace(lastName))
            representative.LastName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(representative.FirstName))
            representative.FirstName = OptionalFieldDefaults.EmptyName;
        if (string.IsNullOrWhiteSpace(representative.Phone))
            representative.Phone = OptionalFieldDefaults.EmptyPhone;

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (representative.IdRepresentative == 0)
        {
            await using var command = new NpgsqlCommand(@"
INSERT INTO public.representative
(last_name, first_name, middle_name, phone, email, residence_address, age, workplace, is_main_payer, id_relation_type, comment)
VALUES
(@last_name, @first_name, @middle_name, @phone, @email, @residence_address, @age, @workplace, false,
 COALESCE(@id_relation_type, (SELECT id_relation_type FROM public.relation_type ORDER BY id_relation_type LIMIT 1)),
 @comment)
RETURNING id_representative;", connection);
            FillRepresentativeParameters(command, representative);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        else
        {
            await using var command = new NpgsqlCommand(@"
UPDATE public.representative
SET last_name = @last_name,
    first_name = @first_name,
    middle_name = @middle_name,
    phone = @phone,
    email = @email,
    residence_address = @residence_address,
    age = @age,
    workplace = @workplace,
    comment = @comment
WHERE id_representative = @id_representative;", connection);
            command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = representative.IdRepresentative;
            FillRepresentativeParameters(command, representative);
            await command.ExecuteNonQueryAsync();
            return representative.IdRepresentative;
        }
    }

    public async Task AttachRepresentativeAsync(StudentRepresentativeEditModel link)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await UpsertStudentRepresentativeAsync(link, connection, transaction, updateStudentDeprecatedField: true);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task UpsertStudentRepresentativeAsync(
        StudentRepresentativeEditModel link,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool updateStudentDeprecatedField)
    {
        if (link.IdRelationType <= 0)
            link.IdRelationType = 1;
        if (link.IdStudent <= 0 || link.IdRepresentative <= 0)
            return;

        if (link.IsMainPayer)
        {
            await using var resetCommand = new NpgsqlCommand(@"
UPDATE public.student_representative
SET is_main_payer = false
WHERE id_student = @id_student;", connection, transaction);
            resetCommand.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = link.IdStudent;
            await resetCommand.ExecuteNonQueryAsync();
        }

        // Проверяем существует ли уже связь — без зависимости от UNIQUE-констрейнта.
        int? existingId = null;
        await using (var checkCommand = new NpgsqlCommand(@"
SELECT id_student_representative
FROM public.student_representative
WHERE id_student = @id_student AND id_representative = @id_representative
LIMIT 1;", connection, transaction))
        {
            checkCommand.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = link.IdStudent;
            checkCommand.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = link.IdRepresentative;
            var result = await checkCommand.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                existingId = Convert.ToInt32(result);
        }

        if (existingId.HasValue)
        {
            await using var updateCommand = new NpgsqlCommand(@"
UPDATE public.student_representative
SET id_relation_type = @id_relation_type,
    is_main_payer = @is_main_payer,
    is_legal_representative = @is_legal_representative,
    note = @note
WHERE id_student_representative = @id;", connection, transaction);
            updateCommand.Parameters.Add("id", NpgsqlDbType.Integer).Value = existingId.Value;
            updateCommand.Parameters.Add("id_relation_type", NpgsqlDbType.Integer).Value = link.IdRelationType;
            updateCommand.Parameters.Add("is_main_payer", NpgsqlDbType.Boolean).Value = link.IsMainPayer;
            updateCommand.Parameters.Add("is_legal_representative", NpgsqlDbType.Boolean).Value = link.IsLegalRepresentative;
            updateCommand.Parameters.Add("note", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(link.Note) ? (object)DBNull.Value : link.Note.Trim();
            await updateCommand.ExecuteNonQueryAsync();
        }
        else
        {
            await using var insertCommand = new NpgsqlCommand(@"
INSERT INTO public.student_representative
(id_student, id_representative, id_relation_type, is_main_payer, is_legal_representative, note)
VALUES
(@id_student, @id_representative, @id_relation_type, @is_main_payer, @is_legal_representative, @note);", connection, transaction);
            insertCommand.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = link.IdStudent;
            insertCommand.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = link.IdRepresentative;
            insertCommand.Parameters.Add("id_relation_type", NpgsqlDbType.Integer).Value = link.IdRelationType;
            insertCommand.Parameters.Add("is_main_payer", NpgsqlDbType.Boolean).Value = link.IsMainPayer;
            insertCommand.Parameters.Add("is_legal_representative", NpgsqlDbType.Boolean).Value = link.IsLegalRepresentative;
            insertCommand.Parameters.Add("note", NpgsqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(link.Note) ? (object)DBNull.Value : link.Note.Trim();
            await insertCommand.ExecuteNonQueryAsync();
        }

        if (updateStudentDeprecatedField)
            await RefreshDeprecatedMainRepresentativeAsync(link.IdStudent, connection, transaction);
    }

    public async Task RemoveRepresentativeLinkAsync(int idStudent, int idRepresentative)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = new NpgsqlCommand(@"
DELETE FROM public.student_representative
WHERE id_student = @id_student AND id_representative = @id_representative;", connection, transaction);
            command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
            command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value = idRepresentative;
            await command.ExecuteNonQueryAsync();
            await RefreshDeprecatedMainRepresentativeAsync(idStudent, connection, transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task RefreshDeprecatedMainRepresentativeAsync(int idStudent, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(@"
UPDATE public.student s
SET id_representative = sub.id_representative
FROM (
    SELECT id_student, id_representative
    FROM public.student_representative
    WHERE id_student = @id_student
    ORDER BY is_main_payer DESC, id_student_representative
    LIMIT 1
) sub
WHERE s.id_student = @id_student;

UPDATE public.student
SET id_representative = NULL
WHERE id_student = @id_student
  AND NOT EXISTS (SELECT 1 FROM public.student_representative WHERE id_student = @id_student);", connection, transaction);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LookupItem>> GetStudentTypesAsync() =>
        await GetLookupAsync("public.student_type", "id_student_type", "type_name", "type_name");

    public async Task<IReadOnlyList<LookupItem>> GetRelationTypesAsync()
    {
        DatabaseSchema.EnsureIfPossible();
        return await GetLookupAsync("public.relation_type", "id_relation_type", "relation_name", "relation_name");
    }

    public async Task<IReadOnlyList<LookupItem>> GetStreetsAsync()
    {
        DatabaseSchema.EnsureIfPossible();
        return await GetLookupAsync("public.street", "id_street", "street_name", "street_name");
    }

    public async Task<IReadOnlyList<LookupItem>> GetInstitutionsAsync() =>
        await GetLookupAsync("public.educational_institution", "id_institution", "institution_name", "institution_name");

    private static async Task<IReadOnlyList<LookupItem>> GetLookupAsync(string table, string idColumn, string nameColumn, string orderColumn)
    {
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT {idColumn} AS id, {nameColumn} AS name FROM {table} ORDER BY {orderColumn};", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LookupItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name"))
            });
        }
        return LookupDeduper.ByName(result);
    }

    private static int? GetMainRepresentativeId(IReadOnlyList<StudentRepresentativeEditModel> representatives)
    {
        if (representatives.Count == 0)
            return null;

        foreach (var item in representatives)
        {
            if (item.IsMainPayer)
                return item.IdRepresentative;
        }

        return representatives[0].IdRepresentative;
    }

    private static void FillStudentParameters(NpgsqlCommand command, StudentEditModel student, int? mainRepresentativeId)
    {
        command.Parameters.Add("last_name", NpgsqlDbType.Varchar).Value = student.LastName.Trim();
        command.Parameters.Add("first_name", NpgsqlDbType.Varchar).Value = student.FirstName.Trim();
        command.Parameters.Add("middle_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(student.MiddleName) ? (object)DBNull.Value : student.MiddleName.Trim();
        command.Parameters.Add("birth_date", NpgsqlDbType.Date).Value = student.BirthDate.Date;
        command.Parameters.Add("phone", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(student.Phone) ? (object)DBNull.Value : student.Phone.Trim();
        command.Parameters.Add("email", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(student.Email) ? (object)DBNull.Value : student.Email.Trim();
        command.Parameters.Add("residence_address", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(student.ResidenceAddress) ? (object)DBNull.Value : student.ResidenceAddress.Trim();
        command.Parameters.Add("birth_certificate", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(student.BirthCertificate) ? (object)DBNull.Value : student.BirthCertificate.Trim();
        command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = student.Status;
        command.Parameters.Add("id_representative", NpgsqlDbType.Integer).Value =
            mainRepresentativeId.HasValue ? (object)mainRepresentativeId.Value : DBNull.Value;
        command.Parameters.Add("id_institution", NpgsqlDbType.Integer).Value =
            student.IdInstitution.HasValue ? (object)student.IdInstitution.Value : DBNull.Value;
        command.Parameters.Add("school_number", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(student.SchoolNumber) ? (object)DBNull.Value : student.SchoolNumber.Trim();
        command.Parameters.Add("school_shift", NpgsqlDbType.Smallint).Value =
            student.SchoolShift.HasValue ? (object)student.SchoolShift.Value : DBNull.Value;
        command.Parameters.Add("id_student_type", NpgsqlDbType.Integer).Value = student.IdStudentType;
        command.Parameters.Add("arrival_date", NpgsqlDbType.Date).Value = student.ArrivalDate.Date;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(student.Comment) ? (object)DBNull.Value : student.Comment.Trim();
    }

    private static void FillRepresentativeParameters(NpgsqlCommand command, RepresentativeEditModel representative)
    {
        command.Parameters.Add("last_name", NpgsqlDbType.Varchar).Value = representative.LastName.Trim();
        command.Parameters.Add("first_name", NpgsqlDbType.Varchar).Value = representative.FirstName.Trim();
        command.Parameters.Add("middle_name", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(representative.MiddleName) ? (object)DBNull.Value : representative.MiddleName.Trim();
        command.Parameters.Add("phone", NpgsqlDbType.Varchar).Value = representative.Phone.Trim();
        command.Parameters.Add("email", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(representative.Email) ? (object)DBNull.Value : representative.Email.Trim();
        command.Parameters.Add("residence_address", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(representative.ResidenceAddress) ? (object)DBNull.Value : representative.ResidenceAddress.Trim();
        command.Parameters.Add("age", NpgsqlDbType.Integer).Value =
            representative.Age.HasValue ? (object)representative.Age.Value : DBNull.Value;
        command.Parameters.Add("workplace", NpgsqlDbType.Varchar).Value =
            string.IsNullOrWhiteSpace(representative.Workplace) ? (object)DBNull.Value : representative.Workplace.Trim();
        command.Parameters.Add("id_relation_type", NpgsqlDbType.Integer).Value =
            representative.FallbackRelationTypeId.HasValue ? (object)representative.FallbackRelationTypeId.Value : DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(representative.Comment) ? (object)DBNull.Value : representative.Comment.Trim();
    }

    // -----------------------------------------------------------------
    // ГРУППЫ ОБУЧАЮЩЕГОСЯ (для вкладки «Группы» в карточке)
    //   - period_start/period_end берём из договора, если он есть
    //     по этой группе у этого обучающегося, иначе фолбэк на
    //     enrollment_date / expulsion_date в самом group_membership.
    //   - сортируем активные сверху, потом по дате зачисления (новые сверху).
    // -----------------------------------------------------------------
    public async Task<IReadOnlyList<StudentGroupMembershipItem>> GetStudentGroupsAsync(int idStudent)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        return await GetStudentGroupsAsync(idStudent, connection);
    }

    private static async Task<IReadOnlyList<StudentGroupMembershipItem>> GetStudentGroupsAsync(int idStudent, NpgsqlConnection connection)
    {
        var result = new List<StudentGroupMembershipItem>();

        // DISTINCT ON по (id_student, id_group) даёт нам самый «свежий» договор
        // на эту группу — приоритет «активен», иначе по самой свежей дате.
        await using var command = new NpgsqlCommand(@"
WITH gc AS (
    SELECT DISTINCT ON (c.id_student, c.id_group)
        c.id_student,
        c.id_group,
        c.contract_number,
        c.start_date,
        c.end_date
    FROM public.contract c
    WHERE c.id_student = @id_student AND c.id_group IS NOT NULL
    ORDER BY c.id_student, c.id_group,
             CASE WHEN c.status = 'активен' THEN 0 ELSE 1 END,
             c.contract_date DESC,
             c.id_contract DESC
)
SELECT
    gm.id_membership,
    gm.id_group,
    sg.id_direction,
    d.direction_name                                                                AS direction_name,
    d.direction_name || '-' || sg.group_number::text                                AS group_display,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, ''))  AS teacher_name,
    gm.enrollment_date,
    gm.expulsion_date,
    gc.start_date                                                                   AS contract_start_date,
    gc.end_date                                                                     AS contract_end_date,
    COALESCE(gc.contract_number, '')                                                AS contract_number
FROM public.group_membership gm
JOIN public.study_group sg ON sg.id_group = gm.id_group
JOIN public.direction d    ON d.id_direction = sg.id_direction
JOIN public.teacher t      ON t.id_teacher  = sg.id_teacher
LEFT JOIN gc               ON gc.id_student = gm.id_student AND gc.id_group = gm.id_group
WHERE gm.id_student = @id_student
ORDER BY (gm.expulsion_date IS NULL) DESC,
         gm.enrollment_date DESC,
         gm.id_membership DESC;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new StudentGroupMembershipItem
            {
                IdMembership      = ReadInt(reader, "id_membership"),
                IdGroup           = ReadInt(reader, "id_group"),
                IdDirection       = ReadInt(reader, "id_direction"),
                DirectionName     = ReadString(reader, "direction_name"),
                GroupDisplay      = ReadString(reader, "group_display"),
                TeacherName       = ReadString(reader, "teacher_name"),
                EnrollmentDate    = ReadDateTime(reader, "enrollment_date"),
                ExpulsionDate     = ReadNullableDateTime(reader, "expulsion_date"),
                ContractStartDate = ReadNullableDateTime(reader, "contract_start_date"),
                ContractEndDate   = ReadNullableDateTime(reader, "contract_end_date"),
                ContractNumber    = ReadString(reader, "contract_number")
            });
        }

        return result;
    }

    // Группы-кандидаты для перевода. Показываем только активные группы;
    // желательно того же направления (фильтр сверху можно снять при необходимости),
    // и исключаем те, где обучающийся уже состоит сейчас.
    public async Task<IReadOnlyList<GroupTransferOption>> GetGroupTransferCandidatesAsync(
        int idStudent,
        int? idDirection = null,
        bool filterByAge = true)
    {
        var result = new List<GroupTransferOption>();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        int? studentAge = null;
        if (filterByAge)
        {
            await using var ageCmd = new NpgsqlCommand(@"
SELECT DATE_PART('year', AGE(CURRENT_DATE, birth_date))::int
FROM public.student WHERE id_student = @id;", connection);
            ageCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = idStudent;
            var ageObj = await ageCmd.ExecuteScalarAsync();
            if (ageObj != null && ageObj != DBNull.Value)
                studentAge = Convert.ToInt32(ageObj);
        }

        await using var command = new NpgsqlCommand(@"
SELECT
    sg.id_group,
    sg.id_direction,
    d.direction_name,
    d.direction_name || '-' || sg.group_number::text                                AS group_display,
    TRIM(t.last_name || ' ' || t.first_name || COALESCE(' ' || t.middle_name, ''))  AS teacher_name,
    sg.age_min,
    sg.age_max,
    sg.age_band_label,
    (SELECT COUNT(*)::int
       FROM public.group_membership gm2
      WHERE gm2.id_group = sg.id_group AND gm2.expulsion_date IS NULL)              AS members_count
FROM public.study_group sg
JOIN public.direction d ON d.id_direction = sg.id_direction
JOIN public.teacher t   ON t.id_teacher = sg.id_teacher
WHERE sg.is_active = true
  AND (@id_direction IS NULL OR sg.id_direction = @id_direction)
  AND NOT EXISTS (
        SELECT 1
          FROM public.group_membership gm
         WHERE gm.id_group = sg.id_group
           AND gm.id_student = @id_student
           AND gm.expulsion_date IS NULL
      )
  AND (
        @student_age IS NULL
        OR (
            (sg.age_min IS NULL OR @student_age >= sg.age_min)
            AND (sg.age_max IS NULL OR @student_age <= sg.age_max)
        )
      )
ORDER BY d.direction_name, sg.group_number;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        command.Parameters.Add("id_direction", NpgsqlDbType.Integer).Value =
            idDirection.HasValue && idDirection.Value > 0 ? idDirection.Value : DBNull.Value;
        command.Parameters.Add("student_age", NpgsqlDbType.Integer).Value =
            studentAge.HasValue ? studentAge.Value : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new GroupTransferOption
            {
                IdGroup       = ReadInt(reader, "id_group"),
                IdDirection   = ReadInt(reader, "id_direction"),
                DirectionName = ReadString(reader, "direction_name"),
                GroupDisplay  = ReadString(reader, "group_display"),
                TeacherName   = ReadString(reader, "teacher_name"),
                MembersCount  = ReadInt(reader, "members_count"),
                AgeMin        = ReadNullableShort(reader, "age_min"),
                AgeMax        = ReadNullableShort(reader, "age_max"),
                AgeBandLabel  = ReadNullableString(reader, "age_band_label")
            });
        }

        return result;
    }

    // Первичное зачисление обучающегося в группу (без закрытия предыдущей записи).
    public async Task EnrollStudentInGroupAsync(int idStudent, int idToGroup, DateTime enrollmentDate)
    {
        if (await HasActiveGroupMembershipAsync(idStudent, idToGroup))
            throw new InvalidOperationException("Обучающийся уже состоит в этой группе.");

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            await InsertGroupMembershipAsync(connection, tx, idStudent, idToGroup, enrollmentDate);
            await RebindActiveContractsToGroupAsync(connection, tx, idStudent, idFromGroup: null, idToGroup);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var teacherTo = await GetGroupTeacherIdAsync(idToGroup);
        await new AcademicHistoryRepository().LogStudentGroupEventAsync(
            idStudent,
            idGroupFrom: null,
            idGroupTo: idToGroup,
            idTeacherFrom: null,
            idTeacherTo: teacherTo,
            eventDate: enrollmentDate,
            idChangedBy: UserSession.CurrentUserId,
            eventType: "enroll");
    }

    // Перевод обучающегося в другую группу.
    //   1) у текущей активной записи в group_membership проставляем expulsion_date;
    //   2) создаём новую запись с enrollment_date в целевой группе;
    //   3) активный договор перепривязываем на новую группу (без смены суммы и условий).
    // Всё в одной транзакции — либо целиком, либо никак.
    public async Task TransferStudentToGroupAsync(
        int idStudent,
        int idFromGroup,
        int idToGroup,
        DateTime transferDate)
    {
        if (idFromGroup == idToGroup)
            throw new InvalidOperationException("Целевая группа совпадает с текущей.");

        var enrollmentDate = await GetActiveMembershipEnrollmentDateAsync(idStudent, idFromGroup);
        if (!enrollmentDate.HasValue)
            throw new InvalidOperationException("Активная запись о группе не найдена. Обновите карточку и попробуйте снова.");

        if (transferDate.Date < enrollmentDate.Value.Date)
            throw new InvalidOperationException(
                $"Дата перевода не может быть раньше даты зачисления в текущую группу ({enrollmentDate.Value:dd.MM.yyyy}).");

        await GroupsRepository.EnsureGroupHasCapacityAsync(idToGroup);

        int? teacherFrom = await GetGroupTeacherIdAsync(idFromGroup);
        int? teacherTo = await GetGroupTeacherIdAsync(idToGroup);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            // 1. Закрываем активную membership в исходной группе
            await using (var closeCmd = new NpgsqlCommand(@"
UPDATE public.group_membership
   SET expulsion_date = @transfer_date
 WHERE id_student = @id_student
   AND id_group   = @id_from_group
   AND expulsion_date IS NULL;", connection, tx))
            {
                closeCmd.Parameters.Add("transfer_date", NpgsqlDbType.Date).Value = transferDate.Date;
                closeCmd.Parameters.Add("id_student",    NpgsqlDbType.Integer).Value = idStudent;
                closeCmd.Parameters.Add("id_from_group", NpgsqlDbType.Integer).Value = idFromGroup;
                var closed = await closeCmd.ExecuteNonQueryAsync();
                if (closed == 0)
                    throw new InvalidOperationException("Не удалось закрыть текущую запись о группе.");
            }

            await InsertGroupMembershipAsync(connection, tx, idStudent, idToGroup, transferDate);
            await RebindActiveContractsToGroupAsync(connection, tx, idStudent, idFromGroup, idToGroup);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await new AcademicHistoryRepository().LogStudentTransferAsync(
            idStudent,
            idFromGroup,
            idToGroup,
            teacherFrom,
            teacherTo,
            transferDate,
            UserSession.CurrentUserId);
    }

    private static async Task<int?> GetGroupTeacherIdAsync(int idGroup)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id_teacher FROM public.study_group WHERE id_group = @id", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = idGroup;
        var value = await command.ExecuteScalarAsync();
        return value == null || value is DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<bool> HasActiveGroupMembershipAsync(int idStudent, int idGroup)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT 1
  FROM public.group_membership
 WHERE id_student = @id_student
   AND id_group   = @id_group
   AND expulsion_date IS NULL
 LIMIT 1;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        command.Parameters.Add("id_group",   NpgsqlDbType.Integer).Value = idGroup;
        return await command.ExecuteScalarAsync() != null;
    }

    private static async Task<DateTime?> GetActiveMembershipEnrollmentDateAsync(int idStudent, int idGroup)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT enrollment_date
  FROM public.group_membership
 WHERE id_student = @id_student
   AND id_group   = @id_group
   AND expulsion_date IS NULL
 ORDER BY enrollment_date DESC, id_membership DESC
 LIMIT 1;", connection);
        command.Parameters.Add("id_student", NpgsqlDbType.Integer).Value = idStudent;
        command.Parameters.Add("id_group",   NpgsqlDbType.Integer).Value = idGroup;
        var value = await command.ExecuteScalarAsync();
        return value == null || value is DBNull ? null : Convert.ToDateTime(value).Date;
    }

    private static async Task InsertGroupMembershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        int idStudent,
        int idToGroup,
        DateTime enrollmentDate)
    {
        // UNIQUE(id_group, id_student, enrollment_date) — при повторном переводе в тот же день
        // «оживляем» существующую запись, убирая expulsion_date.
        await using var insertCmd = new NpgsqlCommand(@"
INSERT INTO public.group_membership (id_group, id_student, enrollment_date, expulsion_date)
VALUES (@id_to_group, @id_student, @enrollment_date, NULL)
ON CONFLICT (id_group, id_student, enrollment_date)
DO UPDATE SET expulsion_date = NULL;", connection, tx);
        insertCmd.Parameters.Add("id_to_group",     NpgsqlDbType.Integer).Value = idToGroup;
        insertCmd.Parameters.Add("id_student",      NpgsqlDbType.Integer).Value = idStudent;
        insertCmd.Parameters.Add("enrollment_date", NpgsqlDbType.Date).Value    = enrollmentDate.Date;
        await insertCmd.ExecuteNonQueryAsync();
    }

    private static async Task RebindActiveContractsToGroupAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        int idStudent,
        int? idFromGroup,
        int idToGroup)
    {
        await using var directionCmd = new NpgsqlCommand(
            "SELECT id_direction FROM public.study_group WHERE id_group = @id_group;", connection, tx);
        directionCmd.Parameters.Add("id_group", NpgsqlDbType.Integer).Value = idToGroup;
        var directionObj = await directionCmd.ExecuteScalarAsync();
        if (directionObj == null || directionObj is DBNull)
            throw new InvalidOperationException("Целевая группа не найдена.");

        var idDirection = Convert.ToInt32(directionObj);

        // Перевод не создаёт новый договор и не меняет сумму — только привязку к группе.
        await using var contractCmd = new NpgsqlCommand(@"
UPDATE public.contract
   SET id_group     = @id_to_group,
       id_direction = @id_direction,
       id_branch    = (SELECT g.id_branch FROM public.study_group g WHERE g.id_group = @id_to_group),
       updated_at   = CURRENT_TIMESTAMP
 WHERE id_student = @id_student
   AND status     = 'активен'
   AND (
        id_group = @id_from_group
        OR (@id_from_group IS NOT NULL AND id_group IS NULL)
        OR (@id_from_group IS NULL AND id_group IS NULL)
       );", connection, tx);
        contractCmd.Parameters.Add("id_to_group",   NpgsqlDbType.Integer).Value = idToGroup;
        contractCmd.Parameters.Add("id_direction",  NpgsqlDbType.Integer).Value = idDirection;
        contractCmd.Parameters.Add("id_student",    NpgsqlDbType.Integer).Value = idStudent;
        contractCmd.Parameters.Add("id_from_group", NpgsqlDbType.Integer).Value =
            idFromGroup.HasValue ? (object)idFromGroup.Value : DBNull.Value;
        await contractCmd.ExecuteNonQueryAsync();
    }

    public static int CalculateAge(DateTime birthDate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age))
            age--;
        return age;
    }

    private static int ReadInt(IDataRecord reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    private static short? ReadNullableShort(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetInt16(index);
    }
    private static int? ReadNullableInt(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetInt32(index);
    }
    private static string ReadString(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
    }
    private static string? ReadNullableString(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }
    private static bool ReadBool(IDataRecord reader, string name) => reader.GetBoolean(reader.GetOrdinal(name));
    private static decimal ReadDecimal(IDataRecord reader, string name) => reader.GetDecimal(reader.GetOrdinal(name));
    private static decimal? ReadNullableDecimal(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        return reader.IsDBNull(index) ? null : reader.GetDecimal(index);
    }
    private static DateTime ReadDateTime(IDataRecord reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            DateTime dateTime => dateTime,
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(value)
        };
    }
    private static DateTime? ReadNullableDateTime(IDataRecord reader, string name)
    {
        var index = reader.GetOrdinal(name);
        if (reader.IsDBNull(index))
            return null;
        var value = reader.GetValue(index);
        return value switch
        {
            DateTime dateTime => dateTime,
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(value)
        };
    }
}
