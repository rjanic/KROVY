namespace AcKrovy.Core.Models;

public sealed record TimberElementItemAssignment(
    TimberElementMeasurement Measurement,
    TimberElementSignature Signature,
    string ElementId);
