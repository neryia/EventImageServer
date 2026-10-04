namespace EventImageServer.Models
{
    // Metadata for files uploaded by the event owner. Owner uploads are
    // private by default; only explicitly approved items appear on the wall.
    public class OwnerMedia
    {
        public int OwnerMediaId { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool ShowOnWall { get; set; }
    }
}