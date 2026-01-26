namespace EventImageServer.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    class Sitting
    {
        public static async Task Calc()
        {
            var client = new HttpClient();
            var request = new
            {
                guests = new string[] { "Alice", "Bob", "Charlie", "David", "Eve", "Frank" },
                priority = new Dictionary<string, string[]>{
                {"Alice", new string[]{"Bob","Charlie"}},
                {"Bob", new string[]{"Alice"}},
                {"Charlie", new string[]{"Alice","David"}},
                {"David", new string[]{"Charlie"}},
                {"Eve", new string[]{"Frank"}},
                {"Frank", new string[]{"Eve"}}
            },
                num_tables = 2,
                seats_per_table = 3
            };

            var response = await client.PostAsJsonAsync("http://localhost:8000/seat", request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Seating result from Python API:");
            Console.WriteLine(json);
        }
    }

}
