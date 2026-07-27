using System.Text;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Infrastructure.Diagnostics;

namespace AcKrovy.AutoCAD.Diagnostics;

internal static class DiagnosticsSupportSummaryBuilder
{
    public static string Build(
        IReadOnlyList<DiagnosticsInfoRow> informationRows,
        IReadOnlyList<DiagnosticsInfoRow> settingsRows,
        IReadOnlyList<string> events,
        string settingsHeading,
        string eventsHeading)
    {
        ArgumentNullException.ThrowIfNull(informationRows);
        ArgumentNullException.ThrowIfNull(settingsRows);
        ArgumentNullException.ThrowIfNull(events);

        var builder = new StringBuilder();
        builder.AppendLine("ACAD KROVY");
        foreach (var row in informationRows)
        {
            builder.AppendLine($"{row.Label}: {row.Value}");
        }

        builder.AppendLine();
        builder.AppendLine(settingsHeading);
        foreach (var row in settingsRows)
        {
            builder.AppendLine($"{row.Label}: {row.Value}");
        }

        builder.AppendLine();
        builder.AppendLine(eventsHeading);
        foreach (var diagnosticEvent in events)
        {
            builder.AppendLine(diagnosticEvent);
        }

        return DiagnosticSanitizer.Sanitize(builder.ToString());
    }
}
