using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Converts between the user-facing physical HIGH-to-LOW fall direction and the
/// stable persisted/domain LOW-to-HIGH direction. This is presentation-only.
/// </summary>
public static class MonopitchRoofDirectionPresentationRules
{
    public static RoofDirection2D ToCanonicalLowToHigh(RoofDirection2D highToLow) =>
        Reverse(highToLow);

    public static RoofDirection2D ToPhysicalHighToLow(RoofDirection2D lowToHigh) =>
        Reverse(lowToHigh);

    private static RoofDirection2D Reverse(RoofDirection2D direction)
    {
        if (!RoofDirection2D.TryCreate(-direction.X, -direction.Y, out var reversed))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return reversed;
    }
}
