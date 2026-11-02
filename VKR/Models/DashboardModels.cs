#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace VKR.Models;

public sealed class DashboardSnapshot
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public decimal ContractRevenue { get; set; }
    public decimal SalesRevenue { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal PreviousTotalRevenue { get; set; }

    public int ContractsCount { get; set; }
    public int PreviousContractsCount { get; set; }

    public int TotalLeadsCount { get; set; }
    public string TopLeadSourceName { get; set; } = "—";
    public int TopLeadSourceCount { get; set; }

    public List<DashboardRevenuePoint> RevenueTrend { get; set; } = new();
    public List<DashboardChartItem> RevenueStructure { get; set; } = new();
    public List<DashboardChartItem> LeadSourceDistribution { get; set; } = new();
    public List<DashboardChartItem> DirectionRevenue { get; set; } = new();

    public string TotalRevenueText => FormatMoney(TotalRevenue);
    public string ContractRevenueText => FormatMoney(ContractRevenue);
    public string SalesRevenueText => FormatMoney(SalesRevenue);

    public static string FormatMoney(decimal value)
    {
        return value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
    }
}

public sealed class DashboardRevenuePoint
{
    public string Label { get; set; } = string.Empty;
    public decimal ContractRevenue { get; set; }
    public decimal SalesRevenue { get; set; }
    public decimal TotalRevenue => ContractRevenue + SalesRevenue;

    public double ContractBarWidth { get; set; }
    public double SalesBarWidth { get; set; }
    public double TotalBarWidth => ContractBarWidth + SalesBarWidth;

    public string ContractRevenueText => DashboardSnapshot.FormatMoney(ContractRevenue);
    public string SalesRevenueText => DashboardSnapshot.FormatMoney(SalesRevenue);
    public string TotalText => DashboardSnapshot.FormatMoney(TotalRevenue);
}

public sealed class DashboardChartItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public double BarWidth { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public string PercentageText { get; set; } = string.Empty;
    public string SlicePathData { get; set; } = string.Empty;
    public bool IsFullCircle { get; set; }
    public bool HasValue { get; set; }
    public Brush BarBrush { get; set; } = Brushes.SlateGray;
    public Brush BadgeBackground { get; set; } = Brushes.WhiteSmoke;
    public Brush BadgeForeground { get; set; } = Brushes.SlateGray;
}
