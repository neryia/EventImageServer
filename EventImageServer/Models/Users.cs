namespace EventImageServer.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.ComponentModel.DataAnnotations.Schema;

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
        public string? WallToken { get; set; } // Secret token gating the public live photo wall (null = disabled)

        // Automatic RSVP reminders (Phase 5.A): when enabled, ReminderScheduler
        // sends a reminder to still-pending guests N days before RsvpDeadline,
        // for each offset in ReminderOffsets (e.g. [30,14,7,2]).
        public bool AutoRemindersEnabled { get; set; }

        [JsonIgnore]
        public string? ReminderOffsetsJson { get; set; }

        [NotMapped]
        public List<int> ReminderOffsets
        {
            get => string.IsNullOrEmpty(ReminderOffsetsJson)
                ? new List<int>()
                : JsonSerializer.Deserialize<List<int>>(ReminderOffsetsJson) ?? new List<int>();
            set => ReminderOffsetsJson = value == null ? null : JsonSerializer.Serialize(value);
        }

        public ICollection<UserMedia>? Media { get; set; }
    }
}
