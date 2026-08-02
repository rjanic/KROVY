namespace AcKrovy.Core.Models;

public sealed record TimberItemLeaderTextFitResult(
    bool Fits,
    string ItemText,
    TimberItemLeaderBlockDefinition EvaluatedDefinition,
    double MeasuredTextWidthMm,
    double AvailableInnerWidthMm,
    double HorizontalPaddingMm,
    string DiagnosticReason);
