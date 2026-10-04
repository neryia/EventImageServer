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
public sealed class TablesController : SeatingControllerBase
{
    private readonly SeatingServiceClient _seatingServiceClient;

    public TablesController(AppDbContext dbContext, SeatingServiceClient seatingServiceClient) : base(dbContext)
    {
        _seatingServiceClient = seatingServiceClient;
    }

    [HttpPut("Layout")]
    public async Task<IActionResult> UpdateLayout([FromBody] LayoutRequest request)
    {
        var owner = Owner;
        if (request.Tables.Any(t => string.IsNullOrWhiteSpace(t.ClientKey)) ||
            request.Tables.Select(t => t.ClientKey).Distinct().Count() != request.Tables.Count ||
            request.Tables.Any(t => t.Capacity < 0))
        {
            return BadRequest(new { message = "Table keys must be unique and capacities cannot be negative." });
        }

        if (request.Assignments.Select(a => a.GuestId).Distinct().Count() != request.Assignments.Count)
        {
            return BadRequest(new { message = "Each guest can only have one seating assignment." });
        }

        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var tables = await DbContext.Tables.Where(t => t.OwnerId == owner.Id).ToListAsync();
        var guests = await DbContext.Guests.Where(g => g.OwnerId == owner.Id).ToListAsync();
        var tablesById = tables.ToDictionary(t => t.TableId);
        var guestsById = guests.ToDictionary(g => g.GuestId);
        var requestedIds = request.Tables.Where(t => t.TableId.HasValue).Select(t => t.TableId!.Value).ToHashSet();

        if (requestedIds.Any(id => !tablesById.ContainsKey(id)))
        {
            return BadRequest(new { message = "One or more tables do not belong to this event." });
        }

        var requestedAssignments = request.Assignments.ToDictionary(a => a.GuestId, a => a.TableKey);
        if (requestedAssignments.Keys.Any(id => !guestsById.ContainsKey(id)))
        {
            return BadRequest(new { message = "One or more guests do not belong to this event." });
        }

        var tableByClientKey = new Dictionary<string, Table>();
        foreach (var tableRequest in request.Tables)
        {
            Table table;
            if (tableRequest.TableId.HasValue)
            {
                table = tablesById[tableRequest.TableId.Value];
            }
            else
            {
                table = new Table { OwnerId = owner.Id };
                DbContext.Tables.Add(table);
                tables.Add(table);
            }

            table.Name = tableRequest.Name ?? string.Empty;
            table.Shape = tableRequest.Shape ?? string.Empty;
            table.Tag = tableRequest.Tag;
            table.Capacity = tableRequest.Capacity;
            table.CapacityOnSides = tableRequest.CapacityOnSides;
            table.CapacityOnTopAndBottom = tableRequest.CapacityOnTopAndBottom;
            if (tableRequest.Side.HasValue)
            {
                table.Side = tableRequest.Side.Value;
            }
            table.PositionX = tableRequest.PositionX;
            table.PositionY = tableRequest.PositionY;
            table.Rotation = tableRequest.Rotation;
            tableByClientKey.Add(tableRequest.ClientKey, table);
        }

        await DbContext.SaveChangesAsync();

        var requestedTableIds = tableByClientKey.Values.Select(t => t.TableId).ToHashSet();
        var deletedTables = tables.Where(t => !requestedTableIds.Contains(t.TableId)).ToList();
        var seatsByTable = new Dictionary<int, int>();

        foreach (var guest in guests)
        {
            int? resultingTableId = guest.TableId;
            if (requestedAssignments.TryGetValue(guest.GuestId, out var tableKey))
            {
                if (tableKey == null)
                {
                    resultingTableId = null;
                }
                else if (!tableByClientKey.TryGetValue(tableKey, out var assignedTable))
                {
                    return BadRequest(new { message = $"Unknown table key '{tableKey}'." });
                }
                else
                {
                    resultingTableId = assignedTable.TableId;
                }
            }

            if (deletedTables.Any(t => t.TableId == resultingTableId))
            {
                resultingTableId = null;
            }

            guest.TableId = resultingTableId;
            if (resultingTableId.HasValue)
            {
                seatsByTable[resultingTableId.Value] = seatsByTable.GetValueOrDefault(resultingTableId.Value) + guest.NumberOfGuests;
            }
        }

        foreach (var (tableId, seatedCount) in seatsByTable)
        {
            var table = tablesById.GetValueOrDefault(tableId) ?? tableByClientKey.Values.First(t => t.TableId == tableId);
            if (seatedCount > table.Capacity)
            {
                return BadRequest(new { message = $"Table '{table.Name}' capacity exceeded." });
            }
        }

        DbContext.Tables.RemoveRange(deletedTables);
        await DbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var updatedTables = await DbContext.Tables.AsNoTracking()
            .Where(t => t.OwnerId == owner.Id)
            .Include(t => t.Guests)
            .ToListAsync();
        var updatedGuests = await DbContext.Guests.AsNoTracking()
            .Where(g => g.OwnerId == owner.Id)
            .ToListAsync();

        var categories = await DbContext.GuestCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();

        return Ok(new { tables = updatedTables, guests = updatedGuests, categories });
    }

