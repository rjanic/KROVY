using AcKrovy.Cad.Abstractions.Metadata;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed class AutoCadTimberElementMetadataStore : ITimberElementMetadataStore<Entity>
{
    private readonly Transaction _transaction;

    public AutoCadTimberElementMetadataStore(Transaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public bool TryRead(Entity entity, out TimberElementData? data) =>
        ElementDataStore.TryRead(entity, _transaction, out data);

    public void Write(Entity entity, TimberElementData data) =>
        ElementDataStore.Write(entity, _transaction, data);
}
