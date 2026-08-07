using System.Text.Json.Serialization;

namespace EventImageServer.Models
{
    public enum MessageChannel
    {
        Sms,
        WhatsApp
    }

    public enum MessageType
    {
        Invite,
        Reminder
    }

    // Records every outbound Twilio message (invite/reminder) and tracks
    // its delivery status as reported by the Twilio status callback webhook.
    public class MessageLog
    {
        public int MessageLogId { get; set; }
        public string? OwnerId { get; set; } // Firebase UID of the EventOwner
        [JsonIgnore]
        public Users? Owner { get; set; }
        public int GuestId { get; set; }
        [JsonIgnore]
        public Guest? Guest { get; set; }
        public MessageChannel Channel { get; set; }
        public MessageType Type { get; set; }
        public string To { get; set; } = string.Empty;
        public string? TwilioSid { get; set; }
        public string Status { get; set; } = "queued";
        public string? ErrorCode { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