    [HttpPost("SaveArrangement")]
    public async Task<IActionResult> SaveArrangement([FromBody] SaveArrangementRequest request)
    {
        var owner = Owner;
        using var transaction = await DbContext.Database.BeginTransactionAsync();
        var tables = await DbContext.Tables.Where(t => t.OwnerId == owner.Id).ToListAsync();
        var guests = await DbContext.Guests.Where(g => g.OwnerId == owner.Id).ToListAsync();
        var guestMap = guests.ToDictionary(g => g.GuestId);
        var assignmentMap = request.Assignments.ToDictionary(a => a.GuestId, a => a.TableId);

        foreach (var assignment in request.Assignments)
        {
            if (!guestMap.ContainsKey(assignment.GuestId))
            {
                return BadRequest(new { message = $"Guest {assignment.GuestId} not found." });
            }
        }

        var tableTotals = new Dictionary<int, int>();
        foreach (var guest in guests)
        {
            var newTableId = assignmentMap.TryGetValue(guest.GuestId, out var t) ? t : guest.TableId;
            if (newTableId.HasValue)
            {
                tableTotals.TryGetValue(newTableId.Value, out var current);
                tableTotals[newTableId.Value] = current + guest.NumberOfGuests;
            }
        }

        foreach (var (tableId, seated) in tableTotals)
        {
            var table = tables.FirstOrDefault(t => t.TableId == tableId);
            if (table == null)
            {
                return BadRequest(new { message = $"Table {tableId} not found." });
            }
            if (seated > table.Capacity)
            {
                return BadRequest(new { message = $"Table '{table.Name}' capacity exceeded." });
            }
        }

        foreach (var assignment in request.Assignments)
        {
            guestMap[assignment.GuestId].TableId = assignment.TableId;
        }

        await DbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var updatedTables = await DbContext.Tables.AsNoTracking()
            .Where(t => t.OwnerId == owner.Id)
            .Include(t => t.Guests)
            .ToListAsync();
        var updatedGuests = await DbContext.Guests.AsNoTracking()
            .Where(g => g.OwnerId == owner.Id)
            .ToListAsync();

        var categories = await DbContext.GuestCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();

        return Ok(new { tables = updatedTables, guests = updatedGuests, categories });
    }

    private static string? ToSeatingSide(EventSide side) => side switch
    {
        EventSide.Bride => "bride",
        EventSide.Groom => "groom",
        _ => null
    };

