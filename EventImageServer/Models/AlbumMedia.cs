using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    // One media file's membership in an album. A single file (identified by
    // FileName, scoped to the album's OwnerId) can belong to multiple albums.
    public class AlbumMedia
    {
        public int AlbumMediaId { get; set; }
        public int AlbumId { get; set; }
        [JsonIgnore]
        public Album? Album { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
