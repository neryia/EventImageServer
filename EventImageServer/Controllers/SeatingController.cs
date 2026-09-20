using System.Security.Cryptography;
using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class SeatingController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly TwilioMessagingService _twilio;
    private readonly EventOwnerResolver _ownerResolver;

    public SeatingController(AppDbContext dbContext, TwilioMessagingService twilio, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _twilio = twilio;
        _ownerResolver = ownerResolver;
    }

    // Generates a URL-safe, cryptographically random RSVP token.
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    // Loads the current user and verifies they are an EventOwner, delegating
    // the auto-provisioning/role-check logic to the shared EventOwnerResolver
    // (also used by BudgetController and VendorsController).
    // Returns null and sets errorResult when the check fails.
    private Users? RequireEventOwner(out IActionResult? errorResult)
    {
        var resolution = _ownerResolver.Resolve(User, "Only EventOwners have a seat order.");
        if (resolution.Owner == null)
        {
            errorResult = StatusCode(resolution.ErrorStatusCode!.Value, new { message = resolution.ErrorMessage });
            return null;
        }

        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            errorResult = StatusCode(403, new { message = "Viewers have read-only access." });
            return null;
        }

        errorResult = null;
        return resolution.Owner;
    }

    // Ensures a GuestCategory row exists for the given owner/value (created with the
    // default color if missing) so newly-used category strings show up in the
    // category/color list returned by GET /Seating. No-op for blank values.
    private void EnsureCategoryExists(string ownerId, string? categoryValue)
    {
        if (string.IsNullOrWhiteSpace(categoryValue))
        {
            return;
        }

        var exists = _dbContext.GuestCategories
            .Any(c => c.OwnerId == ownerId && c.Value == categoryValue);

        if (!exists)
        {
            _dbContext.GuestCategories.Add(new GuestCategory
            {
                OwnerId = ownerId,
                Value = categoryValue
            });
        }
    }

    public class TableRequest
    {
        public string? Name { get; set; }
        public string? Shape { get; set; }
        public string Tag { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public int CapacityOnSides { get; set; }
        public int CapacityOnTopAndBottom { get; set; }
        public double? PositionX { get; set; }
        public double? PositionY { get; set; }
        public double Rotation { get; set; }
    }

    public class GuestRequest
    {
        public string? Name { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public int NumberOfGuests { get; set; } = 1;
        public int? TableId { get; set; }
        public string? Phone { get; set; }
    }

    public class RsvpDeadlineRequest
    {
        public DateTime? Deadline { get; set; }
    }

    public class EventDateRequest
    {
        public DateTime? EventDate { get; set; }
    }

    public class SendMessagesRequest
    {
        // Optional: limit the send to specific guests. If omitted, all eligible
        // guests (pending, not opted-out, has a phone number) are targeted.
        public List<int>? GuestIds { get; set; }
        public MessageChannel Channel { get; set; } = MessageChannel.Sms;
    }

    public class AutoAssignRequest
    {
        // Optional: guests in this list keep their current table (sent to the
        // service as a locked pre-assignment) instead of being freely rearranged.
        public List<int>? LockedGuestIds { get; set; }
    }

    public class GuestAssignment
    {
        public int GuestId { get; set; }
        public int? TableId { get; set; }
    }

    public class CategoryColorRequest
    {
        public string Color { get; set; } = string.Empty;
    }

    public class SaveArrangementRequest
    {
        public List<GuestAssignment> Assignments { get; set; } = new();
    }

    // Atomically applies a full set of guest -> table assignments. Every assignment
    // is validated (table exists, capacity not exceeded) BEFORE anything is written,
    // and the whole update runs in a single transaction, so a failure never leaves a
    // partially-applied arrangement — the previously saved arrangement is left intact.
    [HttpPost("SaveArrangement")]
    public async Task<IActionResult> SaveArrangement([FromBody] SaveArrangementRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var tables = _dbContext.Tables.Where(t => t.OwnerId == owner.Id).ToList();
            var guests = _dbContext.Guests.Where(g => g.OwnerId == owner.Id).ToList();
            var guestMap = guests.ToDictionary(g => g.GuestId);
            var assignmentMap = request.Assignments.ToDictionary(a => a.GuestId, a => a.TableId);

            // Validate every assignment references a real guest owned by this user.
            foreach (var assignment in request.Assignments)
            {
                if (!guestMap.ContainsKey(assignment.GuestId))
                {
                    return BadRequest(new { message = $"Guest {assignment.GuestId} not found." });
                }
            }

            // Compute the resulting seat count per table (unmentioned guests keep
            // their current table) and verify no table's capacity is exceeded.
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

            // All valid — apply and commit atomically.
            foreach (var assignment in request.Assignments)
            {
                guestMap[assignment.GuestId].TableId = assignment.TableId;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            var updatedTables = _dbContext.Tables
                .Where(t => t.OwnerId == owner.Id)
                .Include(t => t.Guests)
                .ToList();
            var updatedGuests = _dbContext.Guests
                .Where(g => g.OwnerId == owner.Id)
                .ToList();

            return Ok(new { tables = updatedTables, guests = updatedGuests });
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Error saving arrangement", error = e.Message });
        }
    }

    // Sends the owner's current guest list to the seating service, applies the
    // returned arrangement (guest -> table) and returns it to the client.
    [HttpPost("AutoAssign")]
    public async Task<IActionResult> AutoAssign([FromBody] AutoAssignRequest? request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        try
        {
            var tables = _dbContext.Tables
                .Where(t => t.OwnerId == owner.Id)
                .OrderBy(t => t.TableId)
                .ToList();

            var guests = _dbContext.Guests
                .Where(g => g.OwnerId == owner.Id)
                .ToList();

            if (tables.Count == 0)
            {
                return BadRequest(new { message = "No tables found. Create tables before auto-assigning." });
            }

            if (guests.Count == 0)
            {
                return BadRequest(new { message = "No guests found. Add guests before auto-assigning." });
            }

            var lockedIds = new HashSet<int>(request?.LockedGuestIds ?? new List<int>());

            // Declined and opted-out guests are never seated: unassign them and
            // exclude them from the arrangement sent to the seating service.
            // Confirmed and Pending guests are still eligible to be seated.
            var excludedGuests = guests.Where(g => g.RsvpStatus == RsvpStatus.Declined || g.OptedOut).ToList();
            foreach (var guest in excludedGuests)
            {
                guest.TableId = null;
            }

            var seatableGuests = guests.Except(excludedGuests).ToList();
            var seatableIds = seatableGuests.Select(g => g.GuestId).ToHashSet();

            var constraints = await _dbContext.SeatingConstraints
                .Where(c => c.OwnerId == owner.Id
                    && seatableIds.Contains(c.GuestAId)
                    && seatableIds.Contains(c.GuestBId))
                .ToListAsync();

            var serviceRequest = new SeatingArrangeRequest
            {
                Tables = tables.Select(t => new SeatingTableDto
                {
                    Id = t.TableId.ToString(),
                    Name = t.Name,
                    Seats = t.Capacity
                }).ToList(),
                Guests = seatableGuests.Select(g => new SeatingGuestDto
                {
                    Id = g.GuestId.ToString(),
                    Name = g.Name,
                    // The seating service requires a non-blank category; default
                    // guests that don't have one set (e.g. legacy records).
                    Category = string.IsNullOrWhiteSpace(g.Category) ? "General" : g.Category,
                    Amount = g.NumberOfGuests,
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

            var result = await Sitting.Arrange(serviceRequest);

            var guestMap = seatableGuests.ToDictionary(g => g.GuestId);

            // Clear non-locked assignments, then apply the arrangement returned
            // by the service. Guests the service reports as unseated are left
            // unassigned (TableId = null).
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

            await _dbContext.SaveChangesAsync();

            var updatedTables = _dbContext.Tables
                .Where(t => t.OwnerId == owner.Id)
                .Include(t => t.Guests)
                .ToList();
            var updatedGuests = _dbContext.Guests
                .Where(g => g.OwnerId == owner.Id)
                .ToList();

            return Ok(new
            {
                tables = updatedTables,
                guests = updatedGuests,
                unseated = result.Unseated,
                score = result.Score
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error auto-assigning seating", error = e.Message });
        }
    }

    // Returns the whole seat order for the current EventOwner:
    // guest list, table list and each guest's assigned table.
    [HttpGet]
    public IActionResult GetSeating()
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var tables = _dbContext.Tables
                .Where(t => t.OwnerId == owner.Id)
                .Include(t => t.Guests)
                .ToList();

            var guests = _dbContext.Guests
                .Where(g => g.OwnerId == owner.Id)
                .ToList();

            var categories = _dbContext.GuestCategories
                .Where(c => c.OwnerId == owner.Id)
                .ToList();

            var venueElements = _dbContext.VenueElements
                .Where(e => e.OwnerId == owner.Id)
                .ToList();

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
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving seating", error = e.Message });
        }
    }

    // Sets (or clears) the RSVP deadline for the current owner's event.
    [HttpPut("RsvpDeadline")]
    public async Task<IActionResult> SetRsvpDeadline([FromBody] RsvpDeadlineRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        owner.RsvpDeadline = request.Deadline;
        await _dbContext.SaveChangesAsync();

        return Ok(new { rsvpDeadline = owner.RsvpDeadline });
    }

    // Sets (or clears) the wedding day itself for the current owner's event.
    // This gates the guest photo/video upload feature on the RSVP page (only
    // open on the event date + a one-day grace period).
    [HttpPut("EventDate")]
    public async Task<IActionResult> SetEventDate([FromBody] EventDateRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        owner.EventDate = request.EventDate;
        await _dbContext.SaveChangesAsync();

        return Ok(new { eventDate = owner.EventDate });
    }

    // Generates (or rotates) the secret token that gates the public live
    // photo wall at /wall/{token}. Anyone with the token can view the wall
    // while the upload window is open, so it's random and revocable.
    [HttpPost("WallToken")]
    public async Task<IActionResult> GenerateWallToken()
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        owner.WallToken = GenerateSecureToken();
        await _dbContext.SaveChangesAsync();

        return Ok(new { wallToken = owner.WallToken });
    }

    // Revokes the wall token, immediately disabling public access to the wall.
    [HttpDelete("WallToken")]
    public async Task<IActionResult> RevokeWallToken()
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        owner.WallToken = null;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Wall token revoked." });
    }

    public class ReminderSettingsRequest
    {
        public bool AutoRemindersEnabled { get; set; }
        public List<int> ReminderOffsets { get; set; } = new();
    }

    // GET /Seating/ReminderSettings
    [HttpGet("ReminderSettings")]
    public IActionResult GetReminderSettings()
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        return Ok(new { autoRemindersEnabled = owner.AutoRemindersEnabled, reminderOffsets = owner.ReminderOffsets });
    }

    // PUT /Seating/ReminderSettings { AutoRemindersEnabled, ReminderOffsets }
    // ReminderOffsets are "days before RsvpDeadline" (e.g. [30,14,7,2]); the
    // ReminderScheduler background service reads these hourly.
    [HttpPut("ReminderSettings")]
    public async Task<IActionResult> UpdateReminderSettings([FromBody] ReminderSettingsRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        owner.AutoRemindersEnabled = request.AutoRemindersEnabled;
        owner.ReminderOffsets = (request.ReminderOffsets ?? new List<int>())
            .Where(d => d > 0)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        await _dbContext.SaveChangesAsync();

        return Ok(new { autoRemindersEnabled = owner.AutoRemindersEnabled, reminderOffsets = owner.ReminderOffsets });
    }

    [HttpPost("Tables")]
    public async Task<IActionResult> CreateTable([FromBody] TableRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var table = new Table
        {
            Name = request.Name ?? string.Empty,
            Shape = request.Shape ?? string.Empty,
            Tag = request.Tag,
            Capacity = request.Capacity,
            CapacityOnSides = request.CapacityOnSides,
            CapacityOnTopAndBottom = request.CapacityOnTopAndBottom,
            PositionX = request.PositionX,
            PositionY = request.PositionY,
            Rotation = request.Rotation,
            OwnerId = owner.Id
        };

        _dbContext.Tables.Add(table);
        await _dbContext.SaveChangesAsync();

        return Ok(table);
    }

    [HttpPut("Tables/{id}")]
    public async Task<IActionResult> UpdateTable(int id, [FromBody] TableRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var table = _dbContext.Tables.FirstOrDefault(t => t.TableId == id && t.OwnerId == owner.Id);
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
        table.PositionX = request.PositionX;
        table.PositionY = request.PositionY;
        table.Rotation = request.Rotation;

        await _dbContext.SaveChangesAsync();

        return Ok(table);
    }

    public class TablePositionRequest
    {
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double Rotation { get; set; }
    }

    // Lightweight endpoint for floor-plan drags: updates only position/rotation
    // without requiring the full name/shape/capacity payload.
    [HttpPatch("Tables/{id}/Position")]
    public async Task<IActionResult> UpdateTablePosition(int id, [FromBody] TablePositionRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var table = _dbContext.Tables.FirstOrDefault(t => t.TableId == id && t.OwnerId == owner.Id);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        table.PositionX = request.PositionX;
        table.PositionY = request.PositionY;
        table.Rotation = request.Rotation;

        await _dbContext.SaveChangesAsync();

        return Ok(table);
    }

    public class VenueElementRequest
    {
        public VenueElementKind Kind { get; set; }
        public string? Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 80;
        public double Height { get; set; } = 80;
        public double Rotation { get; set; }
    }

    [HttpGet("VenueElements")]
    public IActionResult GetVenueElements()
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var elements = _dbContext.VenueElements.Where(e => e.OwnerId == owner.Id).ToList();
        return Ok(elements);
    }

    [HttpPost("VenueElements")]
    public async Task<IActionResult> CreateVenueElement([FromBody] VenueElementRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
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
            Rotation = request.Rotation,
        };

        _dbContext.VenueElements.Add(element);
        await _dbContext.SaveChangesAsync();

        return Ok(element);
    }

    [HttpPut("VenueElements/{id}")]
    public async Task<IActionResult> UpdateVenueElement(int id, [FromBody] VenueElementRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var element = _dbContext.VenueElements.FirstOrDefault(e => e.ElementId == id && e.OwnerId == owner.Id);
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
        element.Rotation = request.Rotation;

        await _dbContext.SaveChangesAsync();

        return Ok(element);
    }

    [HttpDelete("VenueElements/{id}")]
    public async Task<IActionResult> DeleteVenueElement(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var element = _dbContext.VenueElements.FirstOrDefault(e => e.ElementId == id && e.OwnerId == owner.Id);
        if (element == null)
        {
            return NotFound(new { message = "Venue element not found." });
        }

        _dbContext.VenueElements.Remove(element);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Venue element deleted." });
    }

    [HttpDelete("Tables/{id}")]
    public async Task<IActionResult> DeleteTable(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var table = _dbContext.Tables.FirstOrDefault(t => t.TableId == id && t.OwnerId == owner.Id);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        _dbContext.Tables.Remove(table);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Table deleted." });
    }

    [HttpPost("Guests")]
    public async Task<IActionResult> CreateGuest([FromBody] GuestRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            if (request.TableId.HasValue)
            {
                var assignError = ValidateTableAssignment(owner.Id!, request.TableId.Value, request.NumberOfGuests);
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

            _dbContext.Guests.Add(guest);
            EnsureCategoryExists(owner.Id!, request.Category);
            await _dbContext.SaveChangesAsync();

            return Ok(guest);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Failed to create guest", error = e.Message });
        }
    }

    [HttpPut("Guests/{id}")]
    public async Task<IActionResult> UpdateGuest(int id, [FromBody] GuestRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
            if (guest == null)
            {
                return NotFound(new { message = "Guest not found." });
            }

            guest.Name = request.Name ?? string.Empty;
            guest.Category = request.Category;
            guest.Tag = request.Tag;
            guest.NumberOfGuests = request.NumberOfGuests;
            guest.Phone = request.Phone;

            // If the guest is being (re)seated but their party size no longer fits
            // at that table — e.g. the owner just bumped up their headcount while
            // they were already seated — automatically unseat them instead of
            // blocking the party-size update or silently overbooking the table.
            guest.TableId = request.TableId.HasValue &&
                TableHasCapacity(owner.Id!, request.TableId.Value, request.NumberOfGuests, excludeGuestId: guest.GuestId)
                ? request.TableId
                : null;

            EnsureCategoryExists(owner.Id!, request.Category);
            await _dbContext.SaveChangesAsync();

            return Ok(guest);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Failed to update guest", error = e.Message });
        }
    }

    [HttpDelete("Guests/{id}")]
    public async Task<IActionResult> DeleteGuest(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        // SeatingConstraints has no FK to Guests (it references two guest ids
        // from the same table, which EF can't model as two navigation
        // properties cleanly), so clean up any constraints referencing this
        // guest by hand instead of relying on cascade delete.
        var relatedConstraints = _dbContext.SeatingConstraints
            .Where(c => c.OwnerId == owner.Id && (c.GuestAId == id || c.GuestBId == id));
        _dbContext.SeatingConstraints.RemoveRange(relatedConstraints);

        _dbContext.Guests.Remove(guest);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Guest deleted." });
    }

    public class SeatingConstraintRequest
    {
        public int GuestAId { get; set; }
        public int GuestBId { get; set; }
        public ConstraintKind Kind { get; set; }
    }

    // GET /Seating/Constraints
    [HttpGet("Constraints")]
    public async Task<IActionResult> GetConstraints()
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var constraints = await _dbContext.SeatingConstraints
            .Where(c => c.OwnerId == owner.Id)
            .ToListAsync();

        return Ok(constraints);
    }

    // POST /Seating/Constraints
    [HttpPost("Constraints")]
    public async Task<IActionResult> CreateConstraint([FromBody] SeatingConstraintRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        if (request.GuestAId == request.GuestBId)
        {
            return BadRequest(new { message = "A guest cannot be constrained with themselves." });
        }

        var guestIds = new[] { request.GuestAId, request.GuestBId };
        var validGuestCount = await _dbContext.Guests
            .CountAsync(g => g.OwnerId == owner.Id && guestIds.Contains(g.GuestId));
        if (validGuestCount != 2)
        {
            return BadRequest(new { message = "Both guests must exist and belong to you." });
        }

        // Store the pair in a canonical (smaller id first) order so the
        // unique index catches the same pair submitted in either order.
        var (a, b) = request.GuestAId < request.GuestBId
            ? (request.GuestAId, request.GuestBId)
            : (request.GuestBId, request.GuestAId);

        var exists = await _dbContext.SeatingConstraints.AnyAsync(c =>
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

        _dbContext.SeatingConstraints.Add(constraint);
        await _dbContext.SaveChangesAsync();

        return Ok(constraint);
    }

    // DELETE /Seating/Constraints/{id}
    [HttpDelete("Constraints/{id}")]
    public async Task<IActionResult> DeleteConstraint(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var constraint = await _dbContext.SeatingConstraints
            .FirstOrDefaultAsync(c => c.ConstraintId == id && c.OwnerId == owner.Id);
        if (constraint == null)
        {
            return NotFound(new { message = "Constraint not found." });
        }

        _dbContext.SeatingConstraints.Remove(constraint);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Constraint deleted." });
    }

    // Generates a secure RSVP token for the guest on first call. Subsequent calls
    // return the same existing token/link instead of regenerating a new one, so
    // copying the link multiple times always yields the same URL. Use RegenerateLink
    // if a fresh token (invalidating the old link) is explicitly needed.
    [HttpPost("Guests/{id}/GenerateLink")]
    public async Task<IActionResult> GenerateLink(int id)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
            if (guest == null)
            {
                return NotFound(new { message = "Guest not found." });
            }

            if (string.IsNullOrEmpty(guest.RsvpToken))
            {
                guest.RsvpToken = GenerateSecureToken();
                guest.RsvpTokenCreatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : _twilio.PublicBaseUrl.TrimEnd('/');

            return Ok(new { link = $"{baseUrl}/Rsvp/{guest.RsvpToken}", token = guest.RsvpToken });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Failed to generate RSVP link", error = e.Message });
        }
    }

    public class CheckInRequest
    {
        // Optional: defaults to the guest's confirmed (or invited) party size
        // if omitted, so a simple "one tap = arrived" flow doesn't require the
        // owner to also enter a headcount.
        public int? CheckedInCount { get; set; }
    }

    // Marks a guest party as physically arrived at the event. Independent of
    // RsvpStatus, which only tracks whether they responded, not whether they
    // showed up.
    [HttpPut("Guests/{id}/CheckIn")]
    public async Task<IActionResult> CheckInGuest(int id, [FromBody] CheckInRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        guest.CheckedInAt = DateTime.UtcNow;
        guest.CheckedInCount = request.CheckedInCount ?? guest.ConfirmedCount ?? guest.NumberOfGuests;

        await _dbContext.SaveChangesAsync();

        return Ok(guest);
    }

    // Undoes a check-in (accidental tap, guest left before the event, etc.).
    [HttpDelete("Guests/{id}/CheckIn")]
    public async Task<IActionResult> UndoCheckInGuest(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        guest.CheckedInAt = null;
        guest.CheckedInCount = null;

        await _dbContext.SaveChangesAsync();

        return Ok(guest);
    }

    public class RsvpStatusUpdateRequest
    {
        public RsvpStatus Status { get; set; }
        public int NumberOfGuests { get; set; }
    }

    // Lets the owner manually set a guest's RSVP status (e.g. they heard back by
    // phone instead of through the Smart RSVP link). NumberOfGuests is required
    // on every call so the party size always stays in sync with the new status
    // — this matters most when manually declining, where a stale non-zero party
    // size would keep counting the guest toward capacity/head-count even though
    // they aren't coming.
    [HttpPut("Guests/{id}/RsvpStatus")]
    public async Task<IActionResult> UpdateRsvpStatus(int id, [FromBody] RsvpStatusUpdateRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
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
                // A declined guest isn't coming, so they shouldn't occupy a seat.
                guest.TableId = null;
                guest.NumberOfGuests = request.NumberOfGuests;
                guest.ConfirmedCount = request.NumberOfGuests;
            }
            else
            {
                // If the guest is currently seated but their new party size no
                // longer fits at that table, automatically unseat them instead of
                // blocking the status/party-size update.
                if (guest.TableId.HasValue &&
                    !TableHasCapacity(owner.Id!, guest.TableId.Value, request.NumberOfGuests, excludeGuestId: guest.GuestId))
                {
                    guest.TableId = null;
                }

                guest.NumberOfGuests = request.NumberOfGuests;
                guest.ConfirmedCount = request.Status == RsvpStatus.Confirmed
                    ? request.NumberOfGuests
                    : guest.ConfirmedCount;
            }

            await _dbContext.SaveChangesAsync();

            return Ok(guest);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Failed to update RSVP status", error = e.Message });
        }
    }

    // Sends an RSVP invite (SMS/WhatsApp) to eligible guests: not opted-out, has a
    // phone number. Generates a token first if the guest doesn't already have one.
    [HttpPost("Invites/Send")]
    public async Task<IActionResult> SendInvites([FromBody] SendMessagesRequest request)
    {
        return await SendGuestMessages(request, MessageType.Invite);
    }

    // Sends an RSVP reminder to guests who are still Pending, not opted-out, have a
    // phone number, and (if a deadline is set) the deadline hasn't passed yet.
    [HttpPost("Reminders/Send")]
    public async Task<IActionResult> SendReminders([FromBody] SendMessagesRequest request)
    {
        return await SendGuestMessages(request, MessageType.Reminder);
    }

    private async Task<IActionResult> SendGuestMessages(SendMessagesRequest request, MessageType type)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        try
        {
            var guestsQuery = _dbContext.Guests.Where(g => g.OwnerId == owner.Id);
            if (request.GuestIds != null && request.GuestIds.Count > 0)
            {
                var ids = request.GuestIds.ToHashSet();
                guestsQuery = guestsQuery.Where(g => ids.Contains(g.GuestId));
            }

            var guests = guestsQuery.ToList()
                .Where(g => !g.OptedOut && !string.IsNullOrWhiteSpace(g.Phone))
                .ToList();

            // Reminders only make sense for guests who haven't responded yet, and
            // must not be sent once the RSVP deadline has passed.
            if (type == MessageType.Reminder)
            {
                guests = guests.Where(g => g.RsvpStatus == RsvpStatus.Pending).ToList();
                if (owner.RsvpDeadline.HasValue && DateTime.UtcNow > owner.RsvpDeadline.Value)
                {
                    return BadRequest(new { message = "RSVP deadline has already passed." });
                }
            }

            var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : _twilio.PublicBaseUrl.TrimEnd('/');

            var results = new List<object>();

            foreach (var guest in guests)
            {
                if (string.IsNullOrEmpty(guest.RsvpToken))
                {
                    guest.RsvpToken = GenerateSecureToken();
                    guest.RsvpTokenCreatedAt = DateTime.UtcNow;
                }

                var link = $"{baseUrl}/Rsvp/{guest.RsvpToken}";
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

                try
                {
                    var sendResult = await _twilio.SendAsync(request.Channel, guest.Phone!, body);
                    log.TwilioSid = sendResult.Sid;
                    log.Status = sendResult.Status;
                }
                catch (Exception ex)
                {
                    log.Status = "failed";
                    log.ErrorCode = ex.Message;
                }

                _dbContext.MessageLogs.Add(log);
                results.Add(new { guestId = guest.GuestId, status = log.Status });
            }

            await _dbContext.SaveChangesAsync();

            return Ok(new { sent = results.Count, results });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error sending messages", error = e.Message });
        }
    }

    // Sets (or creates) the display color for a category value. This is a bulk
    // operation: the color lives on the GuestCategory row, not on individual guests,
    // so every guest sharing this category value picks up the new color immediately.
    [HttpPut("Category/{categoryValue}/Color")]
    public async Task<IActionResult> UpdateCategoryColor(string categoryValue, [FromBody] CategoryColorRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(categoryValue))
        {
            return BadRequest(new { message = "Category value is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Color))
        {
            return BadRequest(new { message = "Color is required." });
        }

        var category = _dbContext.GuestCategories
            .FirstOrDefault(c => c.OwnerId == owner.Id && c.Value == categoryValue);

        if (category == null)
        {
            category = new GuestCategory
            {
                OwnerId = owner.Id,
                Value = categoryValue,
                Color = request.Color
            };
            _dbContext.GuestCategories.Add(category);
        }
        else
        {
            category.Color = request.Color;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(category);
    }

    // Assigns (or reassigns) a guest to a table, checking capacity.
    [HttpPut("Guests/{id}/Table/{tableId}")]
    public async Task<IActionResult> AssignGuestToTable(int id, int tableId)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var guest = _dbContext.Guests.FirstOrDefault(g => g.GuestId == id && g.OwnerId == owner.Id);
        if (guest == null)
        {
            return NotFound(new { message = "Guest not found." });
        }

        var assignError = ValidateTableAssignment(owner.Id!, tableId, guest.NumberOfGuests, excludeGuestId: guest.GuestId);
        if (assignError != null)
        {
            return assignError;
        }

        guest.TableId = tableId;
        await _dbContext.SaveChangesAsync();

        return Ok(guest);
    }

    // Verifies the table belongs to the owner and has enough remaining capacity.
    private IActionResult? ValidateTableAssignment(string ownerId, int tableId, int additionalGuests, int? excludeGuestId = null)
    {
        var table = _dbContext.Tables.FirstOrDefault(t => t.TableId == tableId && t.OwnerId == ownerId);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        var currentSeated = _dbContext.Guests
            .Where(g => g.TableId == tableId && g.GuestId != excludeGuestId)
            .Sum(g => (int?)g.NumberOfGuests) ?? 0;

        if (currentSeated + additionalGuests > table.Capacity)
        {
            return BadRequest(new { message = "Table capacity exceeded." });
        }

        return null;
    }

    // Non-error variant of the same capacity check, used where an owner is
    // updating a guest's party size/status rather than explicitly assigning a
    // seat — callers use this to silently unseat the guest instead of blocking
    // the update when the new party size no longer fits.
    private bool TableHasCapacity(string ownerId, int tableId, int partySize, int? excludeGuestId = null)
    {
        var table = _dbContext.Tables.FirstOrDefault(t => t.TableId == tableId && t.OwnerId == ownerId);
        if (table == null)
        {
            return false;
        }

        var currentSeated = _dbContext.Guests
            .Where(g => g.TableId == tableId && g.GuestId != excludeGuestId)
            .Sum(g => (int?)g.NumberOfGuests) ?? 0;

        return currentSeated + partySize <= table.Capacity;
    }
}