    [HttpPost("AutoAssign")]
    public async Task<IActionResult> AutoAssign([FromBody] AutoAssignRequest? request)
    {
        var owner = Owner;
        var tables = await DbContext.Tables.AsNoTracking()
            .Where(t => t.OwnerId == owner.Id)
            .OrderBy(t => t.TableId)
            .ToListAsync();
        var guests = await DbContext.Guests
            .Where(g => g.OwnerId == owner.Id)
            .ToListAsync();

        if (tables.Count == 0)
        {
            return BadRequest(new { message = "No tables found. Create tables before auto-assigning." });
        }
        if (guests.Count == 0)
        {
            return BadRequest(new { message = "No guests found. Add guests before auto-assigning." });
        }

        var lockedIds = new HashSet<int>(request?.LockedGuestIds ?? new List<int>());
        var excludedGuests = guests.Where(g => g.RsvpStatus == RsvpStatus.Declined || g.OptedOut).ToList();
        foreach (var guest in excludedGuests)
        {
            guest.TableId = null;
        }

        var seatableGuests = guests.Except(excludedGuests).ToList();
        var seatableIds = seatableGuests.Select(g => g.GuestId).ToHashSet();
        var constraints = await DbContext.SeatingConstraints
            .Where(c => c.OwnerId == owner.Id
                && seatableIds.Contains(c.GuestAId)
                && seatableIds.Contains(c.GuestBId))
            .ToListAsync();

        // A guest's side comes from their category (guests with no category are "both").
        var sideByCategory = (await DbContext.GuestCategories.AsNoTracking()
                .Where(c => c.OwnerId == owner.Id)
                .ToListAsync())
            .GroupBy(c => c.Value)
            .ToDictionary(g => g.Key, g => g.First().Side);

        var serviceRequest = new SeatingArrangeRequest
        {
            Tables = tables.Select(t => new SeatingTableDto
            {
                Id = t.TableId.ToString(),
                Name = t.Name,
                Seats = t.Capacity,
                Side = ToSeatingSide(t.Side)
            }).ToList(),
            Guests = seatableGuests.Select(g => new SeatingGuestDto
            {
                Id = g.GuestId.ToString(),
                Name = g.Name,
                Category = string.IsNullOrWhiteSpace(g.Category) ? "General" : g.Category,
                Amount = g.NumberOfGuests,
                Side = ToSeatingSide(!string.IsNullOrWhiteSpace(g.Category) && sideByCategory.TryGetValue(g.Category, out var guestSide)
                    ? guestSide
                    : EventSide.Both),
                TableId = lockedIds.Contains(g.GuestId) && g.TableId.HasValue
                    ? g.TableId.Value.ToString()
                    : null
            }).ToList(),
            Constraints = constraints.Select(c => new SeatingConstraintDto
            {
                A = c.GuestAId.ToString(),
                B = c.GuestBId.ToString(),
                Kind = c.Kind == ConstraintKind.Together ? "together" : "apart"
            }).ToList()
        };

        ArrangeResponseDto result;
        try
        {
            result = await _seatingServiceClient.Arrange(serviceRequest);
        }
        catch (SeatingServiceException ex) when ((int)ex.StatusCode == 422)
        {
            return UnprocessableEntity(new { message = "The seating service could not arrange these guests.", detail = ex.ResponseBody });
        }
        catch (Exception ex) when (ex is SeatingServiceException || ex is HttpRequestException || ex is TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The seating service is unavailable or misconfigured. Check that it is running and that Seating:ApiKey matches SEATING_API_KEY." });
        }
        var guestMap = seatableGuests.ToDictionary(g => g.GuestId);
        foreach (var guest in seatableGuests)
        {
            if (!lockedIds.Contains(guest.GuestId))
            {
                guest.TableId = null;
            }
        }

        foreach (var assignment in result.Assignments)
        {
            if (!int.TryParse(assignment.TableId, out var tableId))
            {
                continue;
            }

            foreach (var guestIdStr in assignment.GuestIds)
            {
                if (int.TryParse(guestIdStr, out var guestId) && guestMap.TryGetValue(guestId, out var guest))
                {
                    guest.TableId = tableId;
                }
            }
        }

        await DbContext.SaveChangesAsync();
        var updatedTables = await DbContext.Tables.AsNoTracking()
            .Where(t => t.OwnerId == owner.Id)
            .Include(t => t.Guests)
            .ToListAsync();
        var updatedGuests = await DbContext.Guests.AsNoTracking()
            .Where(g => g.OwnerId == owner.Id)
            .ToListAsync();

        var categories = await DbContext.GuestCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();

        return Ok(new { tables = updatedTables, guests = updatedGuests, categories, unseated = result.Unseated, score = result.Score });
    }

