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
public sealed class VenueController : SeatingControllerBase
{
    public VenueController(AppDbContext dbContext) : base(dbContext)
    {
    }

    [HttpGet("VenueElements")]
    public async Task<IActionResult> GetVenueElements()
    {
        var owner = Owner;
        var elements = await DbContext.VenueElements.AsNoTracking()
            .Where(e => e.OwnerId == owner.Id)
            .ToListAsync();
        return Ok(elements);
    }

    [HttpPost("VenueElements")]
    public async Task<IActionResult> CreateVenueElement([FromBody] VenueElementRequest request)
    {
        var owner = Owner;
        var validationError = FloorPlanValidation.ValidateElement(request);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var element = new VenueElement
        {
            OwnerId = owner.Id!,
            Kind = request.Kind,
            Label = request.Label,
            X = request.X,
            Y = request.Y,
            Width = request.Width,
            Height = request.Height,
            Rotation = FloorPlanValidation.NormalizeRotation(request.Rotation),
        };
        DbContext.VenueElements.Add(element);
        await DbContext.SaveChangesAsync();
        return Ok(element);
    }

    [HttpPut("VenueElements/{id}")]
    public async Task<IActionResult> UpdateVenueElement(int id, [FromBody] VenueElementRequest request)
    {
        var owner = Owner;
        var validationError = FloorPlanValidation.ValidateElement(request);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var element = await DbContext.VenueElements.FirstOrDefaultAsync(e => e.ElementId == id && e.OwnerId == owner.Id);
        if (element == null)
        {
            return NotFound(new { message = "Venue element not found." });
        }

        element.Kind = request.Kind;
        element.Label = request.Label;
        element.X = request.X;
        element.Y = request.Y;
        element.Width = request.Width;
        element.Height = request.Height;
        element.Rotation = FloorPlanValidation.NormalizeRotation(request.Rotation);
        await DbContext.SaveChangesAsync();
        return Ok(element);
    }

    [HttpDelete("VenueElements/{id}")]
    public async Task<IActionResult> DeleteVenueElement(int id)
    {
        var owner = Owner;
        var element = await DbContext.VenueElements.FirstOrDefaultAsync(e => e.ElementId == id && e.OwnerId == owner.Id);
        if (element == null)
        {
            return NotFound(new { message = "Venue element not found." });
        }

        DbContext.VenueElements.Remove(element);
        await DbContext.SaveChangesAsync();
        return Ok(new { message = "Venue element deleted." });
    }
}
