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
public sealed class ElementEditElevationTests
{
    [Fact]
    public void ElementEditWindow_ShowsElevationSection_WhenStateIsProvided()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();

            Assert.Equal(Visibility.Visible, window.ElevationSection.Visibility);
            Assert.Equal("+1,000", window.LowerZTextBox.Text);
            Assert.Equal("+3,000", window.UpperZTextBox.Text);
            Assert.Equal(Visibility.Visible, window.SlopeDegreesLabel.Visibility);
            Assert.Equal(Visibility.Visible, window.SlopeTextBox.Visibility);
            Assert.True(window.SlopeElevationTextBox.IsReadOnly);
            window.Close();
        });
    }

    [Fact]
    public void ElementEditWindow_HidesElevationSection_WhenNoStateProvided()
    {
        RunSta(() =>
        {
            var window = new ElementEditWindow(new TimberElementData(), isNewAssignment: false)
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
        });
    }

    [Fact]
    public void ElementEditWindow_HidesElevationSection_WhenMultipleSelection()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var a = CreateRafterSeed(45);
            var b = CreateRafterSeed(45);
            var elevation = StructuralMemberElevationRules.CreateSloped(1000, 3000);
            var window = new ElementEditWindow(
                a,
                isNewAssignment: false,
                validationData: new[] { a, b },
                elevationState: elevation,
                planLengthMm: 4000)
            {
                Left = -30000,
                Top = -30000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, window.ElevationSection.Visibility);
            Assert.Null(window.ElevationViewModel);
            window.Close();
        });
    }

    [Fact]
    public void ModeA_LowerUpper_MakesSlopeReadOnly_OnMainAndLinked()
    {
        RunSta(() =>
        {
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();
            var vm = window.ElevationViewModel!;
            Assert.True(vm.Mode_LowerUpper);
            Assert.True(vm.LowerZEditable);
            Assert.True(vm.UpperZEditable);
            Assert.False(vm.SlopeEditable);
            Assert.False(window.LowerZTextBox.IsReadOnly);
            Assert.False(window.UpperZTextBox.IsReadOnly);
            Assert.True(window.SlopeElevationTextBox.IsReadOnly);
            Assert.True(window.SlopeTextBox.IsReadOnly);
            window.Close();
        });
    }

    [Fact]
    public void ModeB_LowerSlope_MakesUpperZReadOnly_MainSlopeEditable()
    {
        RunSta(() =>
        {
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();
            var vm = window.ElevationViewModel!;
            vm.Mode_LowerSlope = true;
            Assert.True(vm.LowerZEditable);
            Assert.False(vm.UpperZEditable);
            Assert.True(vm.SlopeEditable);
            Assert.False(window.LowerZTextBox.IsReadOnly);
            Assert.True(window.UpperZTextBox.IsReadOnly);
            Assert.True(window.SlopeElevationTextBox.IsReadOnly); // linked mirror
            Assert.False(window.SlopeTextBox.IsReadOnly); // main parameter
            window.Close();
        });
    }

    [Fact]
    public void ModeC_UpperSlope_MakesLowerZReadOnly_MainSlopeEditable()
    {
        RunSta(() =>
        {
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();
            var vm = window.ElevationViewModel!;
            vm.Mode_UpperSlope = true;
            Assert.False(vm.LowerZEditable);
            Assert.True(vm.UpperZEditable);
            Assert.True(vm.SlopeEditable);
            Assert.True(window.LowerZTextBox.IsReadOnly);
            Assert.False(window.UpperZTextBox.IsReadOnly);
            Assert.True(window.SlopeElevationTextBox.IsReadOnly);
            Assert.False(window.SlopeTextBox.IsReadOnly);
            window.Close();
        });
    }

    [Fact]
    public void ReferenceAndCalculation_ImageChoices_AreThreeEach_WithSelectedHighlight()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();
            var vm = window.ElevationViewModel!;

            Assert.Equal(3, vm.ReferenceChoices.Count);
            Assert.Equal(new[] { "SH", "OS", "VH" }, vm.ReferenceChoices.Select(c => c.Title).ToArray());
            Assert.Equal(3, vm.CalculationChoices.Count);
            Assert.Equal(64d, ElevationImageChoiceCatalog.RecommendedImageSizePx);

            Assert.Single(vm.ReferenceChoices, c => c.IsSelected);
            Assert.True(vm.ReferenceChoices.Single(c => c.Title == "OS").IsSelected);
            Assert.Single(vm.CalculationChoices, c => c.IsSelected);
            Assert.True(vm.CalculationChoices[0].IsSelected);

            vm.SelectReferenceChoice(vm.ReferenceChoices.Single(c => c.Title == "SH"));
            Assert.True(vm.ReferenceChoices.Single(c => c.Title == "SH").IsSelected);
            Assert.False(vm.ReferenceChoices.Single(c => c.Title == "OS").IsSelected);

            vm.SelectCalculationChoice(vm.CalculationChoices[1]);
            Assert.True(vm.CalculationChoices[1].IsSelected);
            Assert.False(vm.CalculationChoices[0].IsSelected);

            Assert.Equal(3, window.ReferenceChoicesList.Items.Count);
            Assert.Equal(3, window.CalculationChoicesList.Items.Count);
            Assert.All(vm.ReferenceChoices, c => Assert.False(string.IsNullOrWhiteSpace(c.ImageKey)));
            Assert.All(vm.CalculationChoices, c => Assert.False(string.IsNullOrWhiteSpace(c.ImageKey)));
            window.Close();
        });
    }

    [Fact]
    public void ElevationSlope_SharesUnderlyingOrdinarySlope_NotIndependentDuplicate()
    {
        RunSta(() =>
        {
            var window = CreateWindowWithElevation(startMm: 0, endMm: 4000, planMm: 4000);
            window.Show();
            window.UpdateLayout();
            var vm = window.ElevationViewModel!;
            Assert.Equal(45d, vm.AbsoluteSlopeDegrees, 3);
            Assert.Equal("45", window.SlopeTextBox.Text);
            Assert.Contains("45", vm.SlopeText);
            Assert.True(window.SlopeElevationTextBox.IsReadOnly);

            vm.Mode_LowerSlope = true;
            Assert.False(window.SlopeTextBox.IsReadOnly);
            vm.SlopeText = "-30,00°";
            Assert.Equal(30d, vm.AbsoluteSlopeDegrees, 3);
            Assert.Equal("30", window.SlopeTextBox.Text);
            Assert.Contains("30", vm.SlopeText);
            window.Close();
        });
    }

    [Fact]
    public void SlovakLabels_UseHornéhoAndLinkedSlope()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            Assert.Equal("Z horného bodu", UiStrings.RoofOrdinaryElevationUpperZ);
            Assert.Equal("Previazaný sklon", UiStrings.RoofOrdinaryElevationSlope);
            Assert.Contains("horn", UiStrings.RoofOrdinaryElevationModeLowerUpper, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("vrchn", UiStrings.RoofOrdinaryElevationUpperZ, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Hodnoty", UiStrings.RoofOrdinaryElevationValues);
            Assert.Equal("Z spodného bodu", UiStrings.RoofOrdinaryElevationLowerZ);
        });
    }


    [Fact]
    public void ElementEditWindow_UsesCompactWoodChrome_WithoutScrollViewer()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var window = CreateWindowWithElevation();
            window.Show();
            window.UpdateLayout();

            Assert.NotNull(window.HeaderBand);
            Assert.Equal(54d, window.HeaderBand.Height);
            Assert.NotNull(window.FooterBand);
            Assert.True(window.FooterBand.MinHeight <= 51d);
            Assert.Equal(Visibility.Visible, window.ElevationSection.Visibility);

            // Dialog content itself must not be scroll-hosted (ComboBox templates still embed ScrollViewers).
            Assert.IsType<System.Windows.Controls.Grid>(window.Content);
            Assert.DoesNotContain(
                FindVisualChildrenOfType<System.Windows.Controls.ScrollViewer>(window),
                sv => IsAncestorOf(sv, window.ElevationSection) ||
                      IsAncestorOf(sv, window.HeaderBand) ||
                      IsAncestorOf(sv, window.FooterBand));

            // Single SH/OS/VH card row under Referencia — three reference + three calculation choices.
            Assert.Equal(3, window.ReferenceChoicesList.Items.Count);
            Assert.Equal(3, window.CalculationChoicesList.Items.Count);
            Assert.Equal(3, window.ElevationViewModel!.ReferenceChoices.Count);

            window.Close();
        });
    }


    private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject? descendant)
    {
        for (var current = descendant; current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }
    private static System.Collections.Generic.List<T> FindVisualChildrenOfType<T>(DependencyObject root)
        where T : DependencyObject
    {
        var results = new System.Collections.Generic.List<T>();
        if (root is null) return results;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) results.Add(match);
            results.AddRange(FindVisualChildrenOfType<T>(child));
        }
        return results;
    }
    private static ElementEditWindow CreateWindowWithElevation(
        double startMm = 1000,
        double endMm = 3000,
        double planMm = 4000)
    {
        var seed = CreateRafterSeed(Math.Abs(
            StructuralMemberElevationRules.DeriveSlopeDegrees(startMm, endMm, planMm)));
        var elevation = StructuralMemberElevationRules.CreateSloped(startMm, endMm);
        return new ElementEditWindow(
            seed,
            isNewAssignment: false,
            elevationState: elevation,
            planLengthMm: planMm)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };
    }

    private static TimberElementData CreateRafterSeed(double slopeDegrees) => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = slopeDegrees,
    };

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
