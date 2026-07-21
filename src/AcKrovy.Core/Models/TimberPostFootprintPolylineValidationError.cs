namespace AcKrovy.Core.Models;

public enum TimberPostFootprintPolylineValidationError
{
    None = 0,
    NotClosed = 1,
    WrongSegmentCount = 2,
    CurvedSegment = 3,
    UnsupportedPlane = 4,
}
