#if DEBUG
using System.Globalization;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Commands;

/// <summary>DEBUG-only first-host proof for explicit static automatic Purlins.</summary>
public sealed class AutoCadAutomaticPurlinMaterializationCommands
{
    internal const string CommandName = "AK_DEBUG_PURLIN_MATERIALIZE";

    [CommandMethod(CommandName, CommandFlags.Modal | CommandFlags.Redraw)]
    public void Materialize()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var options = new PromptEntityOptions(
            "\nSelect authoritative persisted Hip roof source Polyline: ");
        options.SetRejectMessage("\nSelection must be a Polyline.");
        options.AddAllowedClass(typeof(Polyline), exactMatch: true);
        var selection = editor.GetEntity(options);
        if (selection.Status != PromptStatus.OK)
        {
            return;
        }

        try
        {
            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var layerProfile = ElementLayerProfileStore.Load();
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    selection.ObjectId,
                    OpenMode.ForRead,
                    out var owner,
                    document.Database) ||
                owner is null ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                WriteFailure(editor, "-", "authoritative-roof-required");
                return;
            }

            var ownerReference = owner.Handle.ToString();
            var preparation = RoofAutomaticPurlinMaterializationService.PrepareInTransaction(
                document,
                transaction,
                owner,
                out var preparationFailure);
            if (preparation is null)
            {
                WriteFailure(editor, ownerReference, preparationFailure);
                return;
            }

            var storedLayout = RoofPurlinLayoutStore.Read(owner);
            if (storedLayout.Exists && storedLayout.Data is null)
            {
                WriteFailure(
                    editor,
                    ownerReference,
                    "layout-" + storedLayout.Error);
                return;
            }

            var storedDatum = RoofRelativeElevationDatumStore.Read(owner);
            if (storedDatum.Exists && storedDatum.Data is null)
            {
                WriteFailure(editor, ownerReference, "relative-elevation-datum-" + storedDatum.Error);
                return;
            }

            var firstDatum = false;
            RoofRelativeElevationDatum relativeElevationDatum;
            if (storedDatum.Exists)
            {
                relativeElevationDatum = storedDatum.Data!;
            }
            else
            {
                if (!TryPromptNewDatum(editor, out relativeElevationDatum))
                {
                    return;
                }

                firstDatum = true;
            }

            var layoutResult = "existing";
            var firstLayout = false;
            RoofAutomaticPurlinLayout layout;
            if (storedLayout.Exists)
            {
                layout = storedLayout.Data!;
            }
            else
            {
                if (!TryPromptNewLayout(editor, out layout))
                {
                    return;
                }

                firstLayout = true;
                var layoutValidation = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
                if (!layoutValidation.IsValid)
                {
                    WriteLayout(editor, ownerReference, layout, "invalid");
                    WriteFailure(
                        editor,
                        ownerReference,
                        "layout-" + layoutValidation.Error);
                    return;
                }
            }

            var result = RoofAutomaticPurlinMaterializationService.MaterializePreparedInTransaction(
                document,
                transaction,
                owner,
                preparation,
                layout,
                relativeElevationDatum,
                defaultProfile,
                layerProfile);
            if (!result.IsSuccess)
            {
                WriteLayout(
                    editor,
                    ownerReference,
                    layout,
                    firstLayout ? "rolledback" : layoutResult);
                WriteSummary(editor, result);
                return;
            }

            if (firstLayout || firstDatum)
            {
                if (!owner.IsWriteEnabled)
                {
                    owner.UpgradeOpen();
                }

                if (firstLayout)
                {
                    RoofPurlinLayoutStore.Write(owner, transaction, layout);
                    layoutResult = "created";
                }

                if (firstDatum)
                {
                    RoofRelativeElevationDatumStore.Write(owner, transaction, relativeElevationDatum);
                }
            }

            transaction.Commit();
            WriteLayout(editor, ownerReference, layout, layoutResult);
            foreach (var member in result.Members)
            {
                WriteMember(editor, result.OwnerReference, member);
            }

            WriteSummary(editor, result);
        }
        catch (System.Exception exception)
        {
            WriteFailure(editor, "-", exception.GetType().Name + ":" + exception.Message);
        }
    }

    private static bool TryPromptNewLayout(
        Editor editor,
        out RoofAutomaticPurlinLayout layout)
    {
        layout = RoofAutomaticPurlinLayout.Empty;
        var ridgeOptions = new PromptKeywordOptions(
            "\nCreate automatic ridge Purlin? [Yes/No] <Yes>: ")
        {
            AllowNone = true,
            AppendKeywordsToMessage = false,
        };
        ridgeOptions.Keywords.Add("Yes");
        ridgeOptions.Keywords.Add("No");
        ridgeOptions.Keywords.Default = "Yes";
        var ridgeResult = editor.GetKeywords(ridgeOptions);
        if (ridgeResult.Status is PromptStatus.Cancel or PromptStatus.Error)
        {
            return false;
        }

        var ridgeEnabled = ridgeResult.Status == PromptStatus.None ||
            string.Equals(ridgeResult.StringResult, "Yes", StringComparison.OrdinalIgnoreCase);
        var elevationOptions = new PromptDoubleOptions(
            "\nIntermediate Purlin lower edge height above the relative reference in mm <0 = none>: ")
        {
            AllowNone = true,
            AllowNegative = false,
            AllowZero = true,
            DefaultValue = 0d,
            UseDefaultValue = true,
        };
        var elevationResult = editor.GetDouble(elevationOptions);
        if (elevationResult.Status is PromptStatus.Cancel or PromptStatus.Error)
        {
            return false;
        }

        var elevation = elevationResult.Status == PromptStatus.None
            ? 0d
            : elevationResult.Value;
        var rows = elevation <= 0d
            ? Array.Empty<RoofAutomaticPurlinLayoutItem>()
            : new[]
            {
                new RoofAutomaticPurlinLayoutItem(
                    RoofAutomaticPurlinLayoutItemIdentity.Create(),
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    elevation),
            };
        layout = new RoofAutomaticPurlinLayout(ridgeEnabled, rows);
        return true;
    }

    private static bool TryPromptNewDatum(
        Editor editor,
        out RoofRelativeElevationDatum datum)
    {
        datum = null!;
        var relativeOptions = new PromptDoubleOptions(
            "\nArchitectural relative elevation of the chosen roof-local reference plane in mm: ")
        {
            AllowNone = false,
            AllowNegative = true,
            AllowZero = true,
        };
        var relativeResult = editor.GetDouble(relativeOptions);
        if (relativeResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var localOptions = new PromptDoubleOptions(
            "\nRoof-local Z of that same reference plane in mm <0 = source eave plane>: ")
        {
            AllowNone = true,
            AllowNegative = true,
            AllowZero = true,
            DefaultValue = 0d,
            UseDefaultValue = true,
        };
        var localResult = editor.GetDouble(localOptions);
        if (localResult.Status is PromptStatus.Cancel or PromptStatus.Error)
        {
            return false;
        }

        datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            relativeResult.Value,
            localResult.Status == PromptStatus.None ? 0d : localResult.Value);
        return RoofRelativeElevationDatumRules.Validate(
            RoofRelativeElevationDatumSchema.CurrentVersion,
            datum.ReferenceKind,
            datum.ReferenceRelativeElevationMm,
            datum.ReferenceLocalZMm).IsValid;
    }

    private static void WriteLayout(
        Editor editor,
        string owner,
        RoofAutomaticPurlinLayout layout,
        string result) =>
        editor.WriteMessage(
            "\nROOF_PURLIN_LAYOUT" +
            $" owner={owner}" +
            $" ridgeEnabled={(layout.RidgeEnabled ? "1" : "0")}" +
            $" intermediateRows={layout.IntermediateItems.Count.ToString(CultureInfo.InvariantCulture)}" +
            $" result={result}");

    private static void WriteMember(
        Editor editor,
        string owner,
        RoofAutomaticPurlinMemberResult member) =>
        editor.WriteMessage(
            "\nROOF_PURLIN_MEMBER" +
            $" owner={owner}" +
            $" role={member.Role}" +
            $" layoutId={member.LayoutItemId ?? "-"}" +
            $" key={member.GeneratedKey}" +
            $" handle={member.Handle}" +
            $" elementId={member.ElementId}" +
            $" length={Format(member.PlanLengthMm)}" +
            $" start={FormatPoint(member.Start)}" +
            $" end={FormatPoint(member.End)}" +
            $" bottomRelative={FormatRelative(member.ElevationProfile?.BottomRelativeElevationMm)}" +
            $" centerRelative={FormatRelative(member.ElevationProfile?.CenterRelativeElevationMm)}" +
            $" topRelative={FormatRelative(member.ElevationProfile?.TopRelativeElevationMm)}" +
            $" seatingDepth={FormatNullable(member.ElevationProfile?.SeatingDepthMm)}" +
            $" result={member.Result}");

    private static void WriteSummary(
        Editor editor,
        RoofAutomaticPurlinMaterializationResult result) =>
        editor.WriteMessage(
            "\nROOF_PURLIN_MATERIALIZE" +
            $" owner={result.OwnerReference}" +
            $" desired={result.Desired.ToString(CultureInfo.InvariantCulture)}" +
            $" actual={result.Actual.ToString(CultureInfo.InvariantCulture)}" +
            $" ridge={result.Ridge.ToString(CultureInfo.InvariantCulture)}" +
            $" intermediate={result.Intermediate.ToString(CultureInfo.InvariantCulture)}" +
            $" created={result.Created.ToString(CultureInfo.InvariantCulture)}" +
            $" existing={result.Existing.ToString(CultureInfo.InvariantCulture)}" +
            $" updated={result.Updated.ToString(CultureInfo.InvariantCulture)}" +
            $" staleRemoved={result.StaleRemoved.ToString(CultureInfo.InvariantCulture)}" +
            $" duplicates={result.Duplicates.ToString(CultureInfo.InvariantCulture)}" +
            $" missing={result.Missing.ToString(CultureInfo.InvariantCulture)}" +
            $" groupCanonical={(result.GroupCanonical ? "1" : "0")}" +
            $" result={result.Result}");

    private static void WriteFailure(Editor editor, string owner, string result) =>
        WriteSummary(
            editor,
            RoofAutomaticPurlinMaterializationResult.Failure(result, owner));

    private static string FormatPoint(Point3d point) =>
        $"({Format(point.X)},{Format(point.Y)},{Format(point.Z)})";

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatRelative(double? value) =>
        value is null ? "-" : RoofRelativeElevationDatumRules.FormatMetres(value.Value);

    private static string FormatNullable(double? value) => value is null ? "-" : Format(value.Value);
}
#endif
