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
    // uploads (GuestMedia rows) with the owner's own uploads from the same
    // UploadedImages/{ownerId}/ folder (any file with no matching GuestMedia
    // row, same distinction ImagesController.GetImages uses for the owner's
    // gallery view).
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

            var now = DateTime.Now;
            var start = owner.EventDate.Value.AddHours(-2); // opens 2 hours early
            var end = owner.EventDate.Value.AddDays(2); // one extra grace day vs. the upload window
            return now >= start && now <= end;
        }

        // Deliberately has NO guest-identifying field (name, phone, etc.) —
        // this DTO is served [AllowAnonymous] to anyone holding the wall
        // token, so it must not leak who uploaded what. Id is a string since
        // owner uploads (plain files, no DB row) use their file name, while
        // guest uploads use their GuestMedia row id.
        public class WallMediaDto
        {
            public string Id { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string MediaType { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        private static string GetMediaType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".mp4" or ".webm" or ".ogg" => "video",
                _ => "image"
            };
        }

        // GET /Wall/{token}?since={iso}
        // Returns media created after `since` (or everything, if omitted),
        // newest first, capped at 100 items per call.
        [HttpGet("{token}")]
        public IActionResult GetWallMedia(string token, [FromQuery] DateTime? since)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return NotFound(new { message = "Wall not found." });
            }

            var owner = _dbContext.Clients.FirstOrDefault(u => u.WallToken == token);
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
                if (DateTime.Now > opensAt)
                {
                    return StatusCode(403, new
                    {
                        message = "This photo wall has closed.",
                        opensInSeconds = (int?)null,
                    });
                }

                var secondsUntilOpen = Math.Max(0, (int)(opensAt - DateTime.Now).TotalSeconds);
                return StatusCode(403, new
                {
                    message = "This photo wall isn't open yet.",
                    opensInSeconds = (int?)secondsUntilOpen,
                });
            }

            var guestMedia = _dbContext.GuestMedia
                .Where(m => m.OwnerId == owner.Id)
                .ToList();

            var wallItems = guestMedia
                .Select(m => new WallMediaDto
                {
                    Id = m.GuestMediaId.ToString(),
                    Url = $"/UploadedImages/{owner.Id}/{m.FileName}",
                    MediaType = m.MediaType,
                    CreatedAt = m.CreatedAt,
                })
                .ToList();

            // Owner's own uploads: any file in their folder with no matching
            // GuestMedia row. They have no DB row of their own, so the file's
            // last-write time stands in for CreatedAt.
            var guestFileNames = new HashSet<string>(guestMedia.Select(m => m.FileName));
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", owner.Id!);
            if (Directory.Exists(folderPath))
            {
                foreach (var filePath in Directory.GetFiles(folderPath))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (guestFileNames.Contains(fileName))
                    {
                        continue;
                    }

                    wallItems.Add(new WallMediaDto
                    {
                        Id = fileName,
                        Url = $"/UploadedImages/{owner.Id}/{fileName}",
                        MediaType = GetMediaType(fileName),
                        CreatedAt = System.IO.File.GetLastWriteTimeUtc(filePath),
                    });
                }
            }

            var media = wallItems
                .Where(m => !since.HasValue || m.CreatedAt > since.Value)
                .OrderByDescending(m => m.CreatedAt)
                .Take(100)
                .ToList();

            return Ok(new { media });
        }
    }
}
