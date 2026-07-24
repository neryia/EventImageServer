using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class VendorAttachment
    {
        [Key]
        public int AttachmentId { get; set; }
        public int VendorId { get; set; }
        [JsonIgnore]
        public Vendor? Vendor { get; set; }
        public VendorAttachmentType Type { get; set; }
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
