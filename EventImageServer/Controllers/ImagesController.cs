using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
[Authorize]
public class ImagesController : ControllerBase
{
    private string GetUID()
    {
       var user = User.FindFirst("user_id");
        if(user == null)
        {
            return string.Empty;
        }
        return user.Value;

    }

    [HttpGet("Gallery")]
    public IActionResult GetImages()
    {
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
                                 .Select(f => $"/UploadedImages/{userId}/" + Path.GetFileName(f))
                                 .ToList();

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

        var filePath = Path.Combine(folderPath, file.FileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileUrl = $"/UploadedImages/{userId}/{file.FileName}";
        return Ok(new { url = fileUrl });
    }
}
