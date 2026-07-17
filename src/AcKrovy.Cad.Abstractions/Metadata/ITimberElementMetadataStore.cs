using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Metadata;

public interface ITimberElementMetadataStore<TEntity>
{
    bool TryRead(TEntity entity, out TimberElementData? data);

    void Write(TEntity entity, TimberElementData data);
}
