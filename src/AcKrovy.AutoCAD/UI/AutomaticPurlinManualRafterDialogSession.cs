namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// In-session state for the nested "Rozmery krokvy" dialog when CAD pick
/// suspends the main AK_ROOF_PURLINS window. Never persisted to XData.
/// </summary>
internal sealed record AutomaticPurlinManualRafterDialogSession
{
    public double SeedWidthMm { get; init; }

    public double SeedHeightMm { get; init; }

    /// <summary>
    /// After CAD pick (success or cancel), reopen the manual dialog once the
    /// main window is restored.
    /// </summary>
    public bool ReopenAfterPick { get; init; }

    /// <summary>
    /// True when the pick resolved to a generated ordinary rafter owned by the
    /// current roof. Confirm may adopt SelectedRafter only while W×H still match.
    /// </summary>
    public bool AdoptAsSelectedIfUnedited { get; init; }

    public double? PickedWidthMm { get; init; }

    public double? PickedHeightMm { get; init; }
}
