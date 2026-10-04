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
public sealed class ConstraintsController : SeatingControllerBase
{
    public ConstraintsController(AppDbContext dbContext) : base(dbContext)
    {
    }

    [HttpGet("Constraints")]
    public async Task<IActionResult> GetConstraints()
    {
        var owner = Owner;
        var constraints = await DbContext.SeatingConstraints
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();
        return Ok(constraints);
    }

    [HttpPost("Constraints")]
    public async Task<IActionResult> CreateConstraint([FromBody] SeatingConstraintRequest request)
    {
        var owner = Owner;
        if (request.GuestAId == request.GuestBId)
        {
            return BadRequest(new { message = "A guest cannot be constrained with themselves." });
        }

        var guestIds = new[] { request.GuestAId, request.GuestBId };
        var validGuestCount = await DbContext.Guests
            .CountAsync(g => g.OwnerId == owner.Id && guestIds.Contains(g.GuestId));
        if (validGuestCount != 2)
        {
            return BadRequest(new { message = "Both guests must exist and belong to you." });
        }

        var (a, b) = request.GuestAId < request.GuestBId
            ? (request.GuestAId, request.GuestBId)
            : (request.GuestBId, request.GuestAId);
        var exists = await DbContext.SeatingConstraints.AnyAsync(c =>
            c.OwnerId == owner.Id && c.GuestAId == a && c.GuestBId == b && c.Kind == request.Kind);
        if (exists)
        {
            return BadRequest(new { message = "This constraint already exists." });
        }

        var constraint = new SeatingConstraint
        {
            OwnerId = owner.Id!,
            GuestAId = a,
            GuestBId = b,
            Kind = request.Kind,
        };
        DbContext.SeatingConstraints.Add(constraint);
        await DbContext.SaveChangesAsync();
        return Ok(constraint);
    }

    [HttpDelete("Constraints/{id}")]
    public async Task<IActionResult> DeleteConstraint(int id)
    {
        var owner = Owner;
        var constraint = await DbContext.SeatingConstraints
            .FirstOrDefaultAsync(c => c.ConstraintId == id && c.OwnerId == owner.Id);
        if (constraint == null)
        {
            return NotFound(new { message = "Constraint not found." });
        }

        DbContext.SeatingConstraints.Remove(constraint);
        await DbContext.SaveChangesAsync();
        return Ok(new { message = "Constraint deleted." });
    }
}
