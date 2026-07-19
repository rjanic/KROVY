using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadObjectIdAccess
{
    public static bool TryGetObject<TObject>(
        Transaction transaction,
        ObjectId id,
        OpenMode openMode,
        out TObject? value,
        Database? expectedDatabase = null)
        where TObject : DBObject
    {
        ArgumentNullException.ThrowIfNull(transaction);

        value = null;
        if (id.IsNull || !id.IsValid || id.IsErased)
        {
            return false;
        }

        if (expectedDatabase is not null && id.Database != expectedDatabase)
        {
            return false;
        }

        try
        {
            if (transaction.GetObject(id, openMode, false) is not TObject candidate ||
                candidate.IsErased)
            {
                return false;
            }

            value = candidate;
            return true;
        }
        catch (AcadException ex) when (ex.ErrorStatus == ErrorStatus.WasErased)
        {
            return false;
        }
    }
}
