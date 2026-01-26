namespace EventImageServer.Models
{
    public class Users
    {
        public string Id { get; set; } // Firebase UID
        public string Email { get; set; }
        public string FullName { get; set; }
        public int RoleId { get; set; } = 2; // Default role = User
        public Role Role { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<UserMedia> Media { get; set; }
    }
}
