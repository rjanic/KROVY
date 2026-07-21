using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintSelectionRules
{
    public static TimberPostFootprintSelectionDecision Evaluate(
        TimberPostFootprintPromptOutcome promptOutcome,
        bool isLightweightPolyline)
    {
        if (promptOutcome != TimberPostFootprintPromptOutcome.Selected)
        {
            return TimberPostFootprintSelectionDecision.Stop;
        }

        return isLightweightPolyline
            ? TimberPostFootprintSelectionDecision.AcceptEntity
            : TimberPostFootprintSelectionDecision.RejectEntity;
    }
}
