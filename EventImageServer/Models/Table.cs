using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public enum Shape
    {
        Round,
        Square,
        Rectangle
    }

    public class Table
    {
        public int TableId { get; set; }
        public string? Name { get; set; }
        public string? Shape { get; set; }
        public string Tag { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public int CapacityOnSides { get; set; }
        public int CapacityOnTopAndBottom { get; set; }
        public ICollection<Guest>? Guests { get; set; }
        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }
    }
}
