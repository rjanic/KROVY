namespace AcKrovy.AutoCAD.UI;

internal static class RoofPhysical3DWarningService
{
    public static void Show()
    {
        MemberWarningPreferenceService.WarnDerived3DEdit();
    }
}