    [HttpGet]
    public async Task<IActionResult> GetSeating()
    {
        var owner = Owner;
        var tables = await DbContext.Tables.AsNoTracking()
            .Where(t => t.OwnerId == owner.Id)
            .Include(t => t.Guests)
            .ToListAsync();
        var guests = await DbContext.Guests.AsNoTracking()
            .Where(g => g.OwnerId == owner.Id)
            .ToListAsync();
        var categories = await DbContext.GuestCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();
        var venueElements = await DbContext.VenueElements.AsNoTracking()
            .Where(e => e.OwnerId == owner.Id)
            .ToListAsync();
        var rsvpSummary = new
        {
            confirmed = guests.Count(g => g.RsvpStatus == RsvpStatus.Confirmed),
            declined = guests.Count(g => g.RsvpStatus == RsvpStatus.Declined),
            pending = guests.Count(g => g.RsvpStatus == RsvpStatus.Pending),
            maybe = guests.Count(g => g.RsvpStatus == RsvpStatus.Maybe),
            totalPeople = guests.Sum(g => g.NumberOfGuests),
            confirmedPeople = guests.Where(g => g.RsvpStatus == RsvpStatus.Confirmed).Sum(g => g.ConfirmedCount ?? g.NumberOfGuests)
        };

        return Ok(new { tables, guests, categories, venueElements, rsvpSummary, rsvpDeadline = owner.RsvpDeadline, eventDate = owner.EventDate, wallToken = owner.WallToken });
    }

    [HttpPost("Tables")]
    public async Task<IActionResult> CreateTable([FromBody] TableRequest request)
    {
        var owner = Owner;
        var table = new Table
        {
            Name = request.Name ?? string.Empty,
            Shape = request.Shape ?? string.Empty,
            Tag = request.Tag,
            Capacity = request.Capacity,
            CapacityOnSides = request.CapacityOnSides,
            CapacityOnTopAndBottom = request.CapacityOnTopAndBottom,
            Side = request.Side ?? EventSide.Both,
            PositionX = request.PositionX,
            PositionY = request.PositionY,
            Rotation = request.Rotation,
            OwnerId = owner.Id
        };

        DbContext.Tables.Add(table);
        await DbContext.SaveChangesAsync();
        return Ok(table);
    }

    [HttpPut("Tables/{id}")]
    public async Task<IActionResult> UpdateTable(int id, [FromBody] TableRequest request)
    {
        var owner = Owner;
        var table = await DbContext.Tables.FirstOrDefaultAsync(t => t.TableId == id && t.OwnerId == owner.Id);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        table.Name = request.Name ?? string.Empty;
        table.Shape = request.Shape ?? string.Empty;
        table.Tag = request.Tag;
        table.Capacity = request.Capacity;
        table.CapacityOnSides = request.CapacityOnSides;
        table.CapacityOnTopAndBottom = request.CapacityOnTopAndBottom;
        if (request.Side.HasValue)
        {
            table.Side = request.Side.Value;
        }
        table.PositionX = request.PositionX;
        table.PositionY = request.PositionY;
        table.Rotation = request.Rotation;
        await DbContext.SaveChangesAsync();
        return Ok(table);
    }

    [HttpPatch("Tables/{id}/Position")]
    public async Task<IActionResult> UpdateTablePosition(int id, [FromBody] TablePositionRequest request)
    {
        var owner = Owner;
        var table = await DbContext.Tables.FirstOrDefaultAsync(t => t.TableId == id && t.OwnerId == owner.Id);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        table.PositionX = request.PositionX;
        table.PositionY = request.PositionY;
        table.Rotation = request.Rotation;
        await DbContext.SaveChangesAsync();
        return Ok(table);
    }

    [HttpDelete("Tables/{id}")]
    public async Task<IActionResult> DeleteTable(int id)
    {
        var owner = Owner;
        var table = await DbContext.Tables.FirstOrDefaultAsync(t => t.TableId == id && t.OwnerId == owner.Id);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        DbContext.Tables.Remove(table);
        await DbContext.SaveChangesAsync();
        return Ok(new { message = "Table deleted." });
    }
}
