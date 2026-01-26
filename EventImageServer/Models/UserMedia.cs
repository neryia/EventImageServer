namespace EventImageServer.Models
{
    public class UserMedia
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } // Firebase UID
        public Users User { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string MediaType { get; set; } // image / video
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
