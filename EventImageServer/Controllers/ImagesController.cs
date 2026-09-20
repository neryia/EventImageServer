using EventImageServer.Contexts;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private string? RequireOwnerId(out IActionResult? error)
    {
        var resolution = _ownerResolver.Resolve(User, "Only the event owner or an invited collaborator can manage images.");
        if (resolution.Owner == null)
        {
            error = StatusCode(resolution.ErrorStatusCode ?? 401, new { message = resolution.ErrorMessage });
            return null;
        }
        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            error = StatusCode(403, new { message = "Viewers have read-only access." });
            return null;
        }
        error = null;
        return resolution.Owner.Id;
    }

    private string GetMediaType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();

        return ext switch
        {
            ".mp4" or ".webm" or ".ogg" => "video",
            _ => "image"
        };
    }


    [HttpGet("Gallery")]
    public IActionResult GetImages()
    {
        try
        {
            var userId = RequireOwnerId(out var error);
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
            var guestMediaByFileName = _dbContext.GuestMedia
                .Where(m => m.OwnerId == userId)
                .ToDictionary(m => m.FileName, m => m);

            var guestIds = guestMediaByFileName.Values.Select(m => m.GuestId).Distinct().ToList();
            var guestNamesById = _dbContext.Guests
                .Where(g => guestIds.Contains(g.GuestId))
                .ToDictionary(g => g.GuestId, g => g.Name);

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
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving gallery", error = e.Message });
        }
    }

    [HttpPost("Upload")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var userId = RequireOwnerId(out var error);
        if (userId == null)
        {
            return error!;
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
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
        try
        {
            var userId = RequireOwnerId(out var error);
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
            var fullFolderPath = Path.GetFullPath(folderPath);
            var fullFilePath = Path.GetFullPath(filePath);

            if (!fullFilePath.StartsWith(fullFolderPath))
            {
                return BadRequest(new { message = "Invalid file path." });
            }

            if (!System.IO.File.Exists(fullFilePath))
            {
                return NotFound(new { message = "File not found." });
            }

            System.IO.File.Delete(fullFilePath);

            // Keep guest-facing RSVP media list in sync: if this file was
            // uploaded via a guest's RSVP link, remove its GuestMedia row(s)
            // and free up the guest's upload quota too, so the guest no
            // longer sees it after the owner deletes it from their gallery.
            var trackedEntries = _dbContext.GuestMedia
                .Where(m => m.OwnerId == userId && m.FileName == fileName)
                .ToList();
            if (trackedEntries.Count > 0)
            {
                var guestIds = trackedEntries.Select(m => m.GuestId).Distinct().ToList();
                var guests = _dbContext.Guests.Where(g => guestIds.Contains(g.GuestId)).ToList();
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
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error deleting file", error = e.Message });
        }
    }
}
