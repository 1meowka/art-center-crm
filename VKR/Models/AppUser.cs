namespace VKR.Models
{
    public class AppUser
    {
        public int IdUser { get; set; }
        public string Login { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string? Email { get; set; }
        public string RoleCode { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public int? IdTeacher { get; set; }

        public string FullName => $"{LastName} {FirstName} {MiddleName}".Trim();
    }
}