using EventImageServer.Contexts;
using EventImageServer.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Controllers.Seating;

[Route("Seating")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(EventOwnerFilter))]
public sealed class CheckInController : SeatingControllerBase
{
    public CheckInController(AppDbContext dbContext) : base(dbContext)
    {
    }

    [HttpPut("Guests/{id}/CheckIn")]
    public async Task<IActionResult> CheckInGuest(int id, [FromBody] CheckInRequest request)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        guest.CheckedInAt = DateTime.UtcNow;
        guest.CheckedInCount = request.CheckedInCount ?? guest.ConfirmedCount ?? guest.NumberOfGuests;
        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }

    [HttpDelete("Guests/{id}/CheckIn")]
    public async Task<IActionResult> UndoCheckInGuest(int id)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        guest.CheckedInAt = null;
        guest.CheckedInCount = null;
        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }
}
