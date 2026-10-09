using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class VendorsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EventOwnerResolver _ownerResolver;

    public VendorsController(AppDbContext dbContext, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _ownerResolver = ownerResolver;
    }

    // Loads the current user and verifies they are an EventOwner, delegating
    // the auto-provisioning/role-check logic to the shared EventOwnerResolver
    // (also used by SeatingController and BudgetController).
    // Returns null and sets errorResult when the check fails.
    private async Task<(Users? Owner, IActionResult? Error)> RequireEventOwnerAsync()
    {
        var resolution = await _ownerResolver.ResolveAsync(User, "Only EventOwners manage vendors.");
        if (resolution.Owner == null)
        {
            return (null, StatusCode(resolution.ErrorStatusCode!.Value, new { message = resolution.ErrorMessage }));
        }

        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            return (null, StatusCode(403, new { message = "Viewers have read-only access." }));
        }

        return (resolution.Owner, null);
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

    public class PaymentRequest
    {
        public string? Label { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class PaymentPaidRequest
    {
        public bool IsPaid { get; set; }
    }

    private const decimal MaxPaymentAmount = 1_000_000_000m;
    private const int MaxPaymentLabelLength = 100;

    private IActionResult? ValidatePayment(PaymentRequest request)
    {
        if (request.Amount <= 0 || request.Amount > MaxPaymentAmount)
        {
            return BadRequest(new { message = "Payment amount must be greater than zero." });
        }

        if (request.DueDate == null)
        {
            return BadRequest(new { message = "Payment due date is required." });
        }

        if ((request.Label ?? string.Empty).Trim().Length > MaxPaymentLabelLength)
        {
            return BadRequest(new { message = $"Payment label must be at most {MaxPaymentLabelLength} characters." });
        }

        return null;
    }

    private Task<Vendor?> LoadFullVendorAsync(int id, string ownerId) =>
        _dbContext.Vendors
            .Where(v => v.VendorId == id && v.OwnerId == ownerId)
            .Include(v => v.Timeline)
            .Include(v => v.Attachments)
            .Include(v => v.Payments)
            .FirstOrDefaultAsync();

    public class TimelineStepRequest
    {
        public TimelineStepType Step { get; set; }
        public bool IsDone { get; set; }
    }

    // Returns the vendor list for the current EventOwner, optionally filtered
    // by category and/or status.
    [HttpGet]
    public async Task<IActionResult> GetVendors([FromQuery] VendorCategory? category, [FromQuery] VendorStatus? status)
    {
            var (owner, error) = await RequireEventOwnerAsync();
            if (owner == null)
            {
                return error!;
            }

            var query = _dbContext.Vendors.AsNoTracking()
                .Where(v => v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .Include(v => v.Attachments)
                .Include(v => v.Payments)
                .AsQueryable();

            if (category.HasValue)
            {
                query = query.Where(v => v.Category == category.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(v => v.Status == status.Value);
            }

            var vendors = await query.OrderBy(v => v.VendorId).ToListAsync();

            return Ok(vendors);
    }

    // Dashboard summary: counts by status, overall progress, and unpaid
    // installments that are overdue or due in the next 7 days (date-only,
    // server local "today"; cancelled vendors are skipped).
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
            var (owner, error) = await RequireEventOwnerAsync();
            if (owner == null)
            {
                return error!;
            }

            var vendors = await _dbContext.Vendors.AsNoTracking()
                .Where(v => v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .Include(v => v.Payments)
                .ToListAsync();

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

            var today = DateTime.Today;
            var weekEnd = today.AddDays(7);
            var unpaid = vendors
                .Where(v => v.Status != VendorStatus.Cancelled)
                .SelectMany(v => (v.Payments ?? new List<VendorPayment>())
                    .Where(p => !p.IsPaid)
                    .Select(p => new
                    {
                        v.VendorId,
                        v.Name,
                        p.PaymentId,
                        p.Label,
                        p.Amount,
                        DueDate = p.DueDate.Date
                    }))
                .OrderBy(p => p.DueDate)
                .ToList();

            var overduePayments = unpaid.Where(p => p.DueDate < today).ToList();
            var paymentsDueThisWeek = unpaid.Where(p => p.DueDate >= today && p.DueDate < weekEnd).ToList();

            return Ok(new
            {
                total = vendors.Count,
                byStatus,
                overallProgress,
                overduePayments,
                paymentsDueThisWeek
            });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetVendor(int id)
    {
            var (owner, error) = await RequireEventOwnerAsync();
            if (owner == null)
            {
                return error!;
            }

            var vendor = await _dbContext.Vendors.AsNoTracking()
                .Where(v => v.VendorId == id && v.OwnerId == owner.Id)
                .Include(v => v.Timeline)
                .Include(v => v.Attachments)
                .Include(v => v.Payments)
                .FirstOrDefaultAsync();

            if (vendor == null)
            {
                return NotFound(new { message = "Vendor not found." });
            }

            return Ok(vendor);
    }

    [HttpPost]
    public async Task<IActionResult> CreateVendor([FromBody] VendorRequest request)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = new Vendor
        {
            Name = request.Name ?? string.Empty,
            ContactName = request.ContactName ?? string.Empty,
            Category = request.Category,
            Status = VendorStatus.NotStarted,
            Phone = request.Phone ?? string.Empty,
            WhatsApp = request.WhatsApp ?? string.Empty,
            Email = request.Email ?? string.Empty,
            Website = request.Website ?? string.Empty,
            Instagram = request.Instagram ?? string.Empty,
            AgreedPrice = request.AgreedPrice,
            DepositAmount = request.DepositAmount,
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
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        vendor.Name = request.Name ?? string.Empty;
        vendor.ContactName = request.ContactName ?? string.Empty;
        vendor.Category = request.Category;
        vendor.Phone = request.Phone ?? string.Empty;
        vendor.WhatsApp = request.WhatsApp ?? string.Empty;
        vendor.Email = request.Email ?? string.Empty;
        vendor.Website = request.Website ?? string.Empty;
        vendor.Instagram = request.Instagram ?? string.Empty;
        vendor.AgreedPrice = request.AgreedPrice;
        vendor.DepositAmount = request.DepositAmount;
        vendor.Notes = request.Notes ?? string.Empty;
        vendor.QuestionsToAsk = request.QuestionsToAsk ?? string.Empty;
        vendor.Promises = request.Promises ?? string.Empty;

        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteVendor(int id)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.VendorId == id && v.OwnerId == owner.Id);
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
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        // Status is derived from the timeline; the only manual action is
        // Cancel (Cancelled) / Reactivate (anything else -> derived).
        vendor.Status = request.Status == VendorStatus.Cancelled
            ? VendorStatus.Cancelled
            : VendorRules.DeriveStatus(vendor.Timeline);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] PaymentRequest request)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var invalid = ValidatePayment(request);
        if (invalid != null)
        {
            return invalid;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        vendor.Payments ??= new List<VendorPayment>();
        vendor.Payments.Add(new VendorPayment
        {
            Label = (request.Label ?? string.Empty).Trim(),
            Amount = request.Amount,
            DueDate = request.DueDate!.Value.Date
        });
        VendorRules.RecalculatePayments(vendor);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPut("{id}/payments/{paymentId}")]
    public async Task<IActionResult> UpdatePayment(int id, int paymentId, [FromBody] PaymentRequest request)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var invalid = ValidatePayment(request);
        if (invalid != null)
        {
            return invalid;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        var payment = vendor?.Payments?.FirstOrDefault(p => p.PaymentId == paymentId);
        if (vendor == null || payment == null)
        {
            return NotFound(new { message = "Payment not found." });
        }

        payment.Label = (request.Label ?? string.Empty).Trim();
        payment.Amount = request.Amount;
        payment.DueDate = request.DueDate!.Value.Date;
        VendorRules.RecalculatePayments(vendor);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPatch("{id}/payments/{paymentId}/paid")]
    public async Task<IActionResult> SetPaymentPaid(int id, int paymentId, [FromBody] PaymentPaidRequest request)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        var payment = vendor?.Payments?.FirstOrDefault(p => p.PaymentId == paymentId);
        if (vendor == null || payment == null)
        {
            return NotFound(new { message = "Payment not found." });
        }

        payment.IsPaid = request.IsPaid;
        payment.PaidAt = request.IsPaid ? DateTime.UtcNow : null;
        VendorRules.RecalculatePayments(vendor);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpDelete("{id}/payments/{paymentId}")]
    public async Task<IActionResult> DeletePayment(int id, int paymentId)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);
        var payment = vendor?.Payments?.FirstOrDefault(p => p.PaymentId == paymentId);
        if (vendor == null || payment == null)
        {
            return NotFound(new { message = "Payment not found." });
        }

        vendor.Payments!.Remove(payment);
        _dbContext.VendorPayments.Remove(payment);
        VendorRules.RecalculatePayments(vendor);
        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPatch("{id}/timeline")]
    public async Task<IActionResult> UpdateTimelineStep(int id, [FromBody] TimelineStepRequest request)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await LoadFullVendorAsync(id, owner.Id!);

        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        if (vendor.Timeline?.Any(s => s.Step == request.Step) != true)
        {
            return NotFound(new { message = "Timeline step not found." });
        }

        // Contiguous-prefix semantics + status derivation live server-side.
        VendorRules.ApplyTimelineChange(vendor, request.Step, request.IsDone);

        await _dbContext.SaveChangesAsync();

        return Ok(vendor);
    }

    [HttpPost("{id}/attachments")]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file, [FromForm] VendorAttachmentType type)
    {
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file uploaded." });
        }

        if (file.Length > VendorAttachmentRules.MaxBytes)
        {
            return BadRequest(new { message = "File is too large (max 15 MB)." });
        }

        if (!VendorAttachmentRules.IsAllowedExtension(file.FileName))
        {
            return BadRequest(new { message = "Unsupported file type. Allowed: PDF, JPG, PNG, WEBP, DOC, DOCX." });
        }

        using (var probe = file.OpenReadStream())
        {
            if (!VendorAttachmentRules.LooksValid(file.FileName, probe))
            {
                return BadRequest(new { message = "File content does not match its type." });
            }
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
        var (owner, error) = await RequireEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var vendor = await _dbContext.Vendors.FirstOrDefaultAsync(v => v.VendorId == id && v.OwnerId == owner.Id);
        if (vendor == null)
        {
            return NotFound(new { message = "Vendor not found." });
        }

        var attachment = await _dbContext.VendorAttachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId && a.VendorId == id);
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
