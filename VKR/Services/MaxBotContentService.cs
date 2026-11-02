#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using VKR.Models;

namespace VKR.Services;

public sealed class MaxBotContentService
{
    private readonly GroupsRepository _groupsRepository = new();

    public async Task<string> BuildCurrentWeekScheduleTextAsync(CancellationToken cancellationToken = default) =>
        await BuildWeekScheduleTextAsync(StudyBranch.Central.IdBranch, DateTime.Today, cancellationToken);

    public async Task<string> BuildWeekScheduleTextAsync(
        int idBranch,
        DateTime referenceDate,
        CancellationToken cancellationToken = default)
    {
        var branch = StudyBranch.Find(idBranch);
        var weekStart = GetWeekStartMonday(referenceDate);
        var rows = await _groupsRepository.GetScheduleAsync(idGroup: null, onlyActive: true, idBranch: idBranch);
        return FormatWeekSchedule(weekStart, rows, branch);
    }

    public static string BuildBranchSelectionPrompt() =>
        "📅 Расписание\n\nВыберите филиал:";

    public async Task<IReadOnlyList<MaxBotDirectionInfo>> GetDirectionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<MaxBotDirectionInfo>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(@"
SELECT id_direction, direction_name, COALESCE(lesson_price, 0) AS lesson_price,
       COALESCE(description, '') AS description
FROM public.direction
WHERE is_active = TRUE
ORDER BY direction_name;", connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MaxBotDirectionInfo
            {
                IdDirection = reader.GetInt32(reader.GetOrdinal("id_direction")),
                Name = reader.GetString(reader.GetOrdinal("direction_name")),
                LessonPrice = reader.GetDecimal(reader.GetOrdinal("lesson_price")),
                Description = ReadOptionalString(reader, "description")
            });
        }

        return result;
    }

    public string FormatDirectionDetail(MaxBotDirectionInfo direction)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🎨 {direction.Name}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(direction.Description))
            sb.AppendLine(direction.Description.Trim());
        else
            sb.AppendLine($"Занятия по направлению «{direction.Name}» в нашем творческом центре.");

        sb.AppendLine();
        if (direction.LessonPrice > 0)
            sb.AppendLine($"💰 Стоимость: {direction.LessonPrice.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} ₽ за занятие");
        else
            sb.AppendLine("💰 Стоимость уточняйте у администратора.");

        sb.AppendLine();
        sb.AppendLine("Напишите «Запись» — поможем записаться на пробное занятие.");
        return sb.ToString().TrimEnd();
    }

    public static string FormatWeekSchedule(
        DateTime weekStart,
        IEnumerable<ScheduleItem> rows,
        StudyBranch? branch = null)
    {
        var weekEnd = weekStart.AddDays(6);
        var items = rows
            .Select(row =>
            {
                var copy = new ScheduleItem
                {
                    IdSchedule = row.IdSchedule,
                    IdGroup = row.IdGroup,
                    GroupTitle = row.GroupTitle,
                    DirectionName = row.DirectionName,
                    TeacherName = row.TeacherName,
                    IdClassroom = row.IdClassroom,
                    ClassroomNumber = row.ClassroomNumber,
                    DayOfWeek = row.DayOfWeek,
                    TimeStart = row.TimeStart,
                    TimeEnd = row.TimeEnd,
                    IsActive = row.IsActive,
                    LessonDate = weekStart.AddDays(row.DayOfWeek - 1)
                };
                return copy;
            })
            .OrderBy(x => x.LessonDate)
            .ThenBy(x => x.TimeStart)
            .ToList();

        var sb = new StringBuilder();
        if (branch != null)
        {
            sb.AppendLine($"📅 Расписание — {branch.ShortName}");
            sb.AppendLine(branch.AddressLine);
        }
        else
        {
            sb.AppendLine("📅 Расписание на неделю");
        }

        sb.AppendLine($"{weekStart:dd.MM.yyyy} — {weekEnd:dd.MM.yyyy}");
        sb.AppendLine();

        if (items.Count == 0)
        {
            sb.AppendLine(branch != null
                ? $"На этой неделе занятий в филиале «{branch.ShortName}» нет."
                : "На этой неделе занятий в расписании нет.");
            sb.AppendLine();
            sb.AppendLine("Хорошего дня! 🙌");
            return sb.ToString().TrimEnd();
        }

        foreach (var dayGroup in items.GroupBy(x => x.LessonDate!.Value.Date))
        {
            var sample = dayGroup.First();
            var isToday = dayGroup.Key == DateTime.Today;
            sb.AppendLine(isToday
                ? $"🗓 Сегодня · {sample.DayName}, {dayGroup.Key:dd.MM}"
                : $"🗓 {sample.DayName}, {dayGroup.Key:dd.MM}");
            sb.AppendLine();

            var index = 1;
            foreach (var lesson in dayGroup)
            {
                sb.AppendLine($"{index}. ⏰ {lesson.TimeRangeText}");
                sb.AppendLine($"   🎨 {lesson.GroupTitle}");
                sb.AppendLine($"   🏫 {lesson.ClassroomText}");
                sb.AppendLine($"   👨‍🏫 {lesson.TeacherName}");
                if (index < dayGroup.Count())
                    sb.AppendLine();
                index++;
            }

            sb.AppendLine();
        }

        sb.AppendLine("Хорошего дня! 🙌");
        return sb.ToString().TrimEnd();
    }

    public static DateTime GetWeekStartMonday(DateTime date)
    {
        var day = date.Date;
        var diff = (7 + (day.DayOfWeek - DayOfWeek.Monday)) % 7;
        return day.AddDays(-diff);
    }

    private static string? ReadOptionalString(NpgsqlDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));
}
