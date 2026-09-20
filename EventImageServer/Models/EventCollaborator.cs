namespace EventImageServer.Models
{
    using System.ComponentModel.DataAnnotations;

    public enum CollaboratorRole
    {
        CoOwner,
        Viewer
    }

    public class EventCollaborator
    {
        [Key]
        public int CollaboratorId { get; set; }
        public string OwnerId { get; set; } = string.Empty; // the event owner who sent the invite
        public string? CollaboratorUserId { get; set; } // set once accepted (Firebase UID)
        public string CollaboratorEmail { get; set; } = string.Empty;
        public CollaboratorRole Role { get; set; } = CollaboratorRole.Viewer;
        public string InviteToken { get; set; } = string.Empty;
        public DateTime InvitedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(14);
        public DateTime? AcceptedAt { get; set; }
    }
}
