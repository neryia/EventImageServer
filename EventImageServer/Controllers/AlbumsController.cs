using EventImageServer.Contexts;
using EventImageServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class AlbumsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public AlbumsController(AppDbContext dbContext)
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

    private string GetMediaType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();

        return ext switch
        {
            ".mp4" or ".webm" or ".ogg" => "video",
            _ => "image"
        };
    }

    private string BuildUrl(string userId, string fileName) => $"/UploadedImages/{userId}/{fileName}";

    public class CreateAlbumRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Theme { get; set; }
    }

    public class UpdateAlbumRequest
    {
        public string? Name { get; set; }
        public string? Theme { get; set; }
        public string? CoverFileName { get; set; }
    }

    public class AddMediaRequest
    {
        public List<string> FileNames { get; set; } = new();
    }

    [HttpGet]
    public async Task<IActionResult> GetAlbums()
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var albums = await _dbContext.Albums
            .Where(a => a.OwnerId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var albumIds = albums.Select(a => a.AlbumId).ToList();
        var counts = await _dbContext.AlbumMedia
            .Where(m => albumIds.Contains(m.AlbumId))
            .GroupBy(m => m.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.AlbumId, g => g.Count);

        var result = albums.Select(a => new
        {
            albumId = a.AlbumId,
            name = a.Name,
            theme = a.Theme,
            coverUrl = a.CoverFileName != null ? BuildUrl(userId, a.CoverFileName) : null,
            itemCount = counts.TryGetValue(a.AlbumId, out var c) ? c : 0,
            createdAt = a.CreatedAt
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAlbum(int id)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var album = await _dbContext.Albums.FirstOrDefaultAsync(a => a.AlbumId == id && a.OwnerId == userId);
        if (album == null)
        {
            return NotFound(new { message = "Album not found." });
        }

        var items = await _dbContext.AlbumMedia
            .Where(m => m.AlbumId == id)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
            albumId = album.AlbumId,
            name = album.Name,
            theme = album.Theme,
            coverFileName = album.CoverFileName,
            coverUrl = album.CoverFileName != null ? BuildUrl(userId, album.CoverFileName) : null,
            items = items.Select(m => new
            {
                fileName = m.FileName,
                url = BuildUrl(userId, m.FileName),
                type = GetMediaType(m.FileName),
                sortOrder = m.SortOrder
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateAlbum([FromBody] CreateAlbumRequest request)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Album name is required." });
        }

        var album = new Album
        {
            OwnerId = userId,
            Name = request.Name.Trim(),
            Theme = string.IsNullOrWhiteSpace(request.Theme) ? "classic" : request.Theme,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Albums.Add(album);
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            albumId = album.AlbumId,
            name = album.Name,
            theme = album.Theme,
            coverUrl = (string?)null,
            itemCount = 0,
            createdAt = album.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAlbum(int id, [FromBody] UpdateAlbumRequest request)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var album = await _dbContext.Albums.FirstOrDefaultAsync(a => a.AlbumId == id && a.OwnerId == userId);
        if (album == null)
        {
            return NotFound(new { message = "Album not found." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            album.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Theme))
        {
            album.Theme = request.Theme;
        }

        if (request.CoverFileName != null)
        {
            var belongsToAlbum = await _dbContext.AlbumMedia
                .AnyAsync(m => m.AlbumId == id && m.FileName == request.CoverFileName);
            if (!belongsToAlbum)
            {
                return BadRequest(new { message = "Cover must be a file already in this album." });
            }
            album.CoverFileName = request.CoverFileName;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Album updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAlbum(int id)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var album = await _dbContext.Albums.FirstOrDefaultAsync(a => a.AlbumId == id && a.OwnerId == userId);
        if (album == null)
        {
            return NotFound(new { message = "Album not found." });
        }

        // Only removes the album + its membership rows. The underlying media
        // files stay on disk and remain visible in the main gallery.
        var items = _dbContext.AlbumMedia.Where(m => m.AlbumId == id);
        _dbContext.AlbumMedia.RemoveRange(items);
        _dbContext.Albums.Remove(album);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Album deleted." });
    }

    [HttpPost("{id}/media")]
    public async Task<IActionResult> AddMedia(int id, [FromBody] AddMediaRequest request)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var album = await _dbContext.Albums.FirstOrDefaultAsync(a => a.AlbumId == id && a.OwnerId == userId);
        if (album == null)
        {
            return NotFound(new { message = "Album not found." });
        }

        if (request.FileNames == null || request.FileNames.Count == 0)
        {
            return BadRequest(new { message = "No files provided." });
        }

        var existing = await _dbContext.AlbumMedia
            .Where(m => m.AlbumId == id)
            .Select(m => m.FileName)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        var nextOrder = existing.Count == 0 ? 0 : (await _dbContext.AlbumMedia
            .Where(m => m.AlbumId == id)
            .MaxAsync(m => m.SortOrder)) + 1;

        var toAdd = request.FileNames.Distinct().Where(f => !existingSet.Contains(f)).ToList();
        foreach (var fileName in toAdd)
        {
            _dbContext.AlbumMedia.Add(new AlbumMedia
            {
                AlbumId = id,
                FileName = fileName,
                SortOrder = nextOrder++,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (album.CoverFileName == null && toAdd.Count > 0)
        {
            album.CoverFileName = toAdd[0];
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Media added.", added = toAdd.Count });
    }

    [HttpDelete("{id}/media/{fileName}")]
    public async Task<IActionResult> RemoveMedia(int id, string fileName)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var album = await _dbContext.Albums.FirstOrDefaultAsync(a => a.AlbumId == id && a.OwnerId == userId);
        if (album == null)
        {
            return NotFound(new { message = "Album not found." });
        }

        var entry = await _dbContext.AlbumMedia.FirstOrDefaultAsync(m => m.AlbumId == id && m.FileName == fileName);
        if (entry == null)
        {
            return NotFound(new { message = "Media not found in album." });
        }

        _dbContext.AlbumMedia.Remove(entry);

        if (album.CoverFileName == fileName)
        {
            var replacement = await _dbContext.AlbumMedia
                .Where(m => m.AlbumId == id && m.FileName != fileName)
                .OrderBy(m => m.SortOrder)
                .FirstOrDefaultAsync();
            album.CoverFileName = replacement?.FileName;
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Media removed from album." });
    }
}
