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

    public ImagesController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private string GetUID()
    {
       var user = User.FindFirst("user_id");
       if(user == null)
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


    [HttpGet("Gallery")]
    public IActionResult GetImages()
    {
        var a = Sitting.Calc();
        a.Wait();
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        try
        {
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", userId);
            if (!Directory.Exists(folderPath))
            {
                return Ok(new List<string>());
            }
            var files = Directory.GetFiles(folderPath)
                                 .Select(f => new
                                 {
                                     url = $"/UploadedImages/{userId}/{Path.GetFileName(f)}",
                                     type = GetMediaType(Path.GetFileName(f))
                                 }).ToList();

            return Ok(files);
        }
        catch (Exception e)
        {
            return Ok(new List<string>());
        }
    }

    [HttpPost("Upload")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
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
}
