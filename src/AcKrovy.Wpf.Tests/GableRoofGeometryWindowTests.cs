using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class GableRoofGeometryWindowTests
{
    [Fact]
    public void ViewModel_DefaultsToSymmetricAndLoadsRotatedCanonicalDimensions()
    {
        var viewModel = new GableRoofGeometryViewModel(RotatedRectangle(10000d, 6000d, 37d));

        Assert.Equal(RoofKind.SimpleGable, viewModel.SelectedKind);
        Assert.True(viewModel.IsSymmetricMode);
        Assert.False(viewModel.IsAsymmetricMode);
        var dimensions = new[] { viewModel.DimensionAMm, viewModel.DimensionBMm }
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(6000d, dimensions[0], 7);
        Assert.Equal(10000d, dimensions[1], 7);
        Assert.False(viewModel.CanPreview);
        Assert.NotNull(viewModel.SectionState);
        Assert.Equal(viewModel.SectionState!.RunAMm, viewModel.SectionState.RunBMm, 8);
    }

    [Fact]
    public void AsymmetricInputs_UpdateLiveSectionAndPreserveSignedDelta()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.EaveHeightDifferenceText = "0";
        var zeroRunA = viewModel.SectionState!.RunAMm;

        viewModel.EaveHeightDifferenceText = "450";

        Assert.True(viewModel.CanApply);
        Assert.Equal(450d, viewModel.SectionState!.EaveBElevationMm);
        Assert.True(viewModel.SectionState.RunAMm > zeroRunA);
        Assert.Equal(20d, viewModel.SectionState.AlphaDegrees);
        Assert.Equal(35d, viewModel.SectionState.BetaDegrees);
        Assert.True(viewModel.TryGetGeometry(out var geometry));
        Assert.Equal(450d, geometry!.EaveHeightDifferenceMm);
    }

    [Fact]
    public void SymmetricPitch_UpdatesBothPlanesAndKeepsRidgeCentered()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));

        viewModel.AlphaText = "25";

        Assert.NotNull(viewModel.SectionState);
        Assert.Equal(25d, viewModel.SectionState!.AlphaDegrees);
        Assert.Equal(25d, viewModel.SectionState.BetaDegrees);
        Assert.Equal(viewModel.SectionState.RunAMm, viewModel.SectionState.RunBMm, 9);
        Assert.Equal(0d, viewModel.SectionState.EaveBElevationMm);
    }

    [Fact]
    public void UniformLayout_RendersTwentyDegreesFromHorizontalWithoutSteepening()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        viewModel.AlphaText = "20";

        var layout = Assert.IsType<GableRoofSectionLayout>(
            GableRoofSectionLayoutCalculator.Create(viewModel.SectionState!, 560d, 360d));
        var renderedAlpha = Degrees(Math.Atan2(
            layout.EaveA.Y - layout.Ridge.Y,
            Math.Abs(layout.Ridge.X - layout.EaveA.X)));
        var renderedBeta = Degrees(Math.Atan2(
            layout.EaveB.Y - layout.Ridge.Y,
            Math.Abs(layout.EaveB.X - layout.Ridge.X)));

        Assert.Equal(layout.ScaleX, layout.ScaleY, 12);
        Assert.Equal(20d, renderedAlpha, 8);
        Assert.Equal(20d, renderedBeta, 8);
        Assert.Equal(
            (layout.EaveA.X + layout.EaveB.X) / 2d,
            layout.Ridge.X,
            8);
    }

    [Fact]
    public void AngleAnnotations_UseHorizontalReferencesAndKeepLabelsAboveRoofPlanes()
    {
        var leftEave = new Point(80d, 260d);
        var ridge = new Point(280d, 120d);
        var rightEave = new Point(500d, 280d);

        var left = GableRoofSectionControl.CreateAngleAnnotation(leftEave, ridge);
        var right = GableRoofSectionControl.CreateAngleAnnotation(rightEave, ridge);

        Assert.Equal(left.ReferenceStart.Y, left.ReferenceEnd.Y, 8);
        Assert.Equal(right.ReferenceStart.Y, right.ReferenceEnd.Y, 8);
        Assert.True(leftEave.X < left.Anchor.X && left.Anchor.X < ridge.X);
        Assert.True(ridge.X < right.Anchor.X && right.Anchor.X < rightEave.X);
        Assert.True(left.Anchor.Y < left.RoofCenterlinePoint.Y);
        Assert.True(right.Anchor.Y < right.RoofCenterlinePoint.Y);
        Assert.True(left.Anchor.X < left.RoofCenterlinePoint.X);
        Assert.True(right.Anchor.X > right.RoofCenterlinePoint.X);
        Assert.True(left.LabelOrigin.Y < left.Anchor.Y - 15d);
        Assert.True(right.LabelOrigin.Y < right.Anchor.Y - 15d);
        Assert.True(left.LabelOrigin.X < left.Anchor.X);
        Assert.True(right.LabelOrigin.X > right.Anchor.X);
    }

    [Theory]
    [InlineData(450d, true)]
    [InlineData(-450d, false)]
    public void AsymmetricLayout_PreservesBetaAndSignedEaveElevation(
        double deltaHeight,
        bool eaveBIsAbove)
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.EaveHeightDifferenceText = deltaHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var layout = Assert.IsType<GableRoofSectionLayout>(
            GableRoofSectionLayoutCalculator.Create(viewModel.SectionState!, 560d, 360d));
        var renderedBeta = Degrees(Math.Atan2(
            layout.EaveB.Y - layout.Ridge.Y,
            Math.Abs(layout.EaveB.X - layout.Ridge.X)));

        Assert.Equal(35d, renderedBeta, 8);
        Assert.Equal(eaveBIsAbove, layout.EaveB.Y < layout.EaveA.Y);
    }

    [Fact]
    public void AsymmetricModes_ComputeTheSamePhysicalInputsInBothDirections()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.EaveHeightDifferenceText = "450";
        Assert.True(viewModel.IsDeltaHeightMode);
        Assert.True(viewModel.TryGetGeometry(out var mode1));

        viewModel.AsymmetricInputMode = AsymmetricGableInputMode.RidgeDistanceFromEaveA;
        Assert.True(viewModel.IsRidgeDistanceMode);
        Assert.True(viewModel.TryGetGeometry(out var switchedToMode2));
        Assert.InRange(Math.Abs(mode1!.Face0RunMm - switchedToMode2!.Face0RunMm), 0d, 0.51d);
        Assert.InRange(
            Math.Abs(mode1.EaveHeightDifferenceMm - switchedToMode2.EaveHeightDifferenceMm),
            0d,
            0.51d);

        viewModel.RidgeDistanceFromEaveAText = "2500";
        Assert.True(viewModel.TryGetGeometry(out var enteredByRidge));
        var expectedDelta = 2500d * Math.Tan(20d * Math.PI / 180d) -
            3500d * Math.Tan(35d * Math.PI / 180d);
        Assert.Equal(2500d, enteredByRidge!.Face0RunMm, 7);
        Assert.Equal(expectedDelta, enteredByRidge.EaveHeightDifferenceMm, 7);
        Assert.Equal(expectedDelta, viewModel.SectionState!.EaveBElevationMm, 7);

        viewModel.AsymmetricInputMode = AsymmetricGableInputMode.EaveHeightDifference;
        Assert.True(viewModel.IsDeltaHeightMode);
        Assert.True(viewModel.TryGetGeometry(out var switchedBackToMode1));
        Assert.InRange(
            Math.Abs(enteredByRidge.Face0RunMm - switchedBackToMode1!.Face0RunMm),
            0d,
            1d);
        Assert.InRange(
            Math.Abs(enteredByRidge.EaveHeightDifferenceMm - switchedBackToMode1.EaveHeightDifferenceMm),
            0d,
            0.51d);
    }

    [Fact]
    public void MirroredDeltaMode_SwapsPhysicalFacesAndPreservesUiSideSemantics()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.EaveHeightDifferenceText = "450";

        viewModel.IsAsymmetryMirrored = true;

        Assert.True(viewModel.TryGetGeometry(out var geometry));
        Assert.Equal(35d, geometry!.Face0SlopeDegrees);
        Assert.Equal(20d, geometry.Face1SlopeDegrees);
        Assert.Equal(-450d, geometry.EaveHeightDifferenceMm);
        Assert.True(viewModel.SectionState!.IsMirrored);
        Assert.Equal(20d, viewModel.SectionState.AlphaDegrees);
        Assert.Equal(35d, viewModel.SectionState.BetaDegrees);
        Assert.Equal(450d, viewModel.SectionState.EaveBElevationMm -
            viewModel.SectionState.EaveAElevationMm);
        Assert.Equal(geometry.Face1RunMm, viewModel.SectionState.RunAMm, 7);
        Assert.Equal(geometry.Face0RunMm, viewModel.SectionState.RunBMm, 7);

        var layout = Assert.IsType<GableRoofSectionLayout>(
            GableRoofSectionLayoutCalculator.Create(viewModel.SectionState, 560d, 360d));
        Assert.True(layout.EaveA.X < layout.Ridge.X);
        Assert.True(layout.Ridge.X < layout.EaveB.X);
        Assert.True(layout.EaveB.Y < layout.EaveA.Y);
    }

    [Fact]
    public void SectionProjection_PlacesPositiveTransverseFaceOnTheLeftAndMirrorsWithUiState()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.EaveHeightDifferenceText = "450";

        Assert.True(viewModel.TryGetGeometry(out var normalGeometry));
        Assert.Equal(SimpleGableRoofFaceSide.NegativeTransverse, normalGeometry!.Faces[0].Side);
        Assert.Equal(SimpleGableRoofFaceSide.PositiveTransverse, normalGeometry.Faces[1].Side);
        var normal = Assert.IsType<GableRoofSectionLayout>(
            GableRoofSectionLayoutCalculator.Create(viewModel.SectionState!, 560d, 360d));
        Assert.True(normal.EaveB.X < normal.Ridge.X);
        Assert.True(normal.Ridge.X < normal.EaveA.X);

        viewModel.IsAsymmetryMirrored = true;

        Assert.True(viewModel.TryGetGeometry(out var mirroredGeometry));
        var mirrored = Assert.IsType<GableRoofSectionLayout>(
            GableRoofSectionLayoutCalculator.Create(viewModel.SectionState!, 560d, 360d));
        Assert.True(mirrored.EaveA.X < mirrored.Ridge.X);
        Assert.True(mirrored.Ridge.X < mirrored.EaveB.X);
        Assert.Equal(35d, mirroredGeometry!.Face0SlopeDegrees);
        Assert.Equal(20d, mirroredGeometry.Face1SlopeDegrees);
        Assert.Equal(-450d, mirroredGeometry.EaveHeightDifferenceMm);
    }

    [Fact]
    public void MirroredRidgeDistanceMode_MapsUiRunAIntoPhysicalFaceOne()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AlphaText = "20";
        viewModel.BetaText = "35";
        viewModel.IsAsymmetryMirrored = true;
        viewModel.AsymmetricInputMode = AsymmetricGableInputMode.RidgeDistanceFromEaveA;

        viewModel.RidgeDistanceFromEaveAText = "2500";

        var expectedUiDelta = 2500d * Math.Tan(20d * Math.PI / 180d) -
            3500d * Math.Tan(35d * Math.PI / 180d);
        Assert.True(viewModel.TryGetGeometry(out var geometry));
        Assert.Equal(2500d, geometry!.Face1RunMm, 7);
        Assert.Equal(-expectedUiDelta, geometry.EaveHeightDifferenceMm, 7);
        Assert.Equal(expectedUiDelta, viewModel.SectionState!.EaveBElevationMm -
            viewModel.SectionState.EaveAElevationMm, 7);
        Assert.Matches("^-?[0-9]+$", viewModel.EaveHeightDifferenceText);
    }

    [Fact]
    public void MillimeterPresentation_UsesWholeNumbersAndRejectsFractionalInputs()
    {
        var viewModel = new GableRoofGeometryViewModel(RotatedRectangle(10000d, 6000d, 37d));
        Assert.DoesNotContain('.', viewModel.DimensionAText);
        Assert.DoesNotContain(',', viewModel.DimensionAText);
        Assert.DoesNotContain('.', viewModel.DimensionBText);
        Assert.DoesNotContain(',', viewModel.DimensionBText);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;

        viewModel.EaveHeightDifferenceText = "450.5";

        Assert.False(viewModel.CanApply);
        Assert.NotEmpty(viewModel.ValidationMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("6000")]
    [InlineData("7000")]
    [InlineData("NaN")]
    public void RidgeDistanceMode_InvalidInputBlocksPreviewAndCreate(string value)
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        viewModel.SetRidgeDirection(direction);
        viewModel.SelectedKind = RoofKind.AsymmetricGable;
        viewModel.AsymmetricInputMode = AsymmetricGableInputMode.RidgeDistanceFromEaveA;

        viewModel.RidgeDistanceFromEaveAText = value;

        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryGetGeometry(out _));
        Assert.NotEmpty(viewModel.ValidationMessage);
    }

    [Fact]
    public void Window_LoadsInAllLanguagesAndSwitchesVisibleFieldSets()
    {
        RunSta(() =>
        {
            foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
            {
                AppLanguageService.Apply(language);
                foreach (var theme in new[] { SettingsTheme.Light, SettingsTheme.Dark })
                {
                    var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
                    var window = CreateOffscreenWindow(viewModel, theme);
                    window.Height = window.MinHeight;
                    window.Show();
                    window.UpdateLayout();

                    Assert.Equal(Visibility.Visible, window.SymmetricFieldsPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.AsymmetricFieldsPanel.Visibility);
                    Assert.False(window.MirrorAsymmetryPanel.IsVisible);
                    Assert.True(window.SymmetricRadioButton.IsChecked);
                    Assert.True(window.SymmetricRoofTypeIcon.IsVisible);
                    Assert.True(window.AsymmetricRoofTypeIcon.IsVisible);
                    Assert.True(window.PickRidgeDirectionIcon.IsVisible);
                    Assert.Same(
                        window.FindResource("SettingsTextPrimaryBrush"),
                        window.Foreground);
                    Assert.Same(
                        window.FindResource("RoofModeCardStyle"),
                        window.SymmetricRadioButton.Style);
                    viewModel.SelectedKind = RoofKind.AsymmetricGable;
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, window.SymmetricFieldsPanel.Visibility);
                    Assert.Equal(Visibility.Visible, window.AsymmetricFieldsPanel.Visibility);
                    Assert.True(window.MirrorAsymmetryPanel.IsVisible);
                    Assert.True(window.AsymmetricRadioButton.IsChecked);
                    AssertElementFitsInside(window.PickRidgeDirectionButton, window.LeftInputCard);
                    Assert.Equal(Visibility.Visible, window.DeltaHeightInputPanel.Visibility);
                    Assert.Equal(Visibility.Collapsed, window.RidgeDistanceInputPanel.Visibility);
                    viewModel.AsymmetricInputMode = AsymmetricGableInputMode.RidgeDistanceFromEaveA;
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, window.DeltaHeightInputPanel.Visibility);
                    Assert.Equal(Visibility.Visible, window.RidgeDistanceInputPanel.Visibility);
                    Assert.True(window.CalculatedDeltaHeightTextBox.IsReadOnly);
                    AssertElementFitsInside(window.PickRidgeDirectionButton, window.LeftInputCard);
                    Assert.DoesNotContain("RoofGeometryWindow_", window.Title);
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void PreviewAndDirectionActions_HideAndReuseSameWindowWithoutLosingState()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
            Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
            viewModel.SetRidgeDirection(direction);
            viewModel.AlphaText = "27.5";
            var window = CreateOffscreenWindow(viewModel, SettingsTheme.Light);
            window.Show();
            window.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(window.IsVisible);
            Assert.Equal(GableRoofGeometryDialogAction.Preview, window.RequestedAction);
            Assert.Equal("27.5", viewModel.AlphaText);

            window.PrepareForInteraction();
            window.Show();
            window.PickRidgeDirectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(GableRoofGeometryDialogAction.PickRidgeDirection, window.RequestedAction);
            Assert.Equal("27.5", viewModel.AlphaText);
            Assert.True(viewModel.HasRidgeDirection);
            window.Close();
        });
    }

    [Fact]
    public void PreviewApplyAndCancelActions_RespectValidationAndDoNotCloseEarly()
    {
        RunSta(() =>
        {
            var viewModel = new GableRoofGeometryViewModel(Rectangle(10000d, 6000d));
            var window = CreateOffscreenWindow(viewModel, SettingsTheme.Light);
            window.Show();

            window.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsVisible);
            Assert.Equal(GableRoofGeometryDialogAction.None, window.RequestedAction);

            Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
            viewModel.SetRidgeDirection(direction);
            window.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(GableRoofGeometryDialogAction.Apply, window.RequestedAction);
            Assert.False(window.IsClosed);
            window.Close();

            var cancelWindow = CreateOffscreenWindow(
                new GableRoofGeometryViewModel(Rectangle(10000d, 6000d)),
                SettingsTheme.Dark);
            cancelWindow.Show();
            cancelWindow.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(cancelWindow.IsVisible);
            Assert.Equal(GableRoofGeometryDialogAction.Cancel, cancelWindow.RequestedAction);
            Assert.False(cancelWindow.IsClosed);
            cancelWindow.Close();
        });
    }

    private static GableRoofGeometryWindow CreateOffscreenWindow(
        GableRoofGeometryViewModel viewModel,
        SettingsTheme theme) =>
        new(viewModel, theme)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static RoofFootprint Rectangle(double length, double width) =>
        Validate([new(0d, 0d), new(length, 0d), new(length, width), new(0d, width)]);

    private static RoofFootprint RotatedRectangle(double length, double width, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var x = (X: Math.Cos(radians), Y: Math.Sin(radians));
        var y = (X: -Math.Sin(radians), Y: Math.Cos(radians));
        return Validate([
            new(250d, -900d),
            new(250d + length * x.X, -900d + length * x.Y),
            new(250d + length * x.X + width * y.X, -900d + length * x.Y + width * y.Y),
            new(250d + width * y.X, -900d + width * y.Y),
        ]);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static double Degrees(double radians) => radians * 180d / Math.PI;

    private static void AssertElementFitsInside(FrameworkElement element, FrameworkElement container)
    {
        var topLeft = element.TransformToAncestor(container).Transform(new Point(0d, 0d));
        Assert.True(topLeft.Y >= 0d, $"{element.Name} starts above its container.");
        Assert.True(
            topLeft.Y + element.ActualHeight <= container.ActualHeight + 0.5d,
            $"{element.Name} is clipped below its container: " +
            $"bottom={topLeft.Y + element.ActualHeight:0.##}, height={container.ActualHeight:0.##}.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Gable roof dialog test timed out.");
        Assert.Null(failure);
    }
}
