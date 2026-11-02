#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

/// <summary>
/// Универсальная запись справочника. Дополнительные поля кладутся в Extra.
/// </summary>
public sealed class DictionaryEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }   // например, тип учреждения, или цена
    public bool? IsActive { get; set; }     // null если у таблицы нет такого поля
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public enum DictionaryKind
{
    Direction,                 // направление обучения
    Classroom,                 // кабинет
    EducationalInstitution,    // учебное заведение
    RelationType,              // тип родства
    StudentType,               // тип обучающегося
    SourceOfArrival,           // источник заявки
    PaymentMethod,             // способ оплаты
    ServiceType,               // тип услуги (оплаты)
    ExpenseType,               // категория расхода
    ShopProduct,               // товар магазина
    CoursePackage,             // курсы / длительность договора
    EventType,                 // тип события обучающегося
    SatisfactionLevel,         // оценка удовлетворённости занятиями
    DissatisfactionReason,     // причина недовольства
    Role,                      // роль пользователя
    StudyBranch,               // филиалы
    AcademicYear,              // учебные годы
    Street,                    // улицы
    School,                    // школы
    Kindergarten,              // детские сады
    Manager                    // менеджеры (учётные записи)
}

public static class DictionaryKindInfo
{
    public static string Title(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction => "Направления",
        DictionaryKind.Classroom => "Кабинеты",
        DictionaryKind.EducationalInstitution => "Учебные учреждения",
        DictionaryKind.RelationType => "Типы родства",
        DictionaryKind.StudentType => "Типы обучающихся",
        DictionaryKind.SourceOfArrival => "Источники заявок",
        DictionaryKind.PaymentMethod => "Способы оплаты",
        DictionaryKind.ServiceType => "Типы услуг",
        DictionaryKind.ExpenseType => "Категории расходов",
        DictionaryKind.ShopProduct => "Товары магазина",
        DictionaryKind.CoursePackage => "Курсы",
        DictionaryKind.EventType => "Типы событий",
        DictionaryKind.SatisfactionLevel => "Оценки удовлетворённости",
        DictionaryKind.DissatisfactionReason => "Причины недовольства",
        DictionaryKind.Role => "Роли пользователей",
        DictionaryKind.StudyBranch => "Филиалы",
        DictionaryKind.AcademicYear => "Учебные годы",
        DictionaryKind.Street => "Улицы",
        DictionaryKind.School => "Школы",
        DictionaryKind.Kindergarten => "Детские сады",
        DictionaryKind.Manager => "Менеджеры",
        _ => "Справочник"
    };
    public static string Subtitle(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction => "ИЗО, музыка и другие. Кнопка «Добавить направление» — новое название и цена",
        DictionaryKind.Classroom => "Учебные помещения и их вместимость",
        DictionaryKind.EducationalInstitution => "Школы и сады в одном списке. Импорт из общего Excel (признак Ш / Д)",
        DictionaryKind.RelationType => "Мама, папа и «Другой» (бабушка, дедушка — в договоре как «Другой»)",
        DictionaryKind.StudentType => "Дошкольник, школьник, взрослый и т.д.",
        DictionaryKind.SourceOfArrival => "Откуда узнали о центре (карточки гостей)",
        DictionaryKind.PaymentMethod => "Наличные, карта, перевод и т.д.",
        DictionaryKind.ServiceType => "Абонемент, разовое занятие и др.",
        DictionaryKind.ExpenseType => "Статьи расходов центра",
        DictionaryKind.ShopProduct => "Каталог товаров для продаж",
        DictionaryKind.CoursePackage => "Направление, длительность, занятия — сумма в договоре",
        DictionaryKind.EventType => "Уход, перевод, приостановка и др.",
        DictionaryKind.SatisfactionLevel => "Нравится / не нравится — для ежемесячных опросов",
        DictionaryKind.DissatisfactionReason => "Причины, если ребёнку не нравится заниматься",
        DictionaryKind.Role => "Админ, директор, старший менеджер, менеджер, преподаватель",
        DictionaryKind.StudyBranch => "Центральная 1, Школьная 5, Парковая 10, Садовая 3 — можно добавить",
        DictionaryKind.AcademicYear => "Учебный год с 1 августа по 31 июля",
        DictionaryKind.Street => "Импорт из Excel: название улицы (если в файле номер и название — берётся название)",
        DictionaryKind.School => "Импорт из Excel школ и садов: столбец «признак» Ш, «наименование». Повторная загрузка обновляет список",
        DictionaryKind.Kindergarten => "Импорт из Excel школ и садов: столбец «признак» Д, «наименование». Повторная загрузка обновляет список",
        DictionaryKind.Manager => "Сотрудники с ролью менеджер или старший менеджер",
        _ => string.Empty
    };
    public static string Icon(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction => "🎨",
        DictionaryKind.Classroom => "🏫",
        DictionaryKind.EducationalInstitution => "🎓",
        DictionaryKind.RelationType => "👨‍👩‍👧",
        DictionaryKind.StudentType => "🧒",
        DictionaryKind.SourceOfArrival => "📣",
        DictionaryKind.PaymentMethod => "💳",
        DictionaryKind.ServiceType => "📋",
        DictionaryKind.ExpenseType => "💸",
        DictionaryKind.ShopProduct => "🛒",
        DictionaryKind.CoursePackage => "📅",
        DictionaryKind.EventType => "📌",
        DictionaryKind.SatisfactionLevel => "😊",
        DictionaryKind.DissatisfactionReason => "😕",
        DictionaryKind.Role => "🔐",
        DictionaryKind.StudyBranch => "📍",
        DictionaryKind.AcademicYear => "📅",
        DictionaryKind.Street => "🛣",
        DictionaryKind.School => "🏫",
        DictionaryKind.Kindergarten => "🧸",
        DictionaryKind.Manager => "👤",
        _ => "📁"
    };
    public static string Color(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction => VKR.Resources.ThemeColors.DangerBg,
        DictionaryKind.Classroom => VKR.Resources.ThemeColors.InfoBg,
        DictionaryKind.EducationalInstitution => VKR.Resources.ThemeColors.SelectedRow,
        DictionaryKind.RelationType => VKR.Resources.ThemeColors.WarningBg,
        DictionaryKind.StudentType => VKR.Resources.ThemeColors.SuccessBg,
        DictionaryKind.SourceOfArrival => VKR.Resources.ThemeColors.InfoBg,
        DictionaryKind.PaymentMethod => VKR.Resources.ThemeColors.SuccessBg,
        DictionaryKind.ServiceType => VKR.Resources.ThemeColors.WarningBg,
        DictionaryKind.ExpenseType => VKR.Resources.ThemeColors.DangerBg,
        DictionaryKind.ShopProduct => VKR.Resources.ThemeColors.SelectedRow,
        DictionaryKind.CoursePackage => VKR.Resources.ThemeColors.InfoBg,
        DictionaryKind.EventType => VKR.Resources.ThemeColors.HoverSurface,
        DictionaryKind.SatisfactionLevel => VKR.Resources.ThemeColors.SuccessBg,
        DictionaryKind.DissatisfactionReason => VKR.Resources.ThemeColors.WarningBg,
        DictionaryKind.Role => VKR.Resources.ThemeColors.InfoBg,
        DictionaryKind.StudyBranch => VKR.Resources.ThemeColors.WarningBg,
        DictionaryKind.AcademicYear => VKR.Resources.ThemeColors.InfoBg,
        DictionaryKind.Street => VKR.Resources.ThemeColors.HoverSurface,
        DictionaryKind.School => VKR.Resources.ThemeColors.SelectedRow,
        DictionaryKind.Kindergarten => VKR.Resources.ThemeColors.SuccessBg,
        DictionaryKind.Manager => VKR.Resources.ThemeColors.InfoBg,
        _ => VKR.Resources.ThemeColors.HoverSurface
    };
}

