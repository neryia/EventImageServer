using EventImageServer.Contexts;
using EventImageServer.Filters;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Controllers.Seating;

[Route("Seating")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(EventOwnerFilter))]
public sealed class GuestMessagingController : SeatingControllerBase
{
    private readonly TwilioMessagingService _twilio;
    private readonly MessageDispatchQueue _messageDispatchQueue;

    public GuestMessagingController(AppDbContext dbContext, TwilioMessagingService twilio, MessageDispatchQueue messageDispatchQueue) : base(dbContext)
    {
        _twilio = twilio;
        _messageDispatchQueue = messageDispatchQueue;
    }

    [HttpPost("Guests/{id}/GenerateLink")]
    public async Task<IActionResult> GenerateLink(int id)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        if (string.IsNullOrEmpty(guest.RsvpToken))
        {
            guest.RsvpToken = GenerateSecureToken();
            guest.RsvpTokenCreatedAt = DateTime.UtcNow;
            await DbContext.SaveChangesAsync();
        }

        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _twilio.PublicBaseUrl.TrimEnd('/');
        return Ok(new { link = $"{baseUrl}/rsvp/{guest.RsvpToken}", token = guest.RsvpToken });
    }

    [HttpPost("Invites/Send")]
    public async Task<IActionResult> SendInvites([FromBody] SendMessagesRequest request)
    {
        return await SendGuestMessages(request, MessageType.Invite);
    }

    [HttpPost("Reminders/Send")]
    public async Task<IActionResult> SendReminders([FromBody] SendMessagesRequest request)
    {
        return await SendGuestMessages(request, MessageType.Reminder);
    }

    private async Task<IActionResult> SendGuestMessages(SendMessagesRequest request, MessageType type)
    {
        var owner = Owner;
        var guestsQuery = DbContext.Guests.Where(g => g.OwnerId == owner.Id);
        if (request.GuestIds != null && request.GuestIds.Count > 0)
        {
            var ids = request.GuestIds.ToHashSet();
            guestsQuery = guestsQuery.Where(g => ids.Contains(g.GuestId));
        }

        var guests = (await guestsQuery.ToListAsync())
            .Where(g => !g.OptedOut && !string.IsNullOrWhiteSpace(g.Phone))
            .ToList();
        if (type == MessageType.Reminder)
        {
            guests = guests.Where(g => g.RsvpStatus == RsvpStatus.Pending).ToList();
            if (owner.RsvpDeadline.HasValue && DateTime.UtcNow > owner.RsvpDeadline.Value)
            {
                return BadRequest(new { message = "RSVP deadline has already passed." });
            }
        }

        var windowStart = DateTime.UtcNow.AddHours(-24);
        var recentMessageCount = await DbContext.MessageLogs
            .CountAsync(m => m.OwnerId == owner.Id && m.SentAt >= windowStart);
        if (recentMessageCount + guests.Count > 200)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "The 24-hour message limit is 200. Try again after older messages expire."
            });
        }

        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _twilio.PublicBaseUrl.TrimEnd('/');
        var pendingDispatches = new List<(int GuestId, MessageLog Log, string Body)>();
        foreach (var guest in guests)
        {
            if (string.IsNullOrEmpty(guest.RsvpToken))
            {
                guest.RsvpToken = GenerateSecureToken();
                guest.RsvpTokenCreatedAt = DateTime.UtcNow;
            }

            var link = $"{baseUrl}/rsvp/{guest.RsvpToken}";
            var body = type == MessageType.Invite
                ? $"You're invited! Please RSVP here: {link}"
                : $"Reminder: please RSVP here: {link}";
            var log = new MessageLog
            {
                OwnerId = owner.Id,
                GuestId = guest.GuestId,
                Channel = request.Channel,
                Type = type,
                To = guest.Phone!,
                SentAt = DateTime.UtcNow
            };
            DbContext.MessageLogs.Add(log);
            pendingDispatches.Add((guest.GuestId, log, body));
        }

        await DbContext.SaveChangesAsync();
        foreach (var dispatch in pendingDispatches)
        {
            await _messageDispatchQueue.EnqueueAsync(new MessageDispatchJob(
                dispatch.Log.MessageLogId,
                request.Channel,
                dispatch.Log.To,
                dispatch.Body));
        }

        var results = pendingDispatches
            .Select(dispatch => new { guestId = dispatch.GuestId, status = "queued" })
            .ToList();
        return Accepted(new { queued = results.Count, results });
    }
}
