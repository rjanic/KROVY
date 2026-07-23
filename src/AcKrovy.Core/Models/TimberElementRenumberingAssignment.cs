namespace AcKrovy.Core.Models;

public sealed record TimberElementRenumberingAssignment(
    TimberElementMeasurement Measurement,
    TimberElementSignature Signature,
    string PreviousElementId,
    string ElementId)
{
    public bool IsChanged => !string.Equals(
        PreviousElementId,
        ElementId,
        StringComparison.OrdinalIgnoreCase);
}
