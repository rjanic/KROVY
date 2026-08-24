namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofRafterRequestValidationResult(
    RoofRafterCreationRequest? Request,
    RoofRafterLayout? Layout,
    RoofRafterRequestValidationError Error)
{
    public bool IsValid =>
        Error == RoofRafterRequestValidationError.None &&
        Request is not null &&
        Layout is not null;
}
