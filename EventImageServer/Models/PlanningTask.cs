namespace EventImageServer.Models
{
    using System.ComponentModel.DataAnnotations;

    public class PlanningTask
    {
        [Key]
        public int TaskId { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsDone { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int? VendorId { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
