using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    // Per-owner metadata for a guest "Category" (a free-text label on Guest.Category).
    // Categories are created implicitly when a guest is saved with a new category value,
    // and carry a display Color that can be bulk-updated for all guests sharing that value.
    public class GuestCategory
    {
        public int GuestCategoryId { get; set; }
        public string Value { get; set; } = string.Empty;
        public string Color { get; set; } = "#CCCCCC";
        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }
    }
}
