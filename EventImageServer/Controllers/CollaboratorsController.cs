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
public class CollaboratorsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EventImageServer.Services.EmailService _emailService;
    private readonly EventOwnerResolver _ownerResolver;

    public CollaboratorsController(AppDbContext dbContext, EventImageServer.Services.EmailService emailService, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _ownerResolver = ownerResolver;
    }

    // Lets the frontend ask "what can the current caller do on this event?"
    // without needing to hit a feature-specific endpoint first. Any
    // authenticated caller (owner, CoOwner, or Viewer collaborator) can call
    // this; it never 403s of its own accord, it just reports the role.
    [HttpGet("MyAccess")]
    public async Task<IActionResult> GetMyAccess()
    {
        var resolution = await _ownerResolver.ResolveAsync(User, "Not associated with any event.");
        if (resolution.Owner == null)
        {
            return StatusCode(resolution.ErrorStatusCode ?? 401, new { message = resolution.ErrorMessage });
        }

        return Ok(new
        {
            isReadOnlyViewer = resolution.IsReadOnlyViewer,
            isCollaborator = resolution.Owner.Id != GetUID(),
            eventOwnerId = resolution.Owner.Id
        });
    }

    private string GetUID()
    {
        var claim = User.FindFirst("user_id");
        return claim == null ? string.Empty : claim.Value;
    }

    private static string GenerateInviteToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Invite management (GET/POST/DELETE below) is intentionally scoped to
    // the literal EventOwner themselves — NOT resolved through
    // EventOwnerResolver's collaborator fallback — so an invited
    // collaborator can never invite/revoke further collaborators on the
    // owner's behalf. Auto-provisions a brand-new UID as an EventOwner,
    // matching every other controller's first-request behavior.
    private async Task<(Users? Owner, IActionResult? Error)> RequireLiteralEventOwnerAsync()
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return (null, Unauthorized(new { message = "Invalid token, UID not found." }));
        }

        var user = await _dbContext.Clients.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            user = new Users
            {
                Id = userId,
                Email = User.FindFirst("email")?.Value,
                FullName = User.FindFirst("name")?.Value,
                Role = RoleType.EventOwner
            };
            _dbContext.Clients.Add(user);
            await _dbContext.SaveChangesAsync();
        }

        if (user.Role != RoleType.EventOwner)
        {
            return (null, StatusCode(403, new { message = "Only the event owner manages collaborators." }));
        }

        return (user, null);
    }

    public class InviteCollaboratorRequest
    {
        public string Email { get; set; } = string.Empty;
        public CollaboratorRole Role { get; set; } = CollaboratorRole.Viewer;
    }

    [HttpGet]
    public async Task<IActionResult> GetCollaborators()
    {
        var (owner, error) = await RequireLiteralEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var collaborators = await _dbContext.EventCollaborators.AsNoTracking()
            .Where(c => c.OwnerId == owner.Id)
            .OrderByDescending(c => c.InvitedAt)
            .ToListAsync();

        return Ok(collaborators);
    }

    [HttpPost]
    public async Task<IActionResult> InviteCollaborator([FromBody] InviteCollaboratorRequest request)
    {
        var (owner, error) = await RequireLiteralEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        if (string.Equals(email, owner.Email?.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            return BadRequest(new { message = "You cannot invite yourself." });
        }

        var existing = await _dbContext.EventCollaborators
            .FirstOrDefaultAsync(c => c.OwnerId == owner.Id && c.CollaboratorEmail == email);
        if (existing != null)
        {
            return BadRequest(new { message = "This email has already been invited." });
        }

        var collaborator = new EventCollaborator
        {
            OwnerId = owner.Id!,
            CollaboratorEmail = email,
            Role = request.Role,
            InviteToken = GenerateInviteToken(),
            InvitedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
        };

        _dbContext.EventCollaborators.Add(collaborator);
        await _dbContext.SaveChangesAsync();

        var inviterName = string.IsNullOrWhiteSpace(owner.FullName) ? "The event owner" : owner.FullName;
        var baseUrl = _emailService.PublicBaseUrl.TrimEnd('/');
        var joinLink = $"{baseUrl}/JoinEvent/{collaborator.InviteToken}";
        var emailSent = await _emailService.SendAsync(
            collaborator.CollaboratorEmail,
            $"{inviterName} invited you to help plan their event",
            $"{inviterName} has invited you to collaborate on their event on EventImage.\n\n" +
            $"Click the link below to accept the invite (no account or sign-up needed):\n\n" +
            $"{joinLink}\n\n" +
            $"This invite expires on {collaborator.ExpiresAt:yyyy-MM-dd}.");

        return Ok(new
        {
            collaborator.CollaboratorId,
            collaborator.OwnerId,
            collaborator.CollaboratorEmail,
            collaborator.Role,
            collaborator.InviteToken,
            collaborator.InvitedAt,
            collaborator.ExpiresAt,
            collaborator.AcceptedAt,
            collaborator.CollaboratorUserId,
            emailSent,
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RevokeCollaborator(int id)
    {
        var (owner, error) = await RequireLiteralEventOwnerAsync();
        if (owner == null)
        {
            return error!;
        }

        var collaborator = await _dbContext.EventCollaborators
            .FirstOrDefaultAsync(c => c.CollaboratorId == id && c.OwnerId == owner.Id);
        if (collaborator == null)
        {
            return NotFound(new { message = "Collaborator not found." });
        }

        // Immediate revocation: deleting the row means Resolve()'s
        // collaborator lookup stops finding it on the very next request —
        // no role is ever cached in the JWT to keep valid until it expires.
        _dbContext.EventCollaborators.Remove(collaborator);
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Collaborator removed." });
    }

    // POST /Collaborators/Accept/{token} — the invited person, now
    // authenticated with their own Firebase account, accepts the invite.
    // The JWT's email claim must match the invited email exactly (so only
    // the actual invitee can accept, even though the token itself is also
    // secret/single-use), the token must not be expired, and each invite can
    // only be accepted once.
    [HttpPost("Accept/{token}")]
    public async Task<IActionResult> AcceptInvite(string token)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { message = "Authentication is required to accept an invite." });
        }

        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Invalid token, UID not found." });
        }

        var collaboration = await _dbContext.EventCollaborators
            .FirstOrDefaultAsync(c => c.InviteToken == token);
        if (collaboration == null)
        {
            return NotFound(new { message = "Invite not found." });
        }

        if (collaboration.AcceptedAt != null)
        {
            return BadRequest(new { message = "This invite has already been accepted." });
        }

        if (collaboration.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "This invite has expired." });
        }

        // Anonymous callers (no registration) are authorized by the secret,
        // single-use, expiring invite token alone. Callers with a real
        // account must still match the invited, verified email.
        if (!EventOwnerResolver.IsAnonymous(User))
        {
            var emailVerified = string.Equals(
                User.FindFirst("email_verified")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);
            var callerEmail = (User.FindFirst("email")?.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(callerEmail))
            {
                return StatusCode(403, new { message = "You are not signed in with an email account." });
            }

            if (!string.Equals(callerEmail, collaboration.CollaboratorEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { message = $"This invite was sent to {collaboration.CollaboratorEmail}, but you are signed in as {callerEmail}." });
            }

            if (!emailVerified)
            {
                return StatusCode(403, new { message = "Your email address is not verified yet. Verify it, sign in again, and reopen the invite link." });
            }
        }

        // Auto-provision the accepting user's own Users row (as a plain
        // User, not EventOwner) if this is their first authenticated
        // request, so EventOwnerResolver.Resolve() has a row to find them by.
        var user = await _dbContext.Clients.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            user = new Users
            {
                Id = userId,
                Email = User.FindFirst("email")?.Value,
                FullName = User.FindFirst("name")?.Value,
                Role = RoleType.User
            };
            _dbContext.Clients.Add(user);
        }

        collaboration.CollaboratorUserId = userId;
        collaboration.AcceptedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(new { ownerId = collaboration.OwnerId, role = collaboration.Role });
    }
}
