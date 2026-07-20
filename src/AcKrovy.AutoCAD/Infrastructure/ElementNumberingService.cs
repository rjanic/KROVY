using System.Text.RegularExpressions;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class ElementNumberingService
{
    public static int GetNextNumber(Database database, Transaction transaction, TimberElementType type)
    {
        var prefix = TimberElementIdentityPrefixes.GetPrefix(type);
        var pattern = new Regex($"^{Regex.Escape(prefix)}(?<number>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var highest = 0;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                !metadataStore.TryRead(entity, out var existing) ||
                existing is null || existing.ElementType != type)
            {
                continue;
            }

            var match = pattern.Match(existing.ElementId);
            if (match.Success && int.TryParse(match.Groups["number"].Value, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest + 1;
    }
}
