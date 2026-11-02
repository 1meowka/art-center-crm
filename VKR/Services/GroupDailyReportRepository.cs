#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public sealed class GroupDailyReportRepository
{
    public async Task<IReadOnlyList<GroupDailyReportEditRow>> GetEditableRowsAsync(
        DateTime reportDate,
        int idBranch)
    {
        AcademicSchema.Ensure();
        var dayOfWeek = ReportPeriodHelper.ToScheduleDayOfWeek(reportDate);
        var result = new List<GroupDailyReportEditRow>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT DISTINCT
    COALESCE(r.id_report, 0),
    g.id_group,
    d.direction_name || ' — гр. ' || g.group_number::text,
    TRIM(t.last_name || ' ' || t.first_name),
    COALESCE(r.students_present, 0),
    COALESCE(r.trial_present, 0),
    r.comment
FROM public.schedule sc
JOIN public.study_group g ON g.id_group = sc.id_group AND g.is_active
JOIN public.direction d ON d.id_direction = g.id_direction
JOIN public.teacher t ON t.id_teacher = g.id_teacher
JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
LEFT JOIN public.group_daily_report r
    ON r.id_group = g.id_group AND r.report_date = @report_date
WHERE sc.is_active
  AND sc.day_of_week = @day_of_week
  AND cr.id_branch = @branch
ORDER BY 4, 3;", connection);
        command.Parameters.Add("report_date", NpgsqlDbType.Date).Value = reportDate.Date;
        command.Parameters.Add("day_of_week", NpgsqlDbType.Smallint).Value = dayOfWeek;
        command.Parameters.Add("branch", NpgsqlDbType.Integer).Value = idBranch;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new GroupDailyReportEditRow
            {
                IdReport = reader.GetInt32(0),
                IdGroup = reader.GetInt32(1),
                GroupTitle = reader.GetString(2),
                TeacherName = reader.GetString(3).Trim(),
                StudentsPresent = reader.GetInt16(4),
                TrialPresent = reader.GetInt16(5),
                Comment = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return result;
    }

    public async Task SaveReportsAsync(
        DateTime reportDate,
        IReadOnlyList<GroupDailyReportEditRow> rows,
        int? idUpdatedBy)
    {
        if (rows.Count == 0)
            return;

        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            foreach (var row in rows)
            {
                await using var command = new NpgsqlCommand(@"
INSERT INTO public.group_daily_report
    (id_group, report_date, students_present, trial_present, comment, id_updated_by, updated_at)
VALUES
    (@group, @date, @students, @trials, @comment, @user, CURRENT_TIMESTAMP)
ON CONFLICT (id_group, report_date) DO UPDATE SET
    students_present = EXCLUDED.students_present,
    trial_present = EXCLUDED.trial_present,
    comment = EXCLUDED.comment,
    id_updated_by = EXCLUDED.id_updated_by,
    updated_at = CURRENT_TIMESTAMP;", connection, tx);
                command.Parameters.Add("group", NpgsqlDbType.Integer).Value = row.IdGroup;
                command.Parameters.Add("date", NpgsqlDbType.Date).Value = reportDate.Date;
                command.Parameters.Add("students", NpgsqlDbType.Smallint).Value = row.StudentsPresent;
                command.Parameters.Add("trials", NpgsqlDbType.Smallint).Value = row.TrialPresent;
                command.Parameters.Add("comment", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(row.Comment) ? DBNull.Value : row.Comment.Trim();
                command.Parameters.Add("user", NpgsqlDbType.Integer).Value =
                    idUpdatedBy.HasValue ? idUpdatedBy.Value : DBNull.Value;
                await command.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<GroupDailyReportDaySummary> GetDaySummaryAsync(DateTime reportDate, int? idBranch = null)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    COUNT(*)::int,
    COALESCE(SUM(r.students_present), 0)::int,
    COALESCE(SUM(r.trial_present), 0)::int
FROM public.group_daily_report r
JOIN public.study_group g ON g.id_group = r.id_group
WHERE r.report_date = @date
  AND (@branch IS NULL OR EXISTS (
      SELECT 1
      FROM public.schedule sc
      JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
      WHERE sc.id_group = g.id_group AND cr.id_branch = @branch AND sc.is_active
  ));", connection);
        command.Parameters.Add("date", NpgsqlDbType.Date).Value = reportDate.Date;
        command.Parameters.Add("branch", NpgsqlDbType.Integer).Value =
            idBranch.HasValue ? idBranch.Value : DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new GroupDailyReportDaySummary();

        return new GroupDailyReportDaySummary
        {
            GroupsReported = reader.GetInt32(0),
            StudentsTotal = reader.GetInt32(1),
            TrialsTotal = reader.GetInt32(2)
        };
    }

    public async Task<IReadOnlyDictionary<DateTime, GroupDailyReportDaySummary>> GetDaySummariesForRangeAsync(
        DateTime dateFrom,
        DateTime dateTo,
        int? idBranch = null)
    {
        AcademicSchema.Ensure();
        var result = new Dictionary<DateTime, GroupDailyReportDaySummary>();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    r.report_date,
    COUNT(*)::int,
    COALESCE(SUM(r.students_present), 0)::int,
    COALESCE(SUM(r.trial_present), 0)::int
FROM public.group_daily_report r
JOIN public.study_group g ON g.id_group = r.id_group
WHERE r.report_date BETWEEN @from AND @to
  AND (@branch IS NULL OR EXISTS (
      SELECT 1
      FROM public.schedule sc
      JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
      WHERE sc.id_group = g.id_group AND cr.id_branch = @branch AND sc.is_active
  ))
GROUP BY r.report_date;", connection);
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = dateFrom.Date;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = dateTo.Date;
        command.Parameters.Add("branch", NpgsqlDbType.Integer).Value =
            idBranch.HasValue ? idBranch.Value : DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var date = reader.GetDateTime(0).Date;
            result[date] = new GroupDailyReportDaySummary
            {
                GroupsReported = reader.GetInt32(1),
                StudentsTotal = reader.GetInt32(2),
                TrialsTotal = reader.GetInt32(3)
            };
        }

        return result;
    }

    public async Task<GroupDailyReportDaySummary> GetPeriodSummaryAsync(
        DateTime periodStart,
        DateTime periodEnd,
        int? idBranch = null)
    {
        AcademicSchema.Ensure();
        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT
    COUNT(DISTINCT r.id_group)::int,
    COALESCE(SUM(r.students_present), 0)::int,
    COALESCE(SUM(r.trial_present), 0)::int
FROM public.group_daily_report r
JOIN public.study_group g ON g.id_group = r.id_group
WHERE r.report_date BETWEEN @from AND @to
  AND (@branch IS NULL OR EXISTS (
      SELECT 1
      FROM public.schedule sc
      JOIN public.classroom cr ON cr.id_classroom = sc.id_classroom
      WHERE sc.id_group = g.id_group AND cr.id_branch = @branch AND sc.is_active
  ));", connection);
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = periodStart.Date;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = periodEnd.Date;
        command.Parameters.Add("branch", NpgsqlDbType.Integer).Value =
            idBranch.HasValue ? idBranch.Value : DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new GroupDailyReportDaySummary();

        return new GroupDailyReportDaySummary
        {
            GroupsReported = reader.GetInt32(0),
            StudentsTotal = reader.GetInt32(1),
            TrialsTotal = reader.GetInt32(2)
        };
    }
}
