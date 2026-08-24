namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofRafterLayoutResult(
    bool IsValid,
    RoofRafterLayout? Layout,
    RoofRafterLayoutError Error);
