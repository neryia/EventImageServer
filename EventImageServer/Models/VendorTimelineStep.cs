using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class VendorTimelineStep
    {
        [Key]
        public int StepId { get; set; }
        public int VendorId { get; set; }
        [JsonIgnore]
        public Vendor? Vendor { get; set; }
        public TimelineStepType Step { get; set; }
        public bool IsDone { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
