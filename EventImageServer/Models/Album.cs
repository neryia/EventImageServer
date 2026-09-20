using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    // A named, themed collection of the owner's existing media (photos/videos),
    // presented as a page-flip photo-book on the frontend. Membership is tracked
    // separately in AlbumMedia (many-to-many by FileName), since owner-uploaded
    // files aren't tracked in any DB table themselves — they just live on disk
    // under UploadedImages/{OwnerId}/.
    public class Album
    {
        public int AlbumId { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Theme { get; set; } = "classic"; // classic | romantic | modern
        public string? CoverFileName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public List<AlbumMedia> Items { get; set; } = new();
    }
}
