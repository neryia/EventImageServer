using EventImageServer.Models;

namespace EventImageServer.Controllers.Seating;

public class TableRequest
{
    public string? Name { get; set; }
    public string? Shape { get; set; }
    public string Tag { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int CapacityOnSides { get; set; }
    public int CapacityOnTopAndBottom { get; set; }
    // Null keeps the existing side on update (Both on create).
    public EventSide? Side { get; set; }
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public double Rotation { get; set; }
}

public class RsvpDeadlineRequest
{
    public DateTime? Deadline { get; set; }
}

public class EventDateRequest
{
    public DateTime? EventDate { get; set; }
}

public class SendMessagesRequest
{
    public List<int>? GuestIds { get; set; }
    public MessageChannel Channel { get; set; } = MessageChannel.Sms;
}

public class AutoAssignRequest
{
    public List<int>? LockedGuestIds { get; set; }
}

public class GuestAssignment
{
    public int GuestId { get; set; }
    public int? TableId { get; set; }
}

public class CategoryColorRequest
{
    public string Color { get; set; } = string.Empty;
}

public class CategorySideRequest
{
    public EventSide Side { get; set; } = EventSide.Both;
    public string? Label { get; set; }
    public string? Color { get; set; }
}

public class SaveArrangementRequest
{
    public List<GuestAssignment> Assignments { get; set; } = new();
}

public class LayoutTableRequest : TableRequest
{
    public int? TableId { get; set; }
    public string ClientKey { get; set; } = string.Empty;
}

public class LayoutGuestAssignment
{
    public int GuestId { get; set; }
    public string? TableKey { get; set; }
}

public class LayoutRequest
{
    public List<LayoutTableRequest> Tables { get; set; } = new();
    public List<LayoutGuestAssignment> Assignments { get; set; } = new();
}

public class ReminderSettingsRequest
{
    public bool AutoRemindersEnabled { get; set; }
    public List<int> ReminderOffsets { get; set; } = new();
}

public class TablePositionRequest
{
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Rotation { get; set; }
}

public class VenueElementRequest
{
    public VenueElementKind Kind { get; set; }
    public string? Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 80;
    public double Height { get; set; } = 80;
    public double Rotation { get; set; }
}

public class SeatingConstraintRequest
{
    public int GuestAId { get; set; }
    public int GuestBId { get; set; }
    public ConstraintKind Kind { get; set; }
}

public class CheckInRequest
{
    public int? CheckedInCount { get; set; }
}

public class RsvpStatusUpdateRequest
{
    public RsvpStatus Status { get; set; }
    public int NumberOfGuests { get; set; }
}
