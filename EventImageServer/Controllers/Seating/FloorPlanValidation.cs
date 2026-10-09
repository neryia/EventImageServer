using EventImageServer.Models;

namespace EventImageServer.Controllers.Seating;

// Shared range checks for floor plan geometry (tables, venue elements, canvas size).
public static class FloorPlanValidation
{
    public const int MinCanvas = 300;
    public const int MaxCanvas = 5000;
    public const double MaxCoordinate = 10000;
    public const double MaxElementSize = 5000;
    public const int MaxLabelLength = 60;
    public const int MaxElements = 500;

    public static bool IsFiniteWithin(double value, double limit) =>
        double.IsFinite(value) && Math.Abs(value) <= limit;

    public static double NormalizeRotation(double rotation)
    {
        var r = rotation % 360;
        return r < 0 ? r + 360 : r;
    }

    public static string? ValidatePosition(double x, double y, double rotation)
    {
        if (!IsFiniteWithin(x, MaxCoordinate) || !IsFiniteWithin(y, MaxCoordinate))
        {
            return "Position is out of range.";
        }
        if (!double.IsFinite(rotation))
        {
            return "Rotation must be a finite number.";
        }
        return null;
    }

    public static string? ValidateElement(VenueElementRequest e)
    {
        if (!Enum.IsDefined(typeof(VenueElementKind), e.Kind))
        {
            return "Unknown element kind.";
        }
        if (e.Label != null && e.Label.Length > MaxLabelLength)
        {
            return $"Label cannot exceed {MaxLabelLength} characters.";
        }
        if (!double.IsFinite(e.Width) || !double.IsFinite(e.Height)
            || e.Width < 1 || e.Height < 1 || e.Width > MaxElementSize || e.Height > MaxElementSize)
        {
            return $"Element size must be between 1 and {MaxElementSize}.";
        }
        return ValidatePosition(e.X, e.Y, e.Rotation);
    }

    public static bool IsValidCanvas(int? value) =>
        !value.HasValue || (value.Value >= MinCanvas && value.Value <= MaxCanvas);
}
