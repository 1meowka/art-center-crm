#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Npgsql;
using VKR.Models;
using VKR.Resources;

namespace VKR.Services;

public sealed class DashboardRepository
{
    private const double MaxBarWidth = 260d;
    private const double PieCenterX = 110d;
    private const double PieCenterY = 110d;
    private const double PieRadius = 88d;
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<DashboardSnapshot> GetSnapshotAsync(DateTime periodStart, DateTime periodEnd)
    {
        if (periodEnd.Date < periodStart.Date)
            throw new InvalidOperationException("Дата окончания периода должна быть не раньше даты начала.");

        periodStart = periodStart.Date;
        periodEnd = periodEnd.Date;

        var previousEnd = periodStart.AddDays(-1);
        var previousStart = previousEnd.AddDays(-(periodEnd - periodStart).TotalDays);

        await using var connection = Db.CreateConnection();
        await connection.OpenAsync();

        var snapshot = new DashboardSnapshot
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd
        };

        (snapshot.ContractRevenue, snapshot.SalesRevenue) = await GetRevenueAsync(connection, periodStart, periodEnd);
        snapshot.TotalRevenue = snapshot.ContractRevenue + snapshot.SalesRevenue;

        var (previousContractRevenue, previousSalesRevenue) = await GetRevenueAsync(connection, previousStart, previousEnd);
        snapshot.PreviousTotalRevenue = previousContractRevenue + previousSalesRevenue;

        snapshot.ContractsCount = await GetContractsCountAsync(connection, periodStart, periodEnd);
        snapshot.PreviousContractsCount = await GetContractsCountAsync(connection, previousStart, previousEnd);

        var leadSources = await GetLeadSourceStatsAsync(connection, periodStart, periodEnd);
        snapshot.TotalLeadsCount = leadSources.Sum(x => x.LeadsCount);
        var top = leadSources.OrderByDescending(x => x.LeadsCount).FirstOrDefault();
        if (top != null && top.LeadsCount > 0)
        {
            snapshot.TopLeadSourceName = top.SourceName;
            snapshot.TopLeadSourceCount = top.LeadsCount;
        }

        snapshot.RevenueTrend = await GetRevenueTrendAsync(connection, periodStart, periodEnd);
        NormalizeRevenueTrend(snapshot.RevenueTrend);

        snapshot.RevenueStructure = BuildRevenueStructure(snapshot.ContractRevenue, snapshot.SalesRevenue);
        snapshot.LeadSourceDistribution = BuildLeadSourceDistribution(leadSources);
        ApplyPieLayout(snapshot.LeadSourceDistribution);

        snapshot.DirectionRevenue = await GetDirectionRevenueAsync(connection, periodStart, periodEnd);
        NormalizeChartItems(snapshot.DirectionRevenue);
        ApplyPieLayout(snapshot.DirectionRevenue);

