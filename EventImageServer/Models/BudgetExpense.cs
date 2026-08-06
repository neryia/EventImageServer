using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class BudgetExpense
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BudgetId { get; set; } = string.Empty; // FK -> Budget

        [JsonIgnore]
        public Budget? Budget { get; set; }

        public string Name { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty; // FK -> BudgetCategory
        
        [JsonIgnore]
        public BudgetCategory? Category { get; set; }

        public decimal Amount { get; set; } = 0;
        public decimal PaidAmount { get; set; } = 0;
        public DateTime? DueDate { get; set; }
        public string? VendorId { get; set; } // Optional link to Vendor
    }
}
