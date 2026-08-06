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

    public SeatingController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private string GetUID()
    {
        var user = User.FindFirst("user_id");
        if (user == null)
        {
            return string.Empty;
        }
        return user.Value;
    }

    // Loads the current user and verifies they are an EventOwner.
    // Returns null and sets errorResult when the check fails.
    private Users? RequireEventOwner(out IActionResult? errorResult)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            errorResult = Unauthorized(new { message = "Invalid token, UID not found." });
            return null;
        }

        var user = _dbContext.Clients.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            // No registration flow exists yet, so auto-provision the user on first
            // authenticated request as an EventOwner (the only role that uses seating).
            user = new Users
            {
                Id = userId,
                Email = User.FindFirst("email")?.Value,
                FullName = User.FindFirst("name")?.Value,
                Role = RoleType.EventOwner
            };
            _dbContext.Clients.Add(user);
            _dbContext.SaveChanges();
        }

        if (user.Role != RoleType.EventOwner)
        {
            errorResult = StatusCode(403, new { message = "Only EventOwners have a seat order." });
            return null;
        }

        errorResult = null;
        return user;
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
    }

    public class GuestRequest
    {
        public string? Name { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public int NumberOfGuests { get; set; } = 1;
        public int? TableId { get; set; }
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

            var serviceRequest = new SeatingArrangeRequest
            {
                Tables = tables.Select(t => new SeatingTableDto
                {
                    Id = t.TableId.ToString(),
                    Name = t.Name,
                    Seats = t.Capacity
                }).ToList(),
                Guests = guests.Select(g => new SeatingGuestDto
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
                }).ToList()
            };

            var result = await Sitting.Arrange(serviceRequest);

            var guestMap = guests.ToDictionary(g => g.GuestId);

            // Clear non-locked assignments, then apply the arrangement returned
            // by the service. Guests the service reports as unseated are left
            // unassigned (TableId = null).
            foreach (var guest in guests)
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

            return Ok(new { tables, guests, categories });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving seating", error = e.Message });
        }
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

        await _dbContext.SaveChangesAsync();

        return Ok(table);
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
            OwnerId = owner.Id
        };

        _dbContext.Guests.Add(guest);
        EnsureCategoryExists(owner.Id!, request.Category);
        await _dbContext.SaveChangesAsync();

        return Ok(guest);
    }

    [HttpPut("Guests/{id}")]
    public async Task<IActionResult> UpdateGuest(int id, [FromBody] GuestRequest request)
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

        if (request.TableId.HasValue && request.TableId != guest.TableId)
        {
            var assignError = ValidateTableAssignment(owner.Id!, request.TableId.Value, request.NumberOfGuests);
            if (assignError != null)
            {
                return assignError;
            }
        }

        guest.Name = request.Name ?? string.Empty;
        guest.Category = request.Category;
        guest.Tag = request.Tag;
        guest.NumberOfGuests = request.NumberOfGuests;
        guest.TableId = request.TableId;

        EnsureCategoryExists(owner.Id!, request.Category);
        await _dbContext.SaveChangesAsync();

        return Ok(guest);
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

        _dbContext.Guests.Remove(guest);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Guest deleted." });
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
}