        return snapshot;
    }

    private static async Task<(decimal ContractRevenue, decimal SalesRevenue)> GetRevenueAsync(
        NpgsqlConnection connection,
        DateTime start,
        DateTime end)
    {
        const string sql = @"
SELECT
    COALESCE((
        SELECT SUM(c.total_amount)
        FROM public.contract c
        WHERE c.contract_date BETWEEN @start AND @end
    ), 0) AS contract_revenue,
    COALESCE((
        SELECT SUM(COALESCE(s.total_amount, s.unit_price * s.quantity, s.sale_price * s.quantity, 0))
        FROM public.shop_sale s
        WHERE s.sale_date BETWEEN @start AND @end
    ), 0) AS sales_revenue;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start.Date);
        command.Parameters.AddWithValue("end", end.Date);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (0m, 0m);

        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task<int> GetContractsCountAsync(NpgsqlConnection connection, DateTime start, DateTime end)
    {
        const string sql = @"
SELECT COUNT(*)::int
FROM public.contract
WHERE contract_date BETWEEN @start AND @end;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start.Date);
        command.Parameters.AddWithValue("end", end.Date);

        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task<List<LeadSourceStatRow>> GetLeadSourceStatsAsync(
        NpgsqlConnection connection,
        DateTime start,
        DateTime end)
    {
        const string sql = @"
SELECT
    COALESCE(so.source_name, 'Не указан') AS source_name,
    COUNT(*)::int AS leads_count
FROM public.lead_request lr
LEFT JOIN public.source_of_arrival so ON so.id_source = lr.id_source
WHERE lr.created_date::date BETWEEN @start AND @end
GROUP BY COALESCE(so.source_name, 'Не указан')
ORDER BY leads_count DESC, source_name;";

        var result = new List<LeadSourceStatRow>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LeadSourceStatRow
            {
                SourceName = reader.GetString(0),
                LeadsCount = reader.GetInt32(1)
            });
        }

        return result;
    }

    private static async Task<List<DashboardRevenuePoint>> GetRevenueTrendAsync(
        NpgsqlConnection connection,
        DateTime start,
        DateTime end)
    {
        var list = new List<DashboardRevenuePoint>();
        var groupByMonth = (end - start).TotalDays > 45;

        const string sql = @"
WITH income AS (
    SELECT
        CASE WHEN @monthly THEN date_trunc('month', c.contract_date)::date ELSE c.contract_date::date END AS period_date,
        SUM(c.total_amount) AS contract_revenue,
        0::numeric AS sales_revenue
    FROM public.contract c
    WHERE c.contract_date BETWEEN @start AND @end
    GROUP BY 1

    UNION ALL

    SELECT
        CASE WHEN @monthly THEN date_trunc('month', s.sale_date)::date ELSE s.sale_date::date END AS period_date,
        0::numeric AS contract_revenue,
        SUM(COALESCE(s.total_amount, s.unit_price * s.quantity, s.sale_price * s.quantity, 0)) AS sales_revenue
    FROM public.shop_sale s
    WHERE s.sale_date BETWEEN @start AND @end
    GROUP BY 1
)
SELECT period_date,
       COALESCE(SUM(contract_revenue), 0) AS contract_revenue,
       COALESCE(SUM(sales_revenue), 0) AS sales_revenue
FROM income
GROUP BY period_date
ORDER BY period_date;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("monthly", groupByMonth);
        command.Parameters.AddWithValue("start", start.Date);
        command.Parameters.AddWithValue("end", end.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var periodDate = reader.GetDateTime(0);
            list.Add(new DashboardRevenuePoint
            {
                Label = groupByMonth
                    ? periodDate.ToString("MMM yyyy", RuCulture)
                    : periodDate.ToString("dd.MM", RuCulture),
                ContractRevenue = reader.GetDecimal(1),
                SalesRevenue = reader.GetDecimal(2)
            });
        }

        if (list.Count == 0)
        {
            list.Add(new DashboardRevenuePoint
            {
                Label = "нет данных",
                ContractRevenue = 0m,
                SalesRevenue = 0m
            });
        }

        return list;
    }

    private static async Task<List<DashboardChartItem>> GetDirectionRevenueAsync(
        NpgsqlConnection connection,
        DateTime start,
        DateTime end)
    {
        var list = new List<DashboardChartItem>();

        const string sql = @"
SELECT COALESCE(d.direction_name, 'Без направления') AS direction_name,
       COALESCE(SUM(c.total_amount), 0) AS total_revenue
FROM public.contract c
LEFT JOIN public.direction d ON d.id_direction = c.id_direction
WHERE c.contract_date BETWEEN @start AND @end
GROUP BY COALESCE(d.direction_name, 'Без направления')
HAVING COALESCE(SUM(c.total_amount), 0) > 0
ORDER BY total_revenue DESC
LIMIT 5;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start.Date);
        command.Parameters.AddWithValue("end", end.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.GetDecimal(1);
            list.Add(new DashboardChartItem
            {
                Title = reader.IsDBNull(0) ? "Без направления" : reader.GetString(0),
                Value = value,
                ValueText = DashboardSnapshot.FormatMoney(value),
                BarBrush = ThemeColors.ToBrush(ThemeColors.AccentViolet),
                BadgeBackground = ThemeColors.ToBrush(ThemeColors.NeutralBg),
                BadgeForeground = ThemeColors.ToBrush(ThemeColors.Text)
            });
        }

        var palette = ThemeColors.ChartPalette;

        var total = 0m;
        foreach (var item in list)
            total += item.Value;

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var color = palette[i % palette.Length];
            item.BarBrush = new SolidColorBrush(color);
            item.BadgeBackground = ThemeColors.TintedBadge(color);
            item.BadgeForeground = new SolidColorBrush(color);
            item.Subtitle = total > 0 ? $"{item.Value / total:P0} от выручки" : "0% от выручки";
            item.PercentageText = total > 0 ? $"{item.Value / total:P0}" : "0%";
        }

        if (list.Count == 0)
        {
            list.Add(new DashboardChartItem
            {
                Title = "Нет данных по направлениям",
                Subtitle = "за выбранный период",
                Value = 0m,
                ValueText = "0 ₽",
                BarBrush = ThemeColors.ToBrush(ThemeColors.TextSubtle),
                BadgeBackground = ThemeColors.ToBrush(ThemeColors.NeutralBg),
                BadgeForeground = ThemeColors.ToBrush(ThemeColors.TextMuted)
            });
        }

        return list;
    }

    private static List<DashboardChartItem> BuildRevenueStructure(decimal contractRevenue, decimal salesRevenue)
    {
        var total = contractRevenue + salesRevenue;

        var list = new List<DashboardChartItem>
        {
            new()
            {
                Title = "Договоры",
                Subtitle = total > 0 ? $"{contractRevenue / total:P0} от выручки" : "0% от выручки",
                Value = contractRevenue,
                ValueText = DashboardSnapshot.FormatMoney(contractRevenue),
                PercentageText = total > 0 ? $"{contractRevenue / total:P0}" : "0%",
                BarBrush = ThemeColors.ToBrush(ThemeColors.Primary),
                BadgeBackground = ThemeColors.ToBrush(ThemeColors.SelectedRow),
                BadgeForeground = ThemeColors.ToBrush(ThemeColors.Text)
            },
            new()
            {
                Title = "Продажи",
                Subtitle = total > 0 ? $"{salesRevenue / total:P0} от выручки" : "0% от выручки",
                Value = salesRevenue,
                ValueText = DashboardSnapshot.FormatMoney(salesRevenue),
                PercentageText = total > 0 ? $"{salesRevenue / total:P0}" : "0%",
                BarBrush = ThemeColors.ToBrush(ThemeColors.AccentOrange),
                BadgeBackground = ThemeColors.ToBrush(ThemeColors.WarningBg),
                BadgeForeground = ThemeColors.ToBrush(ThemeColors.WarningFg)
            }
        };

        NormalizeChartItems(list);
        return list;
    }

    private static List<DashboardChartItem> BuildLeadSourceDistribution(IReadOnlyList<LeadSourceStatRow> sources)
    {
        var total = sources.Sum(x => x.LeadsCount);
        var palette = ThemeColors.ChartPalette;
        var list = new List<DashboardChartItem>();

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var color = palette[i % palette.Length];
            list.Add(new DashboardChartItem
            {
                Title = source.SourceName,
                Subtitle = total > 0 ? $"{(decimal)source.LeadsCount / total:P0} заявок" : "нет заявок",
                Value = source.LeadsCount,
                ValueText = source.LeadsCount.ToString(RuCulture),
                PercentageText = total > 0 ? $"{(decimal)source.LeadsCount / total:P0}" : "0%",
                BarBrush = new SolidColorBrush(color),
                BadgeBackground = ThemeColors.TintedBadge(color),
                BadgeForeground = new SolidColorBrush(color)
            });
        }

        if (list.Count == 0)
        {
            list.Add(new DashboardChartItem
            {
                Title = "Нет заявок",
                Subtitle = "за выбранный период",
                Value = 0m,
                ValueText = "0",
                BarBrush = ThemeColors.ToBrush(ThemeColors.TextSubtle),
                BadgeBackground = ThemeColors.ToBrush(ThemeColors.NeutralBg),
                BadgeForeground = ThemeColors.ToBrush(ThemeColors.TextMuted)
            });
        }

        NormalizeChartItems(list);
        return list;
    }

    private static void NormalizeRevenueTrend(IReadOnlyList<DashboardRevenuePoint> points)
    {
        decimal max = 0m;
        foreach (var point in points)
            max = Math.Max(max, point.TotalRevenue);

        foreach (var point in points)
        {
            if (max <= 0 || point.TotalRevenue <= 0)
            {
                point.ContractBarWidth = 0;
                point.SalesBarWidth = 0;
                continue;
            }

            var totalWidth = Math.Max(10d, (double)(point.TotalRevenue / max) * MaxBarWidth);
            point.ContractBarWidth = point.TotalRevenue == 0 ? 0 : totalWidth * (double)(point.ContractRevenue / point.TotalRevenue);
            point.SalesBarWidth = point.TotalRevenue == 0 ? 0 : totalWidth * (double)(point.SalesRevenue / point.TotalRevenue);
        }
    }

    private static void NormalizeChartItems(IReadOnlyList<DashboardChartItem> items)
    {
        decimal max = 0m;
        foreach (var item in items)
            max = Math.Max(max, item.Value);

        foreach (var item in items)
        {
            item.BarWidth = max <= 0 || item.Value <= 0
                ? 0
                : Math.Max(10d, (double)(item.Value / max) * MaxBarWidth);
        }
    }
    private static void ApplyPieLayout(IReadOnlyList<DashboardChartItem> items)
    {
        decimal total = 0m;
        var positiveCount = 0;

        foreach (var item in items)
        {
            item.HasValue = item.Value > 0;
            item.IsFullCircle = false;
            item.SlicePathData = string.Empty;

            if (item.Value > 0)
            {
                total += item.Value;
                positiveCount++;
            }
        }

        if (total <= 0 || positiveCount == 0)
            return;

        if (positiveCount == 1)
        {
            foreach (var item in items)
            {
                if (item.Value > 0)
                {
                    item.IsFullCircle = true;
                    item.PercentageText = "100%";
                    break;
                }
            }
            return;
        }

        var startAngle = -90d;
        foreach (var item in items)
        {
            if (item.Value <= 0)
                continue;

            var sweepAngle = (double)(item.Value / total) * 360d;
            item.SlicePathData = BuildPieSlicePath(startAngle, sweepAngle);
            if (string.IsNullOrWhiteSpace(item.PercentageText))
                item.PercentageText = $"{item.Value / total:P0}";
            startAngle += sweepAngle;
        }
    }

    private static string BuildPieSlicePath(double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0)
            return string.Empty;

        var startRadians = Math.PI * startAngle / 180d;
        var endRadians = Math.PI * (startAngle + sweepAngle) / 180d;

        var x1 = PieCenterX + PieRadius * Math.Cos(startRadians);
        var y1 = PieCenterY + PieRadius * Math.Sin(startRadians);
        var x2 = PieCenterX + PieRadius * Math.Cos(endRadians);
        var y2 = PieCenterY + PieRadius * Math.Sin(endRadians);
        var largeArc = sweepAngle > 180d ? 1 : 0;

        return FormattableString.Invariant(
            $"M {PieCenterX:0.###},{PieCenterY:0.###} L {x1:0.###},{y1:0.###} A {PieRadius:0.###},{PieRadius:0.###} 0 {largeArc} 1 {x2:0.###},{y2:0.###} Z");
    }

}
