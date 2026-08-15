namespace AcKrovy.Core.Models.Roofs;

public sealed record SimpleGableRafterLayoutResult(
    bool IsValid,
    SimpleGableRafterLayout? Layout,
    SimpleGableRafterLayoutError Error);
