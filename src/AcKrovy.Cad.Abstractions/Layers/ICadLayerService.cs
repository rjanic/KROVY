using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Layers;

public interface ICadLayerService<TEntity>
{
    IReadOnlyList<string> GetAvailableLinetypeNames();

    CadLayerApplyResult ApplyLayerForTimberType(
        TEntity entity,
        TimberElementType elementType,
        ElementLayerProfile profile,
        CadLayerUpdateMode updateMode = CadLayerUpdateMode.PreserveExisting);
}
