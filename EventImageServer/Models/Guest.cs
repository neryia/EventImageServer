using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public class Guest
    {
        public int GuestId { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }       
        public string Tag { get; set; } = string.Empty;
        public int NumberOfGuests { get; set; }
        public int? TableId { get; set; }
        [JsonIgnore]
        public Table? Table { get; set; }
        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }
    }
}
