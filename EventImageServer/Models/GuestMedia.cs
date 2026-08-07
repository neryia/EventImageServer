using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    // A single photo/video a guest uploaded via their RSVP link. Lets the
    // guest see and manage only their own uploads (list/delete), while the
    // underlying file still lives in the owner's shared UploadedImages/{ownerId}
    // folder so it renders in the owner's gallery too.
    public class GuestMedia
    {
        public int GuestMediaId { get; set; }
        public int GuestId { get; set; }
        [JsonIgnore]
        public Guest? Guest { get; set; }
        public string? OwnerId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty; // image / video
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
