using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    // One installment in a vendor's payment schedule.
    public class VendorPayment
    {
        [Key]
        public int PaymentId { get; set; }
        public int VendorId { get; set; }
        [JsonIgnore]
        public Vendor? Vendor { get; set; }

        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsPaid { get; set; }
        public DateTime? PaidAt { get; set; }
    }
}
