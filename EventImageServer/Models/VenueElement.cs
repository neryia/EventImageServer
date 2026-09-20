namespace EventImageServer.Models
{
    using System.ComponentModel.DataAnnotations;

    public enum VenueElementKind
    {
        Stage,
        DanceFloor,
        Bar,
        Entrance,
        Dj,
        Custom
    }

    public class VenueElement
    {
        [Key]
        public int ElementId { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public VenueElementKind Kind { get; set; }
        public string? Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 80;
        public double Height { get; set; } = 80;
        public double Rotation { get; set; }
    }
}
