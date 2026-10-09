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
public sealed class FloorPlanController : SeatingControllerBase
{
    public FloorPlanController(AppDbContext dbContext) : base(dbContext)
    {
    }

    // Saves canvas size, table positions and the complete venue element list
    // in one transaction. Elements missing from the request are deleted.
    [HttpPut("FloorPlan")]
    public async Task<IActionResult> UpdateFloorPlan([FromBody] FloorPlanRequest request)
    {
        var owner = Owner;

        if (!FloorPlanValidation.IsValidCanvas(request.Width) || !FloorPlanValidation.IsValidCanvas(request.Height))
        {
            return BadRequest(new { message = $"Canvas size must be between {FloorPlanValidation.MinCanvas} and {FloorPlanValidation.MaxCanvas}." });
        }

        if (request.Elements.Count > FloorPlanValidation.MaxElements)
        {
            return BadRequest(new { message = "Too many venue elements." });
        }

        if (request.Tables.Select(t => t.TableId).Distinct().Count() != request.Tables.Count)
        {
            return BadRequest(new { message = "Each table can only be listed once." });
        }

        if (request.Elements.Any(e => string.IsNullOrWhiteSpace(e.ClientKey))
            || request.Elements.Select(e => e.ClientKey).Distinct().Count() != request.Elements.Count)
        {
            return BadRequest(new { message = "Element keys must be unique." });
        }

        foreach (var t in request.Tables)
        {
            var error = FloorPlanValidation.ValidatePosition(t.PositionX, t.PositionY, t.Rotation);
            if (error != null) return BadRequest(new { message = error });
        }

        foreach (var e in request.Elements)
        {
            var error = FloorPlanValidation.ValidateElement(e);
            if (error != null) return BadRequest(new { message = error });
        }

        await using var transaction = await DbContext.Database.BeginTransactionAsync();

        var tables = await DbContext.Tables.Where(t => t.OwnerId == owner.Id).ToDictionaryAsync(t => t.TableId);
        if (request.Tables.Any(t => !tables.ContainsKey(t.TableId)))
        {
            return BadRequest(new { message = "One or more tables do not belong to this event." });
        }

        var existing = await DbContext.VenueElements.Where(e => e.OwnerId == owner.Id).ToDictionaryAsync(e => e.ElementId);
        if (request.Elements.Any(e => e.ElementId.HasValue && !existing.ContainsKey(e.ElementId.Value)))
        {
            return BadRequest(new { message = "One or more venue elements do not belong to this event." });
        }

        var ownerRow = await DbContext.Clients.FirstAsync(c => c.Id == owner.Id);
        if (request.Width.HasValue) ownerRow.FloorPlanWidth = request.Width.Value;
        if (request.Height.HasValue) ownerRow.FloorPlanHeight = request.Height.Value;

        foreach (var t in request.Tables)
        {
            var table = tables[t.TableId];
            table.PositionX = t.PositionX;
            table.PositionY = t.PositionY;
            table.Rotation = FloorPlanValidation.NormalizeRotation(t.Rotation);
        }

        var byKey = new Dictionary<string, VenueElement>();
        var keptIds = new HashSet<int>();
        foreach (var e in request.Elements)
        {
            VenueElement element;
            if (e.ElementId.HasValue)
            {
                element = existing[e.ElementId.Value];
                keptIds.Add(element.ElementId);
            }
            else
            {
                element = new VenueElement { OwnerId = owner.Id! };
                DbContext.VenueElements.Add(element);
            }

            element.Kind = e.Kind;
            element.Label = e.Label;
            element.X = e.X;
            element.Y = e.Y;
            element.Width = e.Width;
            element.Height = e.Height;
            element.Rotation = FloorPlanValidation.NormalizeRotation(e.Rotation);
            byKey[e.ClientKey] = element;
        }

        DbContext.VenueElements.RemoveRange(existing.Values.Where(e => !keptIds.Contains(e.ElementId)));
        await DbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var elementKeyMap = byKey.ToDictionary(kv => kv.Key, kv => kv.Value.ElementId);
        return Ok(new
        {
            floorPlan = new { width = ownerRow.FloorPlanWidth, height = ownerRow.FloorPlanHeight },
            elementKeyMap
        });
    }
}
