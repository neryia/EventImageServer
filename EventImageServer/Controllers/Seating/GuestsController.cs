using EventImageServer.Contexts;
using EventImageServer.Filters;
using EventImageServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Controllers.Seating;

[Route("Seating")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(EventOwnerFilter))]
public sealed class GuestsController : SeatingControllerBase
{
    public GuestsController(AppDbContext dbContext) : base(dbContext)
    {
    }

    [HttpPost("Guests")]
    public async Task<IActionResult> CreateGuest([FromBody] GuestRequest request)
    {
        var owner = Owner;
        if (request.TableId.HasValue)
        {
            var assignError = await ValidateTableAssignmentAsync(owner.Id!, request.TableId.Value, request.NumberOfGuests);
            if (assignError != null)
            {
                return assignError;
            }
        }

        var guest = new Guest
        {
            Name = request.Name ?? string.Empty,
            Category = request.Category,
            Tag = request.Tag,
            NumberOfGuests = request.NumberOfGuests,
            TableId = request.TableId,
            Phone = request.Phone,
            OwnerId = owner.Id
        };

        DbContext.Guests.Add(guest);
        await EnsureCategoryExistsAsync(owner.Id!, request.Category);
        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }

    [HttpPut("Guests/{id}")]
    public async Task<IActionResult> UpdateGuest(int id, [FromBody] GuestRequest request)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        guest.Name = request.Name ?? string.Empty;
        guest.Category = request.Category;
        guest.Tag = request.Tag;
        guest.NumberOfGuests = request.NumberOfGuests;
        guest.Phone = request.Phone;
        guest.TableId = request.TableId.HasValue &&
            await TableHasCapacityAsync(owner.Id!, request.TableId.Value, request.NumberOfGuests, excludeGuestId: guest.GuestId)
            ? request.TableId
            : null;

        await EnsureCategoryExistsAsync(owner.Id!, request.Category);
        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }

    [HttpDelete("Guests/{id}")]
    public async Task<IActionResult> DeleteGuest(int id)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        var relatedConstraints = await DbContext.SeatingConstraints
            .Where(c => c.OwnerId == owner.Id && (c.GuestAId == id || c.GuestBId == id))
            .ToListAsync();
        DbContext.SeatingConstraints.RemoveRange(relatedConstraints);
        DbContext.Guests.Remove(guest);
        await DbContext.SaveChangesAsync();
        return Ok(new { message = "Guest deleted." });
    }

    [HttpPut("Category/{categoryValue}/Color")]
    public async Task<IActionResult> UpdateCategoryColor(string categoryValue, [FromBody] CategoryColorRequest request)
    {
        var owner = Owner;
        if (string.IsNullOrWhiteSpace(categoryValue))
        {
            return BadRequest(new { message = "Category value is required." });
        }
        if (string.IsNullOrWhiteSpace(request.Color))
        {
            return BadRequest(new { message = "Color is required." });
        }

        var category = await DbContext.GuestCategories
            .FirstOrDefaultAsync(c => c.OwnerId == owner.Id && c.Value == categoryValue);
        if (category == null)
        {
            category = new GuestCategory
            {
                OwnerId = owner.Id,
                Value = categoryValue,
                Color = request.Color
            };
            DbContext.GuestCategories.Add(category);
        }
        else
        {
            category.Color = request.Color;
        }

        await DbContext.SaveChangesAsync();
        return Ok(category);
    }

    [HttpPut("Guests/{id}/Table/{tableId}")]
    public async Task<IActionResult> AssignGuestToTable(int id, int tableId)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        var assignError = await ValidateTableAssignmentAsync(owner.Id!, tableId, guest.NumberOfGuests, excludeGuestId: guest.GuestId);
        if (assignError != null)
        {
            return assignError;
        }

        guest.TableId = tableId;
        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }

    [HttpPut("Guests/{id}/RsvpStatus")]
    public async Task<IActionResult> UpdateRsvpStatus(int id, [FromBody] RsvpStatusUpdateRequest request)
    {
        var owner = Owner;
        var guest = await DbContext.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }
        if (!Enum.IsDefined(typeof(RsvpStatus), request.Status))
        {
            return BadRequest(new { message = "Invalid RSVP status." });
        }
        if (request.NumberOfGuests < 0)
        {
            return BadRequest(new { message = "Party size cannot be negative." });
        }

        guest.RsvpStatus = request.Status;
        guest.RsvpRespondedAt = DateTime.UtcNow;
        if (request.Status == RsvpStatus.Declined)
        {
            guest.TableId = null;
            guest.NumberOfGuests = request.NumberOfGuests;
            guest.ConfirmedCount = request.NumberOfGuests;
        }
        else
        {
            if (guest.TableId.HasValue &&
                !await TableHasCapacityAsync(owner.Id!, guest.TableId.Value, request.NumberOfGuests, excludeGuestId: guest.GuestId))
            {
                guest.TableId = null;
            }

            guest.NumberOfGuests = request.NumberOfGuests;
            guest.ConfirmedCount = request.Status == RsvpStatus.Confirmed
                ? request.NumberOfGuests
                : guest.ConfirmedCount;
        }

        await DbContext.SaveChangesAsync();
        return Ok(guest);
    }
}
