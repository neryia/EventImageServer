namespace EventImageServer.Models
{
    using System.ComponentModel.DataAnnotations;

    public enum ConstraintKind
    {
        Together,
        Apart
    }

    public class SeatingConstraint
    {
        [Key]
        public int ConstraintId { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public int GuestAId { get; set; }
        public int GuestBId { get; set; }
        public ConstraintKind Kind { get; set; }
    }
}