public sealed class DictionariesRepository
{
    public async Task<IReadOnlyList<LookupItem>> GetRootDirectionsAsync(int? excludeId = null)
    {
        AcademicSchema.EnsureDirectionColumns();
        var result = new List<LookupItem>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id_direction, direction_name
FROM public.direction
WHERE is_active = true
  AND parent_id_direction IS NULL
  AND (@exclude IS NULL OR id_direction <> @exclude)
ORDER BY direction_name;", connection);
        // AddWithValue(DBNull) уходит в PostgreSQL без типа, и сравнение с id_direction падает
        // сразу при открытии «Новое направление» (exclude = null).
        command.Parameters.Add(new NpgsqlParameter("exclude", NpgsqlDbType.Integer)
        {
            Value = excludeId is > 0 ? excludeId.Value : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new LookupItem { Id = reader.GetInt32(0), Name = reader.GetString(1) });
        return result;
    }

    public async Task<IReadOnlyList<DictionaryEntry>> GetEntriesAsync(DictionaryKind kind)
    {
        DatabaseSchema.EnsureIfPossible();
        if (kind == DictionaryKind.Direction)
            AcademicSchema.EnsureDirectionColumns();
        if (kind == DictionaryKind.CoursePackage)
            FinanceSchema.Ensure();

        var result = new List<DictionaryEntry>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(GetSelectSql(kind), connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entry = new DictionaryEntry
            {
                Id = Convert.ToInt32(reader.GetValue(0)),
                Name = reader.GetString(1)
            };
            switch (kind)
            {
                case DictionaryKind.Direction:
                    {
                        var directionPrice = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2));
                        entry.IsActive = reader.GetBoolean(3);
                        entry.Extra["price"] = directionPrice;
                        entry.Extra["description"] = reader.IsDBNull(4) ? null : reader.GetString(4);
                        int? parentId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                        entry.Extra["parent_id_direction"] = parentId;
                        entry.Extra["sort_order"] = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6);
                        var parentName = reader.IsDBNull(7) ? null : reader.GetString(7);
                        entry.Subtitle = parentName != null
                            ? $"{parentName} → {entry.Name} · {directionPrice:N0} ₽"
                            : $"{directionPrice:N0} ₽ за занятие · корневое";
                        break;
                    }
                case DictionaryKind.Classroom:
                    {
                        var capacity = reader.GetInt32(2);
                        entry.Subtitle = $"вместимость: {capacity}";
                        entry.Extra["capacity"] = capacity;
                        entry.Extra["description"] = reader.IsDBNull(3) ? null : reader.GetString(3);
                        var idBranch = reader.IsDBNull(4) ? 1 : reader.GetInt16(4);
                        entry.Extra["id_branch"] = (int)idBranch;
                        var branch = StudyBranch.Find(idBranch);
                        if (branch != null)
                            entry.Subtitle += $" · {branch.ShortName}";
                        break;
                    }
                case DictionaryKind.EducationalInstitution:
                case DictionaryKind.School:
                case DictionaryKind.Kindergarten:
                    entry.Subtitle = reader.GetString(2);
                    entry.Extra["institution_type"] = reader.GetString(2);
                    var instNumber = reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetString(3) : "";
                    entry.Extra["institution_number"] = instNumber;
                    if (!string.IsNullOrWhiteSpace(instNumber))
                        entry.Subtitle += $" · № {instNumber}";
                    break;
                case DictionaryKind.StudentType:
                    entry.Subtitle = $"код: {reader.GetString(2)}";
                    entry.Extra["type_code"] = reader.GetString(2);
                    break;
                case DictionaryKind.PaymentMethod:
                    entry.Subtitle = $"код: {reader.GetString(2)}";
                    entry.Extra["method_code"] = reader.GetString(2);
                    break;
                case DictionaryKind.ServiceType:
                    entry.Subtitle = $"код: {reader.GetString(2)}";
                    entry.Extra["service_code"] = reader.GetString(2);
                    break;
                case DictionaryKind.ShopProduct:
                    {
                        var productPrice = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2));
                        var stock = reader.GetInt32(3);
                        entry.IsActive = reader.GetBoolean(4);
                        entry.Subtitle = $"{productPrice:N0} ₽ · остаток: {stock}";
                        entry.Extra["price"] = productPrice;
                        entry.Extra["stock_qty"] = stock;
                        break;
                    }
                case DictionaryKind.CoursePackage:
                    {
                        var duration = reader.GetDecimal(2);
                        var lessons = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                        var timesPerWeek = reader.GetInt16(4);
                        entry.IsActive = reader.GetBoolean(6);
                        var directionName = reader.GetString(7);
                        var idDirection = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
                        entry.Subtitle = ContractDurationHelper.FormatDuration(duration) +
                            (lessons.HasValue ? $" · {lessons} зан." : "") +
                            $" · {timesPerWeek} раз/нед." +
                            (string.IsNullOrWhiteSpace(directionName) ? "" : $" · {directionName}");
                        entry.Extra["duration_months"] = duration;
                        entry.Extra["lessons_count"] = lessons;
                        entry.Extra["times_per_week"] = timesPerWeek;
                        entry.Extra["id_direction"] = idDirection;
                        break;
                    }
                case DictionaryKind.Role:
                    entry.Subtitle = $"код: {reader.GetString(2)}";
                    entry.Extra["role_code"] = reader.GetString(2);
                    entry.Extra["description"] = reader.IsDBNull(3) ? null : reader.GetString(3);
                    break;
                case DictionaryKind.SatisfactionLevel:
                    entry.Subtitle = $"код: {reader.GetString(2)}";
                    entry.IsActive = reader.GetBoolean(4);
                    entry.Extra["level_code"] = reader.GetString(2);
                    entry.Extra["is_positive"] = reader.GetBoolean(3);
                    entry.Extra["sort_order"] = reader.GetInt16(5);
                    break;
                case DictionaryKind.DissatisfactionReason:
                    entry.IsActive = reader.GetBoolean(2);
                    entry.Extra["sort_order"] = reader.GetInt16(3);
                    break;
                case DictionaryKind.StudyBranch:
                    entry.Subtitle = reader.GetString(2);
                    entry.IsActive = reader.GetBoolean(4);
                    entry.Extra["address_line"] = reader.GetString(2);
                    entry.Extra["button_label"] = reader.GetString(3);
                    break;
                case DictionaryKind.AcademicYear:
                    entry.Subtitle = $"старт: {reader.GetInt16(2)}";
                    entry.IsActive = reader.GetBoolean(3);
                    entry.Extra["start_year"] = (int)reader.GetInt16(2);
                    break;
                case DictionaryKind.Street:
                    break;
                case DictionaryKind.Manager:
                    entry.Subtitle = reader.GetString(2);
                    entry.IsActive = reader.GetBoolean(3);
                    entry.Extra["email"] = reader.IsDBNull(4) ? "" : reader.GetString(4);
                    break;
            }
            result.Add(entry);
        }
        return result;
    }

    public async Task<int> SaveEntryAsync(DictionaryKind kind, DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
            entry.Name = OptionalFieldDefaults.DictionaryNameOrDefault(null);

        if (kind == DictionaryKind.CoursePackage)
            FinanceSchema.Ensure();
        if (kind is DictionaryKind.StudyBranch or DictionaryKind.AcademicYear or DictionaryKind.Street
            or DictionaryKind.School or DictionaryKind.Kindergarten or DictionaryKind.EducationalInstitution)
            DatabaseSchema.EnsureIfPossible();

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (entry.Id == 0 && kind is DictionaryKind.EducationalInstitution
            or DictionaryKind.School or DictionaryKind.Kindergarten)
        {
            await using var find = new NpgsqlCommand(@"
SELECT id_institution
FROM public.educational_institution
WHERE LOWER(TRIM(institution_name)) = LOWER(TRIM(@name))
LIMIT 1;", connection);
            find.Parameters.Add("name", NpgsqlDbType.Varchar).Value = entry.Name.Trim();
            var existing = await find.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                entry.Id = Convert.ToInt32(existing);
        }

        var (sql, fillParams) = GetSaveSql(kind, entry);
        await using var command = new NpgsqlCommand(sql, connection);
        fillParams(command);

        if (entry.Id == 0)
        {
            var id = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (kind == DictionaryKind.StudyBranch)
                DatabaseSchema.ReloadStudyBranches();
            return id;
        }

        await command.ExecuteNonQueryAsync();
        if (kind == DictionaryKind.StudyBranch)
            DatabaseSchema.ReloadStudyBranches();
        return entry.Id;
    }

    public async Task DeleteEntryAsync(DictionaryKind kind, int id)
    {
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        if (kind == DictionaryKind.StudyBranch)
        {
            await using var deactivateCmd = new NpgsqlCommand(@"
UPDATE public.study_branch SET is_active = false WHERE id_branch = @id;", connection);
            deactivateCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
            await deactivateCmd.ExecuteNonQueryAsync();
            DatabaseSchema.ReloadStudyBranches();
            return;
        }

        if (kind == DictionaryKind.Manager)
        {
            await using var deactivateCmd = new NpgsqlCommand(@"
UPDATE public.app_user SET is_active = false WHERE id_user = @id;", connection);
            deactivateCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
            await deactivateCmd.ExecuteNonQueryAsync();
            return;
        }
        if (kind == DictionaryKind.Direction)
        {
            await using var deactivateCmd = new NpgsqlCommand(@"
UPDATE public.direction
SET is_active = false
WHERE id_direction = @id;", connection);
            deactivateCmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
            await deactivateCmd.ExecuteNonQueryAsync();
            return;
        }

        if (kind == DictionaryKind.StudentType)
        {
            await using var check = new NpgsqlCommand(@"
SELECT CASE
    WHEN EXISTS (SELECT 1 FROM public.contract WHERE id_student_type = @id) THEN 'contract'
    WHEN EXISTS (SELECT 1 FROM public.student WHERE id_student_type = @id) THEN 'student'
    ELSE NULL
END;", connection);
            check.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
            var blocker = await check.ExecuteScalarAsync() as string;
            if (blocker != null)
                throw new InvalidOperationException(
                    blocker == "contract"
                        ? "Тип используется в договорах. Измените название или код, удаление невозможно."
                        : "Тип используется у обучающихся. Удаление невозможно.");
        }

        var (table, idColumn) = GetTableInfo(kind);
        await using var command = new NpgsqlCommand(
            $"DELETE FROM public.{table} WHERE {idColumn} = @id;", connection);
        command.Parameters.Add("id", NpgsqlDbType.Integer).Value = id;
        await command.ExecuteNonQueryAsync();
    }

    private static (string table, string idColumn) GetTableInfo(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction => ("direction", "id_direction"),
        DictionaryKind.Classroom => ("classroom", "id_classroom"),
        DictionaryKind.EducationalInstitution => ("educational_institution", "id_institution"),
        DictionaryKind.RelationType => ("relation_type", "id_relation_type"),
        DictionaryKind.StudentType => ("student_type", "id_student_type"),
        DictionaryKind.SourceOfArrival => ("source_of_arrival", "id_source"),
        DictionaryKind.PaymentMethod => ("payment_method", "id_payment_method"),
        DictionaryKind.ServiceType => ("service_type", "id_service_type"),
        DictionaryKind.ExpenseType => ("expense_type", "id_expense_type"),
        DictionaryKind.ShopProduct => ("shop_product", "id_product"),
        DictionaryKind.CoursePackage => ("course_package", "id_course_package"),
        DictionaryKind.EventType => ("event_type", "id_event_type"),
        DictionaryKind.SatisfactionLevel => ("satisfaction_level", "id_satisfaction_level"),
        DictionaryKind.DissatisfactionReason => ("dissatisfaction_reason", "id_reason"),
        DictionaryKind.Role => ("role", "id_role"),
        DictionaryKind.StudyBranch => ("study_branch", "id_branch"),
        DictionaryKind.AcademicYear => ("academic_year", "id_academic_year"),
        DictionaryKind.Street => ("street", "id_street"),
        DictionaryKind.School => ("educational_institution", "id_institution"),
        DictionaryKind.Kindergarten => ("educational_institution", "id_institution"),
        DictionaryKind.Manager => ("app_user", "id_user"),
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private static string GetSelectSql(DictionaryKind k) => k switch
    {
        DictionaryKind.Direction =>
            @"SELECT d.id_direction, d.direction_name, d.lesson_price, d.is_active, d.description,
                     d.parent_id_direction, d.sort_order, p.direction_name AS parent_name
              FROM public.direction d
              LEFT JOIN public.direction p ON p.id_direction = d.parent_id_direction
              ORDER BY d.is_active DESC, COALESCE(p.direction_name, d.direction_name), d.sort_order, d.direction_name;",
        DictionaryKind.Classroom =>
            "SELECT id_classroom, room_number, capacity, description, COALESCE(id_branch, 1) AS id_branch FROM public.classroom ORDER BY room_number;",
        DictionaryKind.EducationalInstitution or DictionaryKind.School or DictionaryKind.Kindergarten =>
            "SELECT id_institution, institution_name, institution_type, COALESCE(institution_number, '') FROM public.educational_institution ORDER BY institution_type, institution_name;",
        DictionaryKind.StudyBranch =>
            "SELECT id_branch, short_name, address_line, button_label, is_active FROM public.study_branch ORDER BY id_branch;",
        DictionaryKind.AcademicYear =>
            "SELECT id_academic_year, year_label, start_year, is_active FROM public.academic_year ORDER BY start_year DESC;",
        DictionaryKind.Street =>
            "SELECT id_street, street_name FROM public.street ORDER BY street_name;",
        DictionaryKind.Manager =>
            @"SELECT u.id_user,
                     TRIM(COALESCE(u.last_name, '') || ' ' || COALESCE(u.first_name, '') || COALESCE(' ' || u.middle_name, '')),
                     r.role_name, u.is_active, COALESCE(u.email, '')
              FROM public.app_user u
              JOIN public.role r ON r.id_role = u.id_role
              WHERE LOWER(TRIM(r.role_code)) IN (
                    'manager', 'менеджер',
                    'senior_manager', 'senior-manager', 'head_manager',
                    'старший менеджер', 'старший_менеджер', 'старшийменеджер')
              ORDER BY u.is_active DESC, u.last_name, u.first_name;",
        DictionaryKind.RelationType =>
            "SELECT id_relation_type, relation_name FROM public.relation_type ORDER BY relation_name;",
        DictionaryKind.StudentType =>
            "SELECT id_student_type, type_name, type_code FROM public.student_type ORDER BY type_name;",
        DictionaryKind.SourceOfArrival =>
            "SELECT id_source, source_name FROM public.source_of_arrival ORDER BY source_name;",
        DictionaryKind.PaymentMethod =>
            "SELECT id_payment_method, method_name, method_code FROM public.payment_method ORDER BY method_name;",
        DictionaryKind.ServiceType =>
            "SELECT id_service_type, service_name, service_code FROM public.service_type ORDER BY service_name;",
        DictionaryKind.ExpenseType =>
            "SELECT id_expense_type, expense_name FROM public.expense_type ORDER BY expense_name;",
        DictionaryKind.ShopProduct =>
            "SELECT id_product, product_name, price, stock_qty, is_active FROM public.shop_product ORDER BY is_active DESC, product_name;",
        DictionaryKind.CoursePackage =>
            @"SELECT cp.id_course_package, cp.course_name, cp.duration_months, cp.lessons_count,
                     cp.times_per_week, cp.price, cp.is_active, COALESCE(d.direction_name, '') AS direction_name,
                     cp.id_direction
              FROM public.course_package cp
              LEFT JOIN public.direction d ON d.id_direction = cp.id_direction
              ORDER BY cp.is_active DESC, cp.duration_months DESC, cp.course_name;",
        DictionaryKind.EventType =>
            "SELECT id_event_type, event_name FROM public.event_type ORDER BY event_name;",
        DictionaryKind.SatisfactionLevel =>
            "SELECT id_satisfaction_level, level_name, level_code, is_positive, is_active, sort_order FROM public.satisfaction_level ORDER BY sort_order, level_name;",
        DictionaryKind.DissatisfactionReason =>
            "SELECT id_reason, reason_name, is_active, sort_order FROM public.dissatisfaction_reason ORDER BY sort_order, reason_name;",
        DictionaryKind.Role =>
            "SELECT id_role, role_name, role_code, description FROM public.role ORDER BY role_name;",
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private static (string sql, Action<NpgsqlCommand> fill) GetSaveSql(DictionaryKind kind, DictionaryEntry e)
    {
        switch (kind)
        {
            case DictionaryKind.Direction:
                {
                    decimal price = e.Extra.TryGetValue("price", out var p) && p != null ? Convert.ToDecimal(p) : 0m;
                    string? desc = e.Extra.TryGetValue("description", out var d) ? d as string : null;
                    bool active = e.IsActive ?? true;
                    int? parentId = e.Extra.TryGetValue("parent_id_direction", out var pid) && pid is int pi && pi > 0
                        ? pi
                        : null;
                    short sortOrder = e.Extra.TryGetValue("sort_order", out var so) && so != null
                        ? Convert.ToInt16(so)
                        : (short)0;
                    AcademicSchema.EnsureDirectionColumns();
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.direction
    (direction_name, lesson_price, description, is_active, parent_id_direction, sort_order)
VALUES (@name, @price, @description, @is_active, @parent_id, @sort_order) RETURNING id_direction;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("price", NpgsqlDbType.Numeric).Value = price;
                                cmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                                    string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                                cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                                cmd.Parameters.Add("parent_id", NpgsqlDbType.Integer).Value =
                                    parentId.HasValue ? parentId.Value : DBNull.Value;
                                cmd.Parameters.Add("sort_order", NpgsqlDbType.Smallint).Value = sortOrder;
                            });
                    }
                    return (@"UPDATE public.direction SET direction_name = @name, lesson_price = @price,
description = @description, is_active = @is_active, parent_id_direction = @parent_id, sort_order = @sort_order
WHERE id_direction = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("price", NpgsqlDbType.Numeric).Value = price;
                            cmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                                string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                            cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                            cmd.Parameters.Add("parent_id", NpgsqlDbType.Integer).Value =
                                parentId.HasValue ? parentId.Value : DBNull.Value;
                            cmd.Parameters.Add("sort_order", NpgsqlDbType.Smallint).Value = sortOrder;
                        });
                }
            case DictionaryKind.Classroom:
                {
                    int capacity = e.Extra.TryGetValue("capacity", out var c) && c != null ? Convert.ToInt32(c) : 0;
                    string? desc = e.Extra.TryGetValue("description", out var d) ? d as string : null;
                    short idBranch = e.Extra.TryGetValue("id_branch", out var b) && b != null
                        ? Convert.ToInt16(b)
                        : (short)1;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.classroom (room_number, capacity, description, id_branch)
VALUES (@name, @capacity, @description, @id_branch) RETURNING id_classroom;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("capacity", NpgsqlDbType.Integer).Value = capacity;
                                cmd.Parameters.Add("description", NpgsqlDbType.Varchar).Value =
                                    string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                                cmd.Parameters.Add("id_branch", NpgsqlDbType.Smallint).Value = idBranch;
                            });
                    }
                    return (@"UPDATE public.classroom SET room_number = @name, capacity = @capacity,
description = @description, id_branch = @id_branch WHERE id_classroom = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("capacity", NpgsqlDbType.Integer).Value = capacity;
                            cmd.Parameters.Add("description", NpgsqlDbType.Varchar).Value =
                                string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                            cmd.Parameters.Add("id_branch", NpgsqlDbType.Smallint).Value = idBranch;
                        });
                }
            case DictionaryKind.EducationalInstitution:
            case DictionaryKind.School:
            case DictionaryKind.Kindergarten:
                {
                    string type = NormalizeInstitutionType(
                        e.Extra.TryGetValue("institution_type", out var t) && t != null
                            ? t.ToString()
                            : DefaultInstitutionType(kind));
                    string? number = e.Extra.TryGetValue("institution_number", out var n) ? n as string : null;
                    var name = e.Name.Trim();
                    if (name.Length > 150)
                        name = name[..150];
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.educational_institution (institution_name, institution_type, institution_number)
VALUES (@name, @type, @number) RETURNING id_institution;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = name;
                                cmd.Parameters.Add("type", NpgsqlDbType.Varchar).Value = type;
                                cmd.Parameters.Add("number", NpgsqlDbType.Varchar).Value =
                                    string.IsNullOrWhiteSpace(number) ? (object)DBNull.Value : number.Trim();
                            });
                    }
                    return (@"UPDATE public.educational_institution SET institution_name = @name,
institution_type = @type, institution_number = @number WHERE id_institution = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = name;
                            cmd.Parameters.Add("type", NpgsqlDbType.Varchar).Value = type;
                            cmd.Parameters.Add("number", NpgsqlDbType.Varchar).Value =
                                string.IsNullOrWhiteSpace(number) ? (object)DBNull.Value : number.Trim();
                        });
                }
            case DictionaryKind.RelationType:
                {
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.relation_type (relation_name)
VALUES (@name) RETURNING id_relation_type;",
                            cmd => cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim());
                    }
                    return (@"UPDATE public.relation_type SET relation_name = @name
WHERE id_relation_type = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.StudentType:
                {
                    string code = e.Extra.TryGetValue("type_code", out var c) && c != null
                        ? c.ToString()! : e.Name.Trim().ToLowerInvariant();
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.student_type (type_code, type_name)
VALUES (@code, @name) RETURNING id_student_type;",
                            cmd =>
                            {
                                cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            });
                    }
                    return (@"UPDATE public.student_type SET type_code = @code, type_name = @name
WHERE id_student_type = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.SourceOfArrival:
                {
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.source_of_arrival (source_name)
VALUES (@name) RETURNING id_source;",
                            cmd => cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim());
                    }
                    return (@"UPDATE public.source_of_arrival SET source_name = @name
WHERE id_source = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.PaymentMethod:
                {
                    string code = CodeFromExtra(e, "method_code");
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.payment_method (method_code, method_name)
VALUES (@code, @name) RETURNING id_payment_method;",
                            cmd =>
                            {
                                cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            });
                    }
                    return (@"UPDATE public.payment_method SET method_code = @code, method_name = @name
WHERE id_payment_method = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.ServiceType:
                {
                    string code = CodeFromExtra(e, "service_code");
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.service_type (service_code, service_name)
VALUES (@code, @name) RETURNING id_service_type;",
                            cmd =>
                            {
                                cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            });
                    }
                    return (@"UPDATE public.service_type SET service_code = @code, service_name = @name
WHERE id_service_type = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.ExpenseType:
                {
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.expense_type (expense_name)
VALUES (@name) RETURNING id_expense_type;",
                            cmd => cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim());
                    }
                    return (@"UPDATE public.expense_type SET expense_name = @name
WHERE id_expense_type = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.ShopProduct:
                {
                    decimal price = e.Extra.TryGetValue("price", out var p) && p != null ? Convert.ToDecimal(p) : 0m;
                    int stock = e.Extra.TryGetValue("stock_qty", out var s) && s != null ? Convert.ToInt32(s) : 0;
                    bool active = e.IsActive ?? true;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.shop_product (product_name, price, stock_qty, is_active)
VALUES (@name, @price, @stock, @is_active) RETURNING id_product;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("price", NpgsqlDbType.Numeric).Value = price;
                                cmd.Parameters.Add("stock", NpgsqlDbType.Integer).Value = stock;
                                cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.shop_product SET product_name = @name, price = @price,
stock_qty = @stock, is_active = @is_active WHERE id_product = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("price", NpgsqlDbType.Numeric).Value = price;
                            cmd.Parameters.Add("stock", NpgsqlDbType.Integer).Value = stock;
                            cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.CoursePackage:
                {
                    decimal duration = e.Extra.TryGetValue("duration_months", out var dm) && dm != null
                        ? Convert.ToDecimal(dm) : 9m;
                    if (duration <= 0)
                        duration = 9m;
                    int? lessons = e.Extra.TryGetValue("lessons_count", out var lc) && lc != null
                        ? Convert.ToInt32(lc) : null;
                    short timesPerWeek = e.Extra.TryGetValue("times_per_week", out var tw) && tw != null
                        ? Convert.ToInt16(tw) : (short)1;
                    bool active = e.IsActive ?? true;
                    int? idDirection = e.Extra.TryGetValue("id_direction", out var dir) && dir is int di && di > 0
                        ? di
                        : null;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.course_package
    (course_name, id_direction, duration_months, lessons_count, times_per_week, price, is_active)
VALUES (@name, @direction, @duration, @lessons, @times, NULL, @is_active)
RETURNING id_course_package;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("direction", NpgsqlDbType.Integer).Value =
                                    idDirection.HasValue ? idDirection.Value : DBNull.Value;
                                cmd.Parameters.Add("duration", NpgsqlDbType.Numeric).Value = duration;
                                cmd.Parameters.Add("lessons", NpgsqlDbType.Integer).Value = (object?)lessons ?? DBNull.Value;
                                cmd.Parameters.Add("times", NpgsqlDbType.Smallint).Value = timesPerWeek;
                                cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.course_package
SET course_name = @name, id_direction = @direction, duration_months = @duration,
    lessons_count = @lessons, times_per_week = @times, price = NULL, is_active = @is_active
WHERE id_course_package = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("direction", NpgsqlDbType.Integer).Value =
                                idDirection.HasValue ? idDirection.Value : DBNull.Value;
                            cmd.Parameters.Add("duration", NpgsqlDbType.Numeric).Value = duration;
                            cmd.Parameters.Add("lessons", NpgsqlDbType.Integer).Value = (object?)lessons ?? DBNull.Value;
                            cmd.Parameters.Add("times", NpgsqlDbType.Smallint).Value = timesPerWeek;
                            cmd.Parameters.Add("is_active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.EventType:
                {
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.event_type (event_name)
VALUES (@name) RETURNING id_event_type;",
                            cmd => cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim());
                    }
                    return (@"UPDATE public.event_type SET event_name = @name
WHERE id_event_type = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            case DictionaryKind.SatisfactionLevel:
                {
                    string code = CodeFromExtra(e, "level_code");
                    bool isPositive = e.Extra.TryGetValue("is_positive", out var ip) && ip is bool b && b;
                    short sortOrder = e.Extra.TryGetValue("sort_order", out var so) && so != null
                        ? Convert.ToInt16(so) : (short)0;
                    bool active = e.IsActive ?? true;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.satisfaction_level (level_name, level_code, is_positive, sort_order, is_active)
VALUES (@name, @code, @positive, @sort, @active) RETURNING id_satisfaction_level;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                                cmd.Parameters.Add("positive", NpgsqlDbType.Boolean).Value = isPositive;
                                cmd.Parameters.Add("sort", NpgsqlDbType.Smallint).Value = sortOrder;
                                cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.satisfaction_level SET level_name = @name, level_code = @code,
is_positive = @positive, sort_order = @sort, is_active = @active WHERE id_satisfaction_level = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                            cmd.Parameters.Add("positive", NpgsqlDbType.Boolean).Value = isPositive;
                            cmd.Parameters.Add("sort", NpgsqlDbType.Smallint).Value = sortOrder;
                            cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.DissatisfactionReason:
                {
                    short sortOrder = e.Extra.TryGetValue("sort_order", out var so) && so != null
                        ? Convert.ToInt16(so) : (short)0;
                    bool active = e.IsActive ?? true;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.dissatisfaction_reason (reason_name, sort_order, is_active)
VALUES (@name, @sort, @active) RETURNING id_reason;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("sort", NpgsqlDbType.Smallint).Value = sortOrder;
                                cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.dissatisfaction_reason SET reason_name = @name,
sort_order = @sort, is_active = @active WHERE id_reason = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("sort", NpgsqlDbType.Smallint).Value = sortOrder;
                            cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.Role:
                {
                    string code = CodeFromExtra(e, "role_code");
                    string? desc = e.Extra.TryGetValue("description", out var d) ? d as string : null;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.role (role_code, role_name, description)
VALUES (@code, @name, @description) RETURNING id_role;",
                            cmd =>
                            {
                                cmd.Parameters.Add("code", NpgsqlDbType.Varchar).Value = code;
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                                    string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                            });
                    }
                    return (@"UPDATE public.role SET role_name = @name,
description = @description WHERE id_role = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                                string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc.Trim();
                        });
                }
            case DictionaryKind.StudyBranch:
                {
                    string address = e.Extra.TryGetValue("address_line", out var a) && a != null
                        ? a.ToString()! : e.Name.Trim();
                    string button = e.Extra.TryGetValue("button_label", out var bl) && bl != null
                        ? bl.ToString()! : e.Name.Trim();
                    bool active = e.IsActive ?? true;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.study_branch (id_branch, short_name, address_line, button_label, is_active)
VALUES ((SELECT COALESCE(MAX(id_branch), 0) + 1 FROM public.study_branch),
        @name, @address, @button, @active)
RETURNING id_branch;",
                            cmd =>
                            {
                                cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                                cmd.Parameters.Add("address", NpgsqlDbType.Varchar).Value = address.Trim();
                                cmd.Parameters.Add("button", NpgsqlDbType.Varchar).Value = button.Trim();
                                cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.study_branch
SET short_name = @name, address_line = @address, button_label = @button, is_active = @active
WHERE id_branch = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                            cmd.Parameters.Add("address", NpgsqlDbType.Varchar).Value = address.Trim();
                            cmd.Parameters.Add("button", NpgsqlDbType.Varchar).Value = button.Trim();
                            cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.AcademicYear:
                {
                    short startYear = e.Extra.TryGetValue("start_year", out var sy) && sy != null
                        ? Convert.ToInt16(sy)
                        : (short)DateTime.Today.Year;
                    string label = string.IsNullOrWhiteSpace(e.Name)
                        ? $"{startYear}/{startYear + 1}"
                        : e.Name.Trim();
                    bool active = e.IsActive ?? true;
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.academic_year (start_year, year_label, is_active)
VALUES (@start, @label, @active)
ON CONFLICT (start_year) DO UPDATE SET year_label = EXCLUDED.year_label, is_active = EXCLUDED.is_active
RETURNING id_academic_year;",
                            cmd =>
                            {
                                cmd.Parameters.Add("start", NpgsqlDbType.Smallint).Value = startYear;
                                cmd.Parameters.Add("label", NpgsqlDbType.Varchar).Value = label;
                                cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                            });
                    }
                    return (@"UPDATE public.academic_year
SET start_year = @start, year_label = @label, is_active = @active
WHERE id_academic_year = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("start", NpgsqlDbType.Smallint).Value = startYear;
                            cmd.Parameters.Add("label", NpgsqlDbType.Varchar).Value = label;
                            cmd.Parameters.Add("active", NpgsqlDbType.Boolean).Value = active;
                        });
                }
            case DictionaryKind.Street:
                {
                    if (e.Id == 0)
                    {
                        return (@"INSERT INTO public.street (street_name)
VALUES (@name)
ON CONFLICT (street_name) DO UPDATE SET street_name = EXCLUDED.street_name
RETURNING id_street;",
                            cmd => cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim());
                    }
                    return (@"UPDATE public.street SET street_name = @name WHERE id_street = @id;",
                        cmd =>
                        {
                            cmd.Parameters.Add("id", NpgsqlDbType.Integer).Value = e.Id;
                            cmd.Parameters.Add("name", NpgsqlDbType.Varchar).Value = e.Name.Trim();
                        });
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static string DefaultInstitutionType(DictionaryKind kind) => kind switch
    {
        DictionaryKind.Kindergarten => "детский сад",
        DictionaryKind.School => "школа",
        _ => "школа"
    };

    internal static string NormalizeInstitutionType(string? raw)
    {
        var key = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
            return "школа";
        if (key is "д" or "с" or "дс" or "доу" or "сад" or "детсад" or "дет. сад" or "дет.сад"
            or "д/с" or "детский сад")
            return "детский сад";
        if (key.Contains("гимназ"))
            return "гимназия";
        if (key.Contains("лицей"))
            return "лицей";
        if (key.Contains("вуз") || key.Contains("университет") || key.Contains("институт"))
            return "вуз";
        if (key.Contains("колледж") || key.Contains("техникум"))
            return "колледж";
        if (key is "ш" or "школа" or "сош" or "сш" or "нш" or "другое")
            return key is "другое" ? "другое" : "школа";
        if (key is "гимназия" or "лицей" or "школа" or "вуз" or "колледж" or "другое")
            return key;
        return "школа";
    }

    private static string CodeFromExtra(DictionaryEntry e, string key)
    {
        if (e.Extra.TryGetValue(key, out var c) && c != null && !string.IsNullOrWhiteSpace(c.ToString()))
            return c.ToString()!.Trim();
        return e.Name.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
