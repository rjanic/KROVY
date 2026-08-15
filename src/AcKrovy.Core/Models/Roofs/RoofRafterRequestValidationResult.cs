namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofRafterRequestValidationResult(
    RoofRafterCreationRequest? Request,
    SimpleGableRafterLayout? Layout,
    RoofRafterRequestValidationError Error)
{
    public bool IsValid =>
        Error == RoofRafterRequestValidationError.None &&
        Request is not null &&
        Layout is not null;
}
