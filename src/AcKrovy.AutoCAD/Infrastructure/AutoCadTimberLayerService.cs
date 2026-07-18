using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class AutoCadTimberLayerService : ICadLayerService<Entity>
{
    private readonly Database _database;
    private readonly Transaction _transaction;

    public AutoCadTimberLayerService(Database database, Transaction transaction)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public void ApplyLayerForTimberType(Entity entity, TimberElementType elementType, ElementLayerProfile profile) =>
        TimberLayerService.ApplyToEntity(_database, _transaction, entity, elementType, profile);
}
