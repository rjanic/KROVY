#if DEBUG
using System.Globalization;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadTextStyleAuditService
{
    private static readonly string[] AuditedStyleNames =
    [
        "ISO",
        "AK_KROVY_TECHNICAL",
        "Standard",
    ];

    public static void Run()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        using var transaction =
            document.Database.TransactionManager.StartOpenCloseTransaction();
        var table = (TextStyleTable)transaction.GetObject(
            document.Database.TextStyleTableId,
            OpenMode.ForRead);
        document.Editor.WriteMessage("\nAK_DEV_TEXT_STYLE_AUDIT");
        foreach (var requestedName in AuditedStyleNames)
        {
            var id = table.Cast<ObjectId>().FirstOrDefault(candidateId =>
            {
                var candidate = (TextStyleTableRecord)transaction.GetObject(
                    candidateId,
                    OpenMode.ForRead);
                return string.Equals(
                    candidate.Name,
                    requestedName,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (id.IsNull)
            {
                document.Editor.WriteMessage($"\n[{requestedName}] MISSING");
                continue;
            }

            var record = (TextStyleTableRecord)transaction.GetObject(
                id,
                OpenMode.ForRead);
            document.Editor.WriteMessage($"\n[{record.Name}] handle={id.Handle}");
            foreach (var property in typeof(TextStyleTableRecord)
                         .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(property =>
                             property.GetIndexParameters().Length == 0 &&
                             property.CanRead)
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                document.Editor.WriteMessage(
                    $"\n  {property.Name}={ReadValue(record, property)}");
            }
        }

        document.Editor.WriteMessage(
            "\nAK_DEV_TEXT_STYLE_AUDIT: END — paste the complete ISO, " +
            "AK_KROVY_TECHNICAL and Standard sections.");
    }

    private static string ReadValue(
        TextStyleTableRecord record,
        PropertyInfo property)
    {
        try
        {
            var value = property.GetValue(record);
            return value switch
            {
                null => "<null>",
                IFormattable formattable =>
                    formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "<null>",
            };
        }
        catch (TargetInvocationException exception)
        {
            return $"<ERROR:{exception.InnerException?.Message ?? exception.Message}>";
        }
        catch (System.Exception exception)
        {
            return $"<ERROR:{exception.Message}>";
        }
    }
}
#endif
