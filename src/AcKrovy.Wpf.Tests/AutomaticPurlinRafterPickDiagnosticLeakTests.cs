using System.Globalization;
using System.Threading;
using System.Windows;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// Guards against ROOF_PURLIN_RAFTER_PICK diagnostics leaking into WPF ToolTip /
/// user-facing ViewModel text after interactive rafter dimension pick.
/// </summary>
[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinRafterPickDiagnosticLeakTests
{
    private const string DiagnosticToken = "ROOF_PURLIN_RAFTER_PICK";

    [Fact]
    public void SectionView_ToolTip_IsLocalizedAndNeverContainsRafterPickDiagnostic()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var previous = Thread.CurrentThread.CurrentUICulture;
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("sk-SK");
                try
                {
                    var view = new AutomaticPurlinRoofSectionView();
                    var tip = Assert.IsType<string>(view.ToolTip);
                    var expected = AutomaticPurlinRoofSectionView.ResolveRafterUserToolTip();

                    Assert.Equal(expected, tip);
                    Assert.False(string.IsNullOrWhiteSpace(tip));
                    Assert.DoesNotContain(DiagnosticToken, tip, StringComparison.Ordinal);
                    Assert.DoesNotContain("dialogOwner=", tip, StringComparison.Ordinal);
                    Assert.DoesNotContain("recipeWidth=", tip, StringComparison.Ordinal);
                    Assert.Equal(
                        UiStrings.GetString("AutomaticPurlin_SelectRafterHint"),
                        tip);
                }
                finally
                {
                    Thread.CurrentThread.CurrentUICulture = previous;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void ResolveRafterUserToolTip_NeverEmitsDiagnosticToken()
    {
        var tip = AutomaticPurlinRoofSectionView.ResolveRafterUserToolTip();
        Assert.False(string.IsNullOrWhiteSpace(tip));
        Assert.DoesNotContain(DiagnosticToken, tip, StringComparison.Ordinal);
        Assert.Equal(
            UiStrings.GetString("AutomaticPurlin_SelectRafterHint"),
            tip);
    }

    [Fact]
    public void AfterSelectedRafterApply_NoUserFacingPropertyContainsDiagnostic()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));

        foreach (var text in EnumerateUserFacingStrings(viewModel))
        {
            Assert.DoesNotContain(DiagnosticToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain("dialogOwner=", text, StringComparison.Ordinal);
            Assert.DoesNotContain("recipeWidth=", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            DiagnosticToken,
            viewModel.PreviewDiagnosticReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClickHint_RemainsLocalizedHelperLine()
    {
        var hint = UiStrings.GetString(
            "AutomaticPurlin_SelectRafterClickHint",
            CultureInfo.GetCultureInfo("sk-SK"));
        Assert.Contains("Klikni", hint, StringComparison.Ordinal);
        Assert.DoesNotContain(DiagnosticToken, hint, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateUserFacingStrings(
        AutomaticPurlinDialogViewModel viewModel)
    {
        yield return viewModel.WindowTitle;
        yield return viewModel.WindowDescription;
        yield return viewModel.ValidationMessage;
        yield return viewModel.RafterDimensionText;
        yield return viewModel.PurlinSectionText;
        yield return viewModel.SchematicCaption;
        yield return viewModel.DatumPersistenceStatus;
        yield return viewModel.LayoutPersistenceStatus;
        yield return viewModel.RelativeReferenceText;
        yield return viewModel.ReferenceLocalZText;
        if (viewModel.SectionPresentation is { } presentation)
        {
            yield return presentation.RafterDimensionText;
            yield return presentation.RafterSectionDimensionText;
            yield return presentation.RafterCaptionText ?? string.Empty;
        }

        yield return viewModel.WallPlateRow.DisplayName;
        yield return viewModel.WallPlateRow.SeatingStatusText;
        yield return viewModel.WallPlateRow.SeatingPolicyText;
        foreach (var row in viewModel.Rows)
        {
            yield return row.DisplayName;
            yield return row.SeatingStatusText;
            yield return row.SeatingPolicyText;
            yield return row.PlanPosition;
            yield return row.BottomRelative;
        }
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel()
    {
        var solved = Solve(RectanglePoints());
        return new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout: null,
            layoutExists: false,
            datum: null,
            datumExists: false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("sk-SK"),
            AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 0);
    }

    private static SolvedFixture Solve(IReadOnlyList<RoofPoint2D> points)
    {
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofPoint2D[] RectanglePoints() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
