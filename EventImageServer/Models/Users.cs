namespace EventImageServer.Models
{
    public enum RoleType
    {
        Admin,
        EventOwner,
        User
    }
    public class Users
    {
        public string? Id { get; set; } // Firebase UID
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public RoleType? Role { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<UserMedia>? Media { get; set; }
    }
}
