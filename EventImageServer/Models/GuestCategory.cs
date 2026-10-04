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
        // Display name shown in the UI. Value is a unique slug, so several categories
        // (e.g. one per side) can share the same label.
        public string? Label { get; set; }
        public string Color { get; set; } = "#CCCCCC";
        // Bride/groom side of every guest in this category (Both = can sit anywhere).
        public EventSide Side { get; set; } = EventSide.Both;
        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }
    }
}
