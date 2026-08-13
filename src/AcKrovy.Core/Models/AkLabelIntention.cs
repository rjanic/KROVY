namespace AcKrovy.Core.Models;

/// <summary>
/// Explicit AK_LABEL command intention. Annotation-only; never mutates timber sources.
/// </summary>
public enum AkLabelIntention
{
    /// <summary>
    /// Recreate only missing/deleted expected annotations. Existing owned
    /// annotations are left untouched (manual ROTATE/MOVE/grip survive).
    /// </summary>
    MissingOnly = 0,

    /// <summary>
    /// Canonical reset of selected owned annotation groups only.
    /// </summary>
    ResetSelected = 1,

    /// <summary>
    /// Canonical reset of every KROVY annotation in the drawing (after confirm).
    /// </summary>
    ResetAll = 2,
}

/// <summary>
/// Per-source action decided for an AK_LABEL intention.
/// </summary>
public enum AkLabelSourceAction
{
    NoOp = 0,
    EnsureMissing = 1,
    ForceCanonicalRecreate = 2,
}
