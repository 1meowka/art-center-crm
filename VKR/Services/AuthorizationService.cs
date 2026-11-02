#nullable enable
using VKR.Models;
using VKR.Utilities;

namespace VKR.Services;

public enum AppModule
{
    Leads,
    TrialLessons,
    Students,
    Groups,
    Teachers,
    Contracts,
    Payments,
    Debt,
    Sales,
    Expenses,
    PlansKpi,
    AiAnalytics,
    Dictionaries,
    Help,
    Notifications,
    MaxBot,
    Reports,
    Dashboard
}

public enum ReportExportKind
{
    Operational,
    Finance,
    Year,
    Geography
}

public static class AuthorizationService
{
    public static bool CanAccess(AppUser? user, AppModule module)
    {
        if (user == null)
            return false;

        if (module == AppModule.Help || module == AppModule.Notifications)
            return true;

        if (module == AppModule.Dashboard)
            return CanViewManagementDashboard(user);

        // Экранные формы аналитики — после наполнения и интеграции данных.
        if (module == AppModule.AiAnalytics)
            return false;

        if (module == AppModule.MaxBot)
        {
            return RoleCodes.IsAdmin(user.RoleCode)
                || RoleCodes.IsDirector(user.RoleCode)
                || RoleCodes.IsAnyManager(user.RoleCode);
        }

        if (RoleCodes.IsAdmin(user.RoleCode) || RoleCodes.IsDirector(user.RoleCode))
            return true;

        if (RoleCodes.IsSeniorManager(user.RoleCode))
            return module != AppModule.Dictionaries;

        if (RoleCodes.IsManager(user.RoleCode))
            return module is not AppModule.Dictionaries and not AppModule.PlansKpi;

        if (module == AppModule.Sales)
            return false;

        if (RoleCodes.IsTeacher(user.RoleCode))
            return module is AppModule.Teachers
                or AppModule.Groups
                or AppModule.Students
                or AppModule.Leads
                or AppModule.Help;

        return module == AppModule.Help;
    }

    /// <summary>
    /// Сводка продаж на главной — только директор, старший менеджер и администратор.
    /// Обычный менеджер работает с разделами и Excel, без аналитической главной.
    /// </summary>
    public static bool CanViewManagementDashboard(AppUser? user) =>
        user != null
        && (RoleCodes.IsDirector(user.RoleCode)
            || RoleCodes.IsSeniorManager(user.RoleCode)
            || RoleCodes.IsAdmin(user.RoleCode));

    public static bool CanViewReportExport(AppUser? user, ReportExportKind kind)
    {
        if (user == null || !CanAccess(user, AppModule.Reports))
            return false;

        if (RoleCodes.IsAdmin(user.RoleCode)
            || RoleCodes.IsDirector(user.RoleCode)
            || RoleCodes.IsSeniorManager(user.RoleCode))
            return true;

        if (RoleCodes.IsManager(user.RoleCode))
            return kind is ReportExportKind.Operational or ReportExportKind.Geography;

        return false;
    }

    public static bool CanManageFinance(AppUser? user) =>
        user != null && CanAccess(user, AppModule.Contracts);

    public static bool CanDeleteRecords(AppUser? user) =>
        user != null && (RoleCodes.IsAdmin(user.RoleCode) || RoleCodes.IsDirector(user.RoleCode));

    public static bool CanManageUsers(AppUser? user) =>
        user != null && RoleCodes.IsDirector(user.RoleCode);

    public static bool CanManageMaxBotSettings(AppUser? user) =>
        user != null && RoleCodes.IsDirector(user.RoleCode);

    public static bool IsTeacher(AppUser? user) =>
        user != null && RoleCodes.IsTeacher(user.RoleCode);

    public static bool CanViewTeacherSalary(AppUser? user) =>
        user != null && !IsTeacher(user);

    /// <summary>История изменений групп — только руководитель филиала.</summary>
    public static bool CanViewGroupHistory(AppUser? user) =>
        user != null && RoleCodes.IsDirector(user.RoleCode);

    /// <summary>Ежедневный отчёт посещаемости по группам — админ и руководитель.</summary>
    public static bool CanEditGroupDailyReport(AppUser? user) =>
        user != null && (RoleCodes.IsAdmin(user.RoleCode) || RoleCodes.IsDirector(user.RoleCode));
}
