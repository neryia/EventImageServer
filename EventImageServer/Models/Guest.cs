using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

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

        // RSVP
        public string? Phone { get; set; } // E.164 format, used for SMS/WhatsApp
        public RsvpStatus RsvpStatus { get; set; } = RsvpStatus.Pending;
        [JsonIgnore]
        public string? RsvpToken { get; set; } // secret token, never serialized back to owner's client
        public DateTime? RsvpTokenCreatedAt { get; set; }
        public DateTime? RsvpOpenedAt { get; set; }
        public DateTime? RsvpRespondedAt { get; set; }
        public int? ConfirmedCount { get; set; }
        public bool OptedOut { get; set; }

        // Meal preference counts submitted by the guest on the RSVP page (e.g.
        // { "regular": 2, "vegan": 1 }), stored as JSON text since meal types are
        // an open-ended, client-defined set. Exposed as a dictionary so the
        // owner's client (and this same JSON API) can read/write it directly as
        // `mealCounts` without needing to know about the underlying storage.
        [JsonIgnore]
        public string? MealCountsJson { get; set; }

        [NotMapped]
        public Dictionary<string, int>? MealCounts
        {
            get => string.IsNullOrEmpty(MealCountsJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, int>>(MealCountsJson);
            set => MealCountsJson = value == null ? null : JsonSerializer.Serialize(value);
        }

        // Free-text note the guest left on the RSVP page (dietary restrictions,
        // song requests, etc.) — separate from `Tag`, which is the owner's own
        // note about the guest.
        public string? RsvpNote { get; set; }

        // Counts of photos/videos this guest has uploaded via their RSVP link
        // (wedding-day media upload feature). Enforces a per-guest quota.
        public int GuestPhotoUploadCount { get; set; }
        public int GuestVideoUploadCount { get; set; }
    }
}
