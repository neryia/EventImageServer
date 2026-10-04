using System.ComponentModel.DataAnnotations;

namespace EventImageServer.Models
{
    public class GuestRequest
    {
        public string? Name { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public int NumberOfGuests { get; set; } = 1;
        public int? TableId { get; set; }

        [RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Phone number must be in E.164 format.")]
        public string? Phone { get; set; }
    }
}
