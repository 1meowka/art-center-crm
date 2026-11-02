#nullable enable
using System;

using VKR.Utilities;

namespace VKR.Models;

public sealed class RoleItem
{
    public int IdRole { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;

    public override string ToString() => RoleName;
}

public sealed class UserListItem
{
    public int IdUser { get; set; }
    public string Login { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? Email { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    public string FullName => Student.FormatFio(LastName, FirstName, MiddleName);
    public string StatusText => IsActive ? "активен" : "заблокирован";
    public string LoginText => $"@{Login}";
    public string LastLoginText => LastLoginAt.HasValue
        ? $"вход: {LastLoginAt.Value:dd.MM.yyyy HH:mm}"
        : "ещё не входил";
    public string EmailText => string.IsNullOrWhiteSpace(Email) ? "—" : Email;

    public string Initials
    {
        get
        {
            var first = string.IsNullOrWhiteSpace(FirstName) ? "?" : FirstName.Trim()[0].ToString();
            var last = string.IsNullOrWhiteSpace(LastName) ? "?" : LastName.Trim()[0].ToString();
            return $"{last}{first}".ToUpperInvariant();
        }
    }
}

public sealed class UserEditModel
{
    public int IdUser { get; set; }
    public string Login { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? Email { get; set; }
    public int IdRole { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int? IdTeacher { get; set; }
}

public sealed class UserProfileDetails
{
    public int IdUser { get; set; }
    public string Login { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? Email { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int? IdTeacher { get; set; }
    public string? TeacherFullName { get; set; }

    public string FullName => Student.FormatFio(LastName, FirstName, MiddleName);
    public bool IsTeacherRole => RoleCodes.IsTeacher(RoleCode);
}
