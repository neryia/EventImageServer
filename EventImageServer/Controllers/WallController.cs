using EventImageServer.Contexts;
using EventImageServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Controllers
{
    // Public, unauthenticated endpoint powering the live photo wall
    // (/wall/{token} on the client) — a read-only slideshow mixing guest
    // uploads (GuestMedia rows) with owner uploads explicitly approved for
    // public display (OwnerMedia rows where ShowOnWall is true).
    [Route("[controller]")]
    [ApiController]
    [AllowAnonymous]
    [EnableRateLimiting("rsvp")]
    public class WallController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public WallController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Mirrors RsvpController.IsUploadWindowOpen, plus a little extra
        // grace so the wall stays live a bit past the raw upload cutoff for
        // guests who are still viewing it at the reception. Also opens a
        // couple hours early so it can be put up on a screen at the venue
        // before the event officially starts.
        private static bool IsWallOpen(Users? owner)
        {
            if (owner?.EventDate == null)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            var start = owner.EventDate.Value.AddHours(-2); // opens 2 hours early
            var end = owner.EventDate.Value.AddDays(2); // one extra grace day vs. the upload window
            return now >= start && now <= end;
        }

        // Deliberately has NO guest-identifying field (name, phone, etc.) —
        // this DTO is served [AllowAnonymous] to anyone holding the wall
        // token, so it must not leak who uploaded what. Id is a string to
        // accommodate both guest and owner media record identifiers.
        public class WallMediaDto
        {
            public string Id { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string MediaType { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        // GET /Wall/{token}?since={iso}
        // Returns media created after `since` (or everything, if omitted),
        // newest first, capped at 100 items per call.
        [HttpGet("{token}")]
        public async Task<IActionResult> GetWallMedia(string token, [FromQuery] DateTime? since)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return NotFound(new { message = "Wall not found." });
            }

            var owner = await _dbContext.Clients.AsNoTracking().FirstOrDefaultAsync(u => u.WallToken == token);
            if (owner == null)
            {
                return NotFound(new { message = "Wall not found." });
            }

            if (!IsWallOpen(owner))
            {
                // opensInSeconds lets the client render a live countdown to
                // the moment the wall opens without the response ever
                // containing the event's actual calendar date/time — the
                // wall token is effectively a public secret, so the event
                // date itself must not be derivable from it.
                // Two distinct "not open" cases: no event date set yet at
                // all (nothing to count down to), vs. a set date that's
                // either still upcoming (show countdown) or already past
                // (event over, no countdown to show either).
                if (owner.EventDate == null)
                {
                    return StatusCode(403, new
                    {
                        message = "This photo wall isn't open yet.",
                        opensInSeconds = (int?)null,
                    });
                }

                var opensAt = owner.EventDate.Value.AddHours(-2);
                var now = DateTime.UtcNow;
                if (now > opensAt)
                {
                    return StatusCode(403, new
                    {
                        message = "This photo wall has closed.",
                        opensInSeconds = (int?)null,
                    });
                }

                var secondsUntilOpen = Math.Max(0, (int)(opensAt - now).TotalSeconds);
                return StatusCode(403, new
                {
                    message = "This photo wall isn't open yet.",
                    opensInSeconds = (int?)secondsUntilOpen,
                });
            }

            var guestMedia = await _dbContext.GuestMedia.AsNoTracking()
                .Where(m => m.OwnerId == owner.Id && (!since.HasValue || m.CreatedAt > since.Value))
                .ToListAsync();

            var wallItems = guestMedia
                .Select(m => new WallMediaDto
                {
                    Id = m.GuestMediaId.ToString(),
                    Url = $"/UploadedImages/{owner.Id}/{m.FileName}",
                    MediaType = m.MediaType,
                    CreatedAt = m.CreatedAt,
                })
                .ToList();

            // Only owner uploads explicitly approved for the public wall are
            // included. Untracked legacy files and private uploads stay hidden.
            var ownerMedia = await _dbContext.OwnerMedia.AsNoTracking()
                .Where(m => m.OwnerId == owner.Id && m.ShowOnWall
                    && (!since.HasValue || m.CreatedAt > since.Value)
                    && !_dbContext.GuestMedia.Any(g => g.OwnerId == owner.Id && g.FileName == m.FileName))
                .ToListAsync();
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", owner.Id!);
            if (Directory.Exists(folderPath))
            {
                foreach (var item in ownerMedia)
                {
                    var filePath = Path.Combine(folderPath, item.FileName);
                    if (!System.IO.File.Exists(filePath))
                    {
                        continue;
                    }

                    wallItems.Add(new WallMediaDto
                    {
                        Id = item.OwnerMediaId.ToString(),
                        Url = $"/UploadedImages/{owner.Id}/{item.FileName}",
                        MediaType = item.MediaType,
                        CreatedAt = item.CreatedAt,
                    });
                }
            }

            var media = wallItems
                .OrderByDescending(m => m.CreatedAt)
                .Take(100)
                .ToList();

            return Ok(new { media });
        }
    }
}
