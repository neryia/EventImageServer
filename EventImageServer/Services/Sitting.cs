namespace EventImageServer.Services
{
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    public class SeatingGuestDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Amount { get; set; }
        [JsonPropertyName("table_id")]
        public string? TableId { get; set; }
    }

    public class SeatingTableDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int Seats { get; set; }
    }

    public class SeatingArrangeRequest
    {
        public List<SeatingGuestDto> Guests { get; set; } = new();
        public List<SeatingTableDto> Tables { get; set; } = new();
    }

    public class TableAssignmentDto
    {
        [JsonPropertyName("table_id")]
        public string TableId { get; set; } = string.Empty;
        [JsonPropertyName("table_name")]
        public string? TableName { get; set; }
        [JsonPropertyName("guest_ids")]
        public List<string> GuestIds { get; set; } = new();
        [JsonPropertyName("seats_used")]
        public int SeatsUsed { get; set; }
        [JsonPropertyName("seats_free")]
        public int SeatsFree { get; set; }
        public List<string> Categories { get; set; } = new();
    }

    public class UnseatedGuestDto
    {
        public string Id { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class ArrangeScoreDto
    {
        [JsonPropertyName("mixing_cost")]
        public int MixingCost { get; set; }
        [JsonPropertyName("tables_used")]
        public int TablesUsed { get; set; }
        [JsonPropertyName("guests_seated")]
        public int GuestsSeated { get; set; }
        [JsonPropertyName("people_seated")]
        public int PeopleSeated { get; set; }
    }

    public class ArrangeResponseDto
    {
        public List<TableAssignmentDto> Assignments { get; set; } = new();
        public List<UnseatedGuestDto> Unseated { get; set; } = new();
        public ArrangeScoreDto Score { get; set; } = new();
    }

    class Sitting
    {
        // Sends guests/tables to the Python seating service and returns the
        // arrangement (per-table assignments, unseated guests and a score).
        public static async Task<ArrangeResponseDto> Arrange(SeatingArrangeRequest request)
        {
            var client = new HttpClient();

            var response = await client.PostAsJsonAsync("http://localhost:8000/seating/arrange", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ArrangeResponseDto>();
            return result ?? new ArrangeResponseDto();
        }
    }

}
