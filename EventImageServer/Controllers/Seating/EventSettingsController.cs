using EventImageServer.Contexts;
using EventImageServer.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventImageServer.Controllers.Seating;

[Route("Seating")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(EventOwnerFilter))]
public sealed class EventSettingsController : SeatingControllerBase
{
    public EventSettingsController(AppDbContext dbContext) : base(dbContext)
    {
    }

    [HttpPut("RsvpDeadline")]
    public async Task<IActionResult> SetRsvpDeadline([FromBody] RsvpDeadlineRequest request)
    {
        var owner = Owner;
        owner.RsvpDeadline = request.Deadline;
        await DbContext.SaveChangesAsync();
        return Ok(new { rsvpDeadline = owner.RsvpDeadline });
    }

    [HttpPut("EventDate")]
    public async Task<IActionResult> SetEventDate([FromBody] EventDateRequest request)
    {
        var owner = Owner;
        owner.EventDate = request.EventDate;
        await DbContext.SaveChangesAsync();
        return Ok(new { eventDate = owner.EventDate });
    }

    [HttpPost("WallToken")]
    public async Task<IActionResult> GenerateWallToken()
    {
        var owner = Owner;
        owner.WallToken = GenerateSecureToken();
        await DbContext.SaveChangesAsync();
        return Ok(new { wallToken = owner.WallToken });
    }

    [HttpDelete("WallToken")]
    public async Task<IActionResult> RevokeWallToken()
    {
        var owner = Owner;
        owner.WallToken = null;
        await DbContext.SaveChangesAsync();
        return Ok(new { message = "Wall token revoked." });
    }

    [HttpGet("ReminderSettings")]
    public IActionResult GetReminderSettings()
    {
        var owner = Owner;
        return Ok(new { autoRemindersEnabled = owner.AutoRemindersEnabled, reminderOffsets = owner.ReminderOffsets });
    }

    [HttpPut("ReminderSettings")]
    public async Task<IActionResult> UpdateReminderSettings([FromBody] ReminderSettingsRequest request)
    {
        var owner = Owner;
        owner.AutoRemindersEnabled = request.AutoRemindersEnabled;
        owner.ReminderOffsets = (request.ReminderOffsets ?? new List<int>())
            .Where(d => d > 0)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
        await DbContext.SaveChangesAsync();
        return Ok(new { autoRemindersEnabled = owner.AutoRemindersEnabled, reminderOffsets = owner.ReminderOffsets });
    }
}
