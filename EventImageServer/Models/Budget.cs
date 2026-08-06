using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class Budget
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty; // Firebase UID, unique per user
        public decimal TotalBudget { get; set; } = 0;

        [JsonIgnore]
        public List<BudgetCategory> Categories { get; set; } = new();

        [JsonIgnore]
        public List<BudgetExpense> Expenses { get; set; } = new();
    }
}
