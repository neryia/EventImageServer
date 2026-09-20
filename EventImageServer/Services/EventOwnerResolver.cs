using System.Security.Claims;
using EventImageServer.Contexts;
using EventImageServer.Models;

namespace EventImageServer.Services
{
    // Result of resolving the caller as an event owner: either the Users row,
    // or the status code/message a controller should return.
    public class EventOwnerResolution
    {
        public Users? Owner { get; private init; }
        public int? ErrorStatusCode { get; private init; }
        public string? ErrorMessage { get; private init; }
        // True when the caller was resolved via an accepted Viewer-role
        // EventCollaborator invite (as opposed to being the literal
        // EventOwner or a CoOwner collaborator). Controllers use this to
        // reject mutating requests from Viewers while still allowing GETs.
        public bool IsReadOnlyViewer { get; private init; }

        public static EventOwnerResolution Success(Users owner, bool isReadOnlyViewer = false) =>
            new() { Owner = owner, IsReadOnlyViewer = isReadOnlyViewer };

        public static EventOwnerResolution Failure(int statusCode, string message) =>
            new() { ErrorStatusCode = statusCode, ErrorMessage = message };
    }

    // Centralizes the "resolve the caller, auto-provision on first request,
    // require the EventOwner role" logic that was previously duplicated
    // (GetUID + RequireEventOwner) across SeatingController, BudgetController
    // and VendorsController. Controllers own converting a failed resolution
    // into an IActionResult (Unauthorized/StatusCode), since building those
    // requires ControllerBase.
    public class EventOwnerResolver
    {
        private readonly AppDbContext _dbContext;

        public EventOwnerResolver(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public static string GetUID(ClaimsPrincipal principal)
        {
            var claim = principal.FindFirst("user_id");
            return claim == null ? string.Empty : claim.Value;
        }

        // Loads the current user (auto-provisioning them as an EventOwner on
        // first authenticated request, since there is no signup flow) and
        // verifies they hold that role. `roleErrorMessage` lets each feature
        // customize the 403 body (e.g. "Only EventOwners manage vendors.").
        //
        // Collaborator support (Phase 5.B): if the caller is NOT themselves an
        // EventOwner, but has an ACCEPTED EventCollaborator invite, this
        // returns the INVITING owner's Users row instead of the caller's —
        // so every controller's existing `owner.Id`/`owner.EventDate`/etc.
        // usage keeps working unmodified for a collaborator acting on the
        // owner's event, with zero per-controller changes required.
        // `IsReadOnlyViewer` on the result tells callers whether the caller
        // is a Viewer-role collaborator, so mutating endpoints can reject
        // them while GETs keep working — see each controller's
        // RequireEventOwner()/RequireOwnerId() wrapper.
        public EventOwnerResolution Resolve(ClaimsPrincipal principal, string roleErrorMessage)
        {
            var userId = GetUID(principal);
            if (string.IsNullOrEmpty(userId))
            {
                return EventOwnerResolution.Failure(401, "Invalid token, UID not found.");
            }

            var user = _dbContext.Clients.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                user = new Users
                {
                    Id = userId,
                    Email = principal.FindFirst("email")?.Value,
                    FullName = principal.FindFirst("name")?.Value,
                    Role = RoleType.EventOwner
                };
                _dbContext.Clients.Add(user);
                _dbContext.SaveChanges();
            }

            if (user.Role == RoleType.EventOwner)
            {
                return EventOwnerResolution.Success(user);
            }

            var collaboration = _dbContext.EventCollaborators
                .FirstOrDefault(c => c.CollaboratorUserId == userId && c.AcceptedAt != null);
            if (collaboration != null)
            {
                var effectiveOwner = _dbContext.Clients.FirstOrDefault(u => u.Id == collaboration.OwnerId);
                if (effectiveOwner != null)
                {
                    return EventOwnerResolution.Success(effectiveOwner, collaboration.Role == CollaboratorRole.Viewer);
                }
            }

            return EventOwnerResolution.Failure(403, roleErrorMessage);
        }
    }
}
