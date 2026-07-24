using EventImageServer.Contexts;
using EventImageServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class VendorsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public VendorsController(AppDbContext dbContext)
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
            // authenticated request as an EventOwner (the only role that manages vendors).
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
            errorResult = StatusCode(403, new { message = "Only EventOwners manage vendors." });
            return null;
        }

        errorResult = null;
        return user;
    }

    public class VendorRequest
    {
        public string? Name { get; set; }
        public string? ContactName { get; set; }
        public VendorCategory Category { get; set; }
        public VendorStatus Status { get; set; } = VendorStatus.NotStarted;

        public string? Phone { get; set; }
        public string? WhatsApp { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Instagram { get; set; }

        public decimal AgreedPrice { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTime? NextPaymentDate { get; set; }

        public string? Notes { get; set; }
        public string? QuestionsToAsk { get; set; }
        public string? Promises { get; set; }
    }

    public class StatusRequest
    {
        public VendorStatus Status { get; set; }
    }

    public class TimelineStepRequest
    {
        public TimelineStepType Step { get; set; }
        public bool IsDone { get; set; }
    }

    // Returns the vendor list for the current EventOwner, optionally filtered
    // by category and/or status.
    [HttpGet]
    public IActionResult GetVendors([FromQuery] VendorCategory? category, [FromQuery] VendorStatus? status)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var query = _dbContext.Vendors
                .Where(v => v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .Include(v => v.Attachments)
                .AsQueryable();

            if (category.HasValue)
            {
                query = query.Where(v => v.Category == category.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(v => v.Status == status.Value);
            }

            var vendors = query.OrderBy(v => v.VendorId).ToList();

            return Ok(vendors);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving vendors", error = e.Message });
        }
    }

    // Dashboard summary: counts by status, overall progress, and vendors
    // needing a payment in the next 7 days.
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var vendors = _dbContext.Vendors
                .Where(v => v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .ToList();

            var byStatus = vendors
                .GroupBy(v => v.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var overallProgress = vendors.Count == 0
                ? 0
                : (int)Math.Round(vendors.Average(v =>
                {
                    var steps = v.Timeline?.Count ?? 0;
                    if (steps == 0) return 0;
                    return (double)(v.Timeline!.Count(s => s.IsDone)) / steps * 100;
                }));

            var weekFromNow = DateTime.UtcNow.AddDays(7);
            var needsPaymentThisWeek = vendors
                .Where(v => v.NextPaymentDate.HasValue && v.NextPaymentDate.Value <= weekFromNow && v.NextPaymentDate.Value >= DateTime.UtcNow)
                .Select(v => new { v.VendorId, v.Name, v.NextPaymentDate })
                .ToList();

            return Ok(new
            {
                total = vendors.Count,
                byStatus,
                overallProgress,
                needsPaymentThisWeek
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving vendor summary", error = e.Message });
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetVendor(int id)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var vendor = _dbContext.Vendors
                .Where(v => v.VendorId == id && v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .Include(v => v.Attachments)
                .FirstOrDefault();

            if (vendor == null)
            {
                return NotFound(new { message = "Vendor not found." });
            }

            return Ok(vendor);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving vendor", error = e.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateVendor([FromBody] VendorRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = new Vendor
        {
            Name = request.Name ?? string.Empty,
            ContactName = request.ContactName ?? string.Empty,
            Category = request.Category,
            Status = request.Status,
            Phone = request.Phone ?? string.Empty,
            WhatsApp = request.WhatsApp ?? string.Empty,
            Email = request.Email ?? string.Empty,
            Website = request.Website ?? string.Empty,
            Instagram = request.Instagram ?? string.Empty,
            AgreedPrice = request.AgreedPrice,
            DepositAmount = request.DepositAmount,
            PaidAmount = request.PaidAmount,
            NextPaymentDate = request.NextPaymentDate,
            Notes = request.Notes ?? string.Empty,
            QuestionsToAsk = request.QuestionsToAsk ?? string.Empty,
            Promises = request.Promises ?? string.Empty,
            OwnerId = owner.Id
        };

        // Seed the full ordered timeline for this vendor so the client can
        // always render every milestone (done or not) from the start.
        vendor.Timeline = Enum.GetValues<TimelineStepType>()
            .Select(step => new VendorTimelineStep { Step = step, IsDone = false })
            .ToList();

        _dbContext.Vendors.Add(vendor);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateVendor(int id, [FromBody] VendorRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors.FirstOrDefault(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        vendor.Name = request.Name ?? string.Empty;
        vendor.ContactName = request.ContactName ?? string.Empty;
        vendor.Category = request.Category;
        vendor.Status = request.Status;
        vendor.Phone = request.Phone ?? string.Empty;
        vendor.WhatsApp = request.WhatsApp ?? string.Empty;
        vendor.Email = request.Email ?? string.Empty;
        vendor.Website = request.Website ?? string.Empty;
        vendor.Instagram = request.Instagram ?? string.Empty;
        vendor.AgreedPrice = request.AgreedPrice;
        vendor.DepositAmount = request.DepositAmount;
        vendor.PaidAmount = request.PaidAmount;
        vendor.NextPaymentDate = request.NextPaymentDate;
        vendor.Notes = request.Notes ?? string.Empty;
        vendor.QuestionsToAsk = request.QuestionsToAsk ?? string.Empty;
        vendor.Promises = request.Promises ?? string.Empty;

        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteVendor(int id)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors.FirstOrDefault(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        _dbContext.Vendors.Remove(vendor);
        await _dbContext.SaveChangesAsync();

        // Best-effort cleanup of the vendor's uploaded files folder.
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", owner.Id!, "vendors", id.ToString());
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, recursive: true);
        }

        return Ok(new { message = "Vendor deleted." });
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] StatusRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors.FirstOrDefault(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        vendor.Status = request.Status;
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPatch("{id}/timeline")]
    public async Task<IActionResult> UpdateTimelineStep(int id, [FromBody] TimelineStepRequest request)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors
            .Where(v => v.VendorId == id && v.OwnerId == owner.Id)
            .Include(v => v.Timeline)
            .FirstOrDefault();

        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        var step = vendor.Timeline?.FirstOrDefault(s => s.Step == request.Step);
        if (step == null)
        {
            return NotFound(new { message = "Timeline step not found." });
        }

        step.IsDone = request.IsDone;
        step.CompletedAt = request.IsDone ? DateTime.UtcNow : null;

        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPost("{id}/attachments")]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file, [FromForm] VendorAttachmentType type)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors.FirstOrDefault(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded." });
        }

        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", owner.Id!, "vendors", id.ToString());
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(folderPath, storedFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new VendorAttachment
        {
            VendorId = id,
            Type = type,
            Url = $"/UploadedImages/{owner.Id}/vendors/{id}/{storedFileName}",
            FileName = file.FileName
        };

        _dbContext.VendorAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        return Ok(attachment);
    }

    [HttpDelete("{id}/attachments/{attachmentId}")]
    public async Task<IActionResult> DeleteAttachment(int id, int attachmentId)
    {
        var owner = RequireEventOwner(out var error);
        if (owner == null)
        {
            return error!;
        }

        var vendor = _dbContext.Vendors.FirstOrDefault(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        var attachment = _dbContext.VendorAttachments.FirstOrDefault(a => a.AttachmentId == attachmentId && a.VendorId == id);
        if (attachment == null)
        {
            return NotFound(new { message = "Attachment not found." });
        }

        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedImages", owner.Id!, "vendors", id.ToString());
        var filePath = Path.Combine(folderPath, Path.GetFileName(attachment.Url));

        // Security: ensure the resolved file path stays within the vendor's folder.
        var fullFolderPath = Path.GetFullPath(folderPath);
        var fullFilePath = Path.GetFullPath(filePath);
        if (fullFilePath.StartsWith(fullFolderPath) && System.IO.File.Exists(fullFilePath))
        {
            System.IO.File.Delete(fullFilePath);
        }

        _dbContext.VendorAttachments.Remove(attachment);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Attachment deleted." });
    }
}
