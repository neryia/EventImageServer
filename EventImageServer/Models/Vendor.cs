using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class Vendor
    {
        public int VendorId { get; set; }

        // Basic info
        public string Name { get; set; } = string.Empty; // business name
        public string ContactName { get; set; } = string.Empty;
        public VendorCategory Category { get; set; }
        public VendorStatus Status { get; set; } = VendorStatus.NotStarted;

        // Contact details
        public string Phone { get; set; } = string.Empty;
        public string WhatsApp { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Instagram { get; set; } = string.Empty;

        // Financial info
        public decimal AgreedPrice { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTime? NextPaymentDate { get; set; }
        [NotMapped]
        public decimal Balance => AgreedPrice - PaidAmount;

        // Notes
        public string Notes { get; set; } = string.Empty;
        public string QuestionsToAsk { get; set; } = string.Empty;
        public string Promises { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }

        public ICollection<VendorTimelineStep>? Timeline { get; set; }
        public ICollection<VendorAttachment>? Attachments { get; set; }
    }
}
