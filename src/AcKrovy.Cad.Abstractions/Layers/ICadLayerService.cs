using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Layers;

public interface ICadLayerService<TEntity>
{
    void ApplyLayerForTimberType(TEntity entity, TimberElementType elementType, ElementLayerProfile profile);
}
