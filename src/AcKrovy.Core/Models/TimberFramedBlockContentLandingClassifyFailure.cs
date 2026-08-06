namespace AcKrovy.Core.Models;

/// <summary>
/// Why required Combined column-side classification failed for a landing vector.
/// </summary>
public enum TimberFramedBlockContentLandingClassifyFailure
{
    None = 0,
    DegenerateLandingLength = 1,
    EffectiveOrientationMismatch = 2,
}
