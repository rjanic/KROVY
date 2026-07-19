namespace AcKrovy.Core.Models;

public sealed record TimberElementItemNumberingCandidate(
    TimberElementMeasurement Measurement,
    bool IsChanged);
