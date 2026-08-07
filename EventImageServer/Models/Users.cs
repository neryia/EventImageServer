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
        public DateTime? RsvpDeadline { get; set; } // Event-level RSVP deadline for this EventOwner
        public DateTime? EventDate { get; set; } // The wedding day itself; gates guest media uploads on the RSVP page

        public ICollection<UserMedia>? Media { get; set; }
    }
}
