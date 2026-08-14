namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofDisplayValidationResult(
    RoofDisplayState State,
    RoofDisplayValidationIssue Issues)
{
    public bool IsCurrent => State == RoofDisplayState.Current;
}
