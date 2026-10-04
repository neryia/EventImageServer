using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class ImagesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EventOwnerResolver _ownerResolver;

    public ImagesController(AppDbContext dbContext, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _ownerResolver = ownerResolver;
    }

    // Resolves the caller to the event owner's id (auto-provisioning /
    // following an accepted collaborator invite exactly like Seating,
    // Budget, Vendors, etc.), so collaborators see and manage the same
    // gallery/folder as the owner instead of their own empty one.
    private async Task<(string? OwnerId, IActionResult? Error)> RequireOwnerIdAsync()
    {
        var resolution = await _ownerResolver.ResolveAsync(User, "Only the event owner or an invited collaborator can manage images.");
        if (resolution.Owner == null)
        {
            return (null, StatusCode(resolution.ErrorStatusCode ?? 401, new { message = resolution.ErrorMessage }));
        }
        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            return (null, StatusCode(403, new { message = "Viewers have read-only access." }));
        }
        return (resolution.Owner.Id, null);
    }

    private string GetMediaType(string fileName)
    {
        return MediaRules.Classify(fileName) ?? "image";
    }


    [HttpGet("Gallery")]
        public async Task<IActionResult> GetImages()
    {
            var (userId, error) = await RequireOwnerIdAsync();
            if (userId == null)
            {
                return error!;
            }

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", userId);
            if (!Directory.Exists(folderPath))
            {
                return Ok(new List<string>());
            }

            // Attribute each file to the guest who uploaded it (via RSVP),
            // so the owner's gallery can be grouped by guest instead of one
            // flat, unordered list. Files with no matching GuestMedia row are
            // the owner's own uploads.
            var guestMediaByFileName = await _dbContext.GuestMedia.AsNoTracking()
                .Where(m => m.OwnerId == userId)
                .ToDictionaryAsync(m => m.FileName, m => m);

            var guestIds = guestMediaByFileName.Values.Select(m => m.GuestId).Distinct().ToList();
            var guestNamesById = await _dbContext.Guests.AsNoTracking()
                .Where(g => guestIds.Contains(g.GuestId))
                .ToDictionaryAsync(g => g.GuestId, g => g.Name);

            var files = Directory.GetFiles(folderPath)
                                 .Select(f =>
                                 {
                                     var fileName = Path.GetFileName(f);
                                     guestMediaByFileName.TryGetValue(fileName, out var guestMedia);
                                     string? guestName = null;
                                     if (guestMedia != null)
                                     {
                                         guestNamesById.TryGetValue(guestMedia.GuestId, out guestName);
                                     }

                                     return new
                                     {
                                         url = $"/UploadedImages/{userId}/{fileName}",
                                         type = GetMediaType(fileName),
                                         guestId = guestMedia?.GuestId,
                                         guestName,
                                         uploadedAt = guestMedia?.CreatedAt
                                     };
                                 })
                                 .OrderBy(f => f.guestName == null ? 0 : 1)
                                 .ThenBy(f => f.guestName)
                                 .ThenBy(f => f.uploadedAt)
                                 .ToList();

            return Ok(files);
    }

    [HttpPost("Upload")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var (userId, error) = await RequireOwnerIdAsync();
        if (userId == null)
        {
            return error!;
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (MediaRules.Classify(file.FileName) == null)
        {
            return BadRequest(new { message = "Unsupported file type." });
        }

        using (var probe = file.OpenReadStream())
        {
            if (!MediaRules.LooksLikeMedia(file.FileName, probe))
            {
                return BadRequest(new { message = "File content does not match its type." });
            }
        }

        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", userId);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(folderPath, fileName);
        
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        _dbContext.OwnerMedia.Add(new OwnerMedia
        {
            OwnerId = userId,
            FileName = fileName,
            MediaType = GetMediaType(fileName),
            CreatedAt = DateTime.UtcNow,
            ShowOnWall = false,
        });
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            url = $"/UploadedImages/{userId}/{fileName}",
            type = GetMediaType(fileName)
        });
    }

    [HttpDelete("Delete")]
    public async Task<IActionResult> DeleteImage([FromQuery] string fileName)
    {
        return await DeleteImageInternal(fileName);
    }

    [HttpDelete("Gallery/{fileName}")]
    public async Task<IActionResult> DeleteImageFromGallery(string fileName)
    {
        return await DeleteImageInternal(fileName);
    }

    private async Task<IActionResult> DeleteImageInternal(string fileName)
    {
            var (userId, error) = await RequireOwnerIdAsync();
            if (userId == null)
            {
                return error!;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                return BadRequest(new { message = "File name is required." });
            }

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", userId);
            var filePath = Path.Combine(folderPath, fileName);

            // Security: ensure the file is within the user's folder
            var fullFolderPath = Path.GetFullPath(folderPath) + Path.DirectorySeparatorChar;
            var fullFilePath = Path.GetFullPath(filePath);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullFilePath.StartsWith(fullFolderPath, pathComparison))
            {
                return BadRequest(new { message = "Invalid file path." });
            }

            if (!System.IO.File.Exists(fullFilePath))
            {
                return NotFound(new { message = "File not found." });
            }

            System.IO.File.Delete(fullFilePath);

            var ownerMedia = await _dbContext.OwnerMedia
                .Where(m => m.OwnerId == userId && m.FileName == fileName)
                .ToListAsync();
            if (ownerMedia.Count > 0)
            {
                _dbContext.OwnerMedia.RemoveRange(ownerMedia);
                await _dbContext.SaveChangesAsync();
            }

            // Keep guest-facing RSVP media list in sync: if this file was
            // uploaded via a guest's RSVP link, remove its GuestMedia row(s)
            // and free up the guest's upload quota too, so the guest no
            // longer sees it after the owner deletes it from their gallery.
            var trackedEntries = await _dbContext.GuestMedia
                .Where(m => m.OwnerId == userId && m.FileName == fileName)
                .ToListAsync();
            if (trackedEntries.Count > 0)
            {
                var guestIds = trackedEntries.Select(m => m.GuestId).Distinct().ToList();
                var guests = await _dbContext.Guests.Where(g => guestIds.Contains(g.GuestId)).ToListAsync();
                foreach (var entry in trackedEntries)
                {
                    var guest = guests.FirstOrDefault(g => g.GuestId == entry.GuestId);
                    if (guest != null)
                    {
                        if (entry.MediaType == "image")
                        {
                            guest.GuestPhotoUploadCount = Math.Max(0, guest.GuestPhotoUploadCount - 1);
                        }
                        else
                        {
                            guest.GuestVideoUploadCount = Math.Max(0, guest.GuestVideoUploadCount - 1);
                        }
                    }
                }
                _dbContext.GuestMedia.RemoveRange(trackedEntries);
                await _dbContext.SaveChangesAsync();
            }

            return Ok(new { message = "File deleted successfully." });
    }
}
