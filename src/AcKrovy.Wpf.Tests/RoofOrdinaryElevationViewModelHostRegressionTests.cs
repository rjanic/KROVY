using System.Globalization;
using System.Threading;
using System.Windows;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class RoofOrdinaryElevationViewModelHostRegressionTests
{
    [Fact]
    public void ReversedFallDirection_DisplaysStructuralLowerUpper_NotStartEnd()
    {
        // Start high, End low (Obrátený smer spádu) → structural lower is End.
        var state = StructuralMemberElevationRules.CreateSloped(3000d, 0d);
        var vm = new RoofOrdinaryElevationViewModel(state, planLengthMm: 3000d, heightMm: 160d,
            CultureInfo.GetCultureInfo("sk-SK"));

        // Lower display must be the lower Z (~0), not Start (~3000).
        Assert.Contains("0,000", vm.LowerZText);
        Assert.DoesNotContain("3,000", vm.LowerZText);
        Assert.Contains("3,000", vm.UpperZText);
        // Slope display is positive magnitude; fall is separate (Start→End signed is negative).
        Assert.False(vm.SlopeText.StartsWith('-'));
        Assert.Contains("45", vm.SlopeText);
        Assert.True(vm.SignedSlopeDegrees < 0d);
        Assert.False(vm.IsSlopeDirectionReversed); // Start→End downhill
    }

    [Fact]
    public void LowerUpperCrossing_SwapsRolesAndReversesFall_KeepsPositiveSlope()
    {
        // A=3000, B=6000 → edit A (shown as lower) to 7000 → lower=B, upper=A.
        var state = StructuralMemberElevationRules.CreateSloped(3000d, 6000d);
        var vm = new RoofOrdinaryElevationViewModel(state, planLengthMm: 3000d, heightMm: 160d,
            CultureInfo.GetCultureInfo("sk-SK"));
        Assert.True(vm.IsSlopeDirectionReversed);

        vm.LowerZText = "+7,000";
        Assert.Contains("6,000", vm.LowerZText);
        Assert.Contains("7,000", vm.UpperZText);
        var requested = vm.BuildRequestedState();
        Assert.Equal(7000d, requested.AxisStartElevationMm, 1);
        Assert.Equal(6000d, requested.AxisEndElevationMm, 1);
        Assert.False(vm.IsSlopeDirectionReversed);
        Assert.False(vm.SlopeText.StartsWith('-'));
        Assert.True(vm.AbsoluteSlopeDegrees > 0d);
        Assert.False(vm.IsHorizontal);
    }

    [Fact]
    public void HorizontalEqualZ_SlopeZero_NoFallInfluence()
    {
        var state = StructuralMemberElevationRules.CreateUniform(2500d);
        var vm = new RoofOrdinaryElevationViewModel(state, planLengthMm: 4000d, heightMm: 160d,
            CultureInfo.GetCultureInfo("sk-SK"));
        Assert.True(vm.IsHorizontal);
        Assert.Equal(0d, vm.AbsoluteSlopeDegrees, 6);
        Assert.False(vm.IsSlopeDirectionReversed);
        Assert.Contains("0,00", vm.SlopeText);
    }

    [Fact]
    public void OsToVhToShToOs_DoesNotChangeGeometryFlags()
    {
        var state = StructuralMemberElevationRules.CreateSloped(500d, 2500d);
        var vm = new RoofOrdinaryElevationViewModel(state, 4000d, 160d, CultureInfo.GetCultureInfo("sk-SK"));
        Assert.False(vm.GeometryChanged);

        vm.VH_Selected = true;
        Assert.False(vm.GeometryChanged);
        Assert.True(vm.DisplayReferenceChanged);

        vm.SH_Selected = true;
        Assert.False(vm.GeometryChanged);

        vm.OS_Selected = true;
        Assert.False(vm.GeometryChanged);
        // Display reference restored to initial OS → no pending display change either.
        Assert.False(vm.DisplayReferenceChanged);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void ElementEditWindow_DoesNotShowSyntheticZeroWhenResolverDeniedState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppLanguageService.Apply("sk");
                // No elevationState → section must stay collapsed (unavailable), not show 0/0/0.
                var window = new ElementEditWindow(new TimberElementData
                {
                    SchemaVersion = TimberElementDataSchema.CurrentVersion,
                    ElementType = TimberElementType.Rafter,
                    SlopeDegrees = 45,
                }, isNewAssignment: false)
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, window.ElevationSection.Visibility);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
