using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Same-DWG native COPY ownership rehydration for roof-generated rafters.
/// AutoCAD does not remap generated-timber 1005 soft pointers; geometry matching
/// rebinds copied members to the copied roof. Writes join the COPY undo group via
/// the existing StartUndoMark / EndUndoMark lifecycle.
/// </summary>
internal static class RoofGeneratedRafterCopyOwnershipRehydrationService
{
    public static void Process(Document document, string? globalCommandName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName))
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var owners = CollectOwners(document.Database, transaction);
                var observations = CollectObservations(document.Database, transaction);
                if (owners.Count == 0 || observations.Count == 0)
                {
                    return;
                }

                var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);
                var wrote = false;
                foreach (var association in plan.Associations)
                {
                    if (!association.RequiresMetadataRewrite)
                    {
                        continue;
                    }

                    foreach (var member in association.Members)
                    {
                        if (!TryRewriteMember(
                                document.Database,
                                transaction,
                                member,
                                association.OwnerReference,
                                association.ExpectedLayout.Signature))
                        {
                            // Abort the whole write rather than leave a mixed owner set.
                            return;
                        }

                        wrote = true;
                    }
                }

                if (wrote)
                {
                    transaction.Commit();
                }
            }
        }
        catch (System.Exception)
        {
            // Silent internal maintenance — do not break native COPY UX.
        }
    }

    private static IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> CollectOwners(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var owners = new List<RoofGeneratedRafterCopyOwnerTarget>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Polyline polyline ||
                polyline.IsErased)
            {
                continue;
            }

            var stored = RoofDefinitionStore.Read(polyline);
            if (stored.Data is null)
            {
                continue;
            }

            var input = RoofPolylineExtractor.Extract(polyline);
            var validation = RoofFootprintValidator.Validate(input);
            if (!validation.IsValid || validation.Footprint is null)
            {
                continue;
            }

            var restored = RoofDefinitionPersistence.Restore(
                input,
                validation.Footprint,
                stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                continue;
            }

            owners.Add(new RoofGeneratedRafterCopyOwnerTarget(
                polyline.Handle.ToString(),
                restored.Geometry));
        }

        return owners;
    }

    private static IReadOnlyList<RoofGeneratedRafterGeometryObservation> CollectObservations(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var observations = new List<RoofGeneratedRafterGeometryObservation>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Line line ||
                line.IsErased)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line);
            if (generated.Data is null ||
                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
                !metadataStore.TryRead(line, out var timber) ||
                timber is null ||
                timber.ElementType != TimberElementType.Rafter)
            {
                continue;
            }

            observations.Add(new RoofGeneratedRafterGeometryObservation(
                line.Handle.ToString(),
                generated.Data.RoofOwnerReference,
                new RoofRafterGenerationRecipe(
                    timber.WidthMm,
                    timber.HeightMm,
                    generated.Data.RequestedMaximumSpacingMm,
                    timber.Material),
                generated.Data.RoofFace,
                generated.Data.StationIndex,
                generated.Data.StationCount,
                ToPlan(line.StartPoint),
                ToPlan(line.EndPoint),
                generated.Data.LayoutSignature));
        }

        return observations;
    }

    private static bool TryRewriteMember(
        Database database,
        Transaction transaction,
        RoofGeneratedRafterGeometryObservation member,
        string ownerReference,
        string layoutSignature)
    {
        if (!long.TryParse(
                member.MemberKey,
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        ObjectId objectId;
        try
        {
            objectId = database.GetObjectId(false, new Handle(handleValue), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                objectId,
                OpenMode.ForWrite,
                out var entity,
                database) ||
            entity is null)
        {
            return false;
        }

        var current = RoofGeneratedTimberStore.Read(entity);
        if (current.Data is null ||
            current.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
            current.Data.RoofFace != member.Face ||
            current.Data.StationIndex != member.StationIndex ||
            current.Data.StationCount != member.StationCount)
        {
            return false;
        }

        RoofGeneratedTimberStore.Write(
            entity,
            transaction,
            current.Data with
            {
                RoofOwnerReference = ownerReference,
                LayoutSignature = layoutSignature,
            });
        return true;
    }

    private static RoofPoint2D ToPlan(Point3d point) => new(point.X, point.Y);
}
