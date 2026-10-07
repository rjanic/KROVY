namespace AcKrovy.Core.Services.Roofs;

public enum RoofOrdinaryEraseAction { None, DeleteCurrentAuto, DeleteIndependent, RejectPhysicalOnly }

public static class RoofOrdinaryEraseRules
{
    public static RoofOrdinaryEraseAction Decide(bool independent, bool planErased, bool physicalErased) =>
        planErased ? independent ? RoofOrdinaryEraseAction.DeleteIndependent : RoofOrdinaryEraseAction.DeleteCurrentAuto :
        physicalErased ? RoofOrdinaryEraseAction.RejectPhysicalOnly : RoofOrdinaryEraseAction.None;
}
