using System.Security.Cryptography;
using EventImageServer.Contexts;
using EventImageServer.Filters;
using EventImageServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventImageServer.Controllers.Seating;

public abstract class SeatingControllerBase : ControllerBase
{
    protected readonly AppDbContext DbContext;

    protected SeatingControllerBase(AppDbContext dbContext)
    {
        DbContext = dbContext;
    }

    protected Users Owner => (Users)HttpContext.Items[EventOwnerFilter.OwnerItemKey]!;

    protected async Task EnsureCategoryExistsAsync(string ownerId, string? categoryValue)
    {
        if (string.IsNullOrWhiteSpace(categoryValue))
        {
            return;
        }

        var exists = await DbContext.GuestCategories
            .AsNoTracking()
            .AnyAsync(c => c.OwnerId == ownerId && c.Value == categoryValue);

        if (!exists)
        {
            DbContext.GuestCategories.Add(new GuestCategory
            {
                OwnerId = ownerId,
                Value = categoryValue
            });
        }
    }

    protected async Task<IActionResult?> ValidateTableAssignmentAsync(string ownerId, int tableId, int additionalGuests, int? excludeGuestId = null)
    {
        var table = await DbContext.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.TableId == tableId && t.OwnerId == ownerId);
        if (table == null)
        {
            return NotFound(new { message = "Table not found." });
        }

        var currentSeated = await DbContext.Guests
            .AsNoTracking()
            .Where(g => g.TableId == tableId && g.GuestId != excludeGuestId)
            .SumAsync(g => (int?)g.NumberOfGuests) ?? 0;

        if (currentSeated + additionalGuests > table.Capacity)
        {
            return BadRequest(new { message = "Table capacity exceeded." });
        }

        return null;
    }

    protected async Task<bool> TableHasCapacityAsync(string ownerId, int tableId, int partySize, int? excludeGuestId = null)
    {
        var table = await DbContext.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.TableId == tableId && t.OwnerId == ownerId);
        if (table == null)
        {
            return false;
        }

        var currentSeated = await DbContext.Guests
            .AsNoTracking()
            .Where(g => g.TableId == tableId && g.GuestId != excludeGuestId)
            .SumAsync(g => (int?)g.NumberOfGuests) ?? 0;

        return currentSeated + partySize <= table.Capacity;
    }

    protected static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
