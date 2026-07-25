using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class AutoCadTimberLayerService : ICadLayerService<Entity>
{
    private readonly Database _database;
    private readonly Transaction _transaction;
    private readonly Editor? _editor;
    private readonly HashSet<string> _reportedFallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedLayerConflicts = new(StringComparer.OrdinalIgnoreCase);

    public AutoCadTimberLayerService(
        Database database,
        Transaction transaction,
        Editor? editor = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _editor = editor;
    }

    public IReadOnlyList<string> GetAvailableLinetypeNames() =>
        TimberLayerService.GetAvailableLinetypeNames(_database, _transaction);

    public CadLayerApplyResult ApplyLayerForTimberType(
        Entity entity,
        TimberElementType elementType,
        ElementLayerProfile profile,
        CadLayerUpdateMode updateMode = CadLayerUpdateMode.PreserveExisting)
    {
        var result = TimberLayerService.ApplyToEntity(
            _database,
            _transaction,
            entity,
            elementType,
            profile,
            updateMode);
        if (result.UsedFallback &&
            _editor is not null &&
            _reportedFallbacks.Add(result.RequestedLinetypeName))
        {
            _editor.WriteMessage(UiStrings.Format(
                UiStrings.WarningLinetypeFallbackFormat,
                result.RequestedLinetypeName,
                result.AppliedLinetypeName));
        }

        if (result.PreservedConflictingLayer &&
            _editor is not null &&
            _reportedLayerConflicts.Add(result.PreservedConflictingLayerName!))
        {
            _editor.WriteMessage(UiStrings.Format(
                UiStrings.WarningExistingLayerPreservedFormat,
                result.PreservedConflictingLayerName));
        }

        return result;
    }
}
