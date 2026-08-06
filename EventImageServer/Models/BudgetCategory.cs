using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class BudgetCategory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BudgetId { get; set; } = string.Empty; // FK -> Budget
        
        [JsonIgnore]
        public Budget? Budget { get; set; }

        public string Name { get; set; } = string.Empty;
        public decimal PlannedAmount { get; set; } = 0;
        public int? LinkedVendorCategory { get; set; } // Maps to VendorCategory enum 0-17

        [JsonIgnore]
        public List<BudgetExpense> Expenses { get; set; } = new();
    }
}
