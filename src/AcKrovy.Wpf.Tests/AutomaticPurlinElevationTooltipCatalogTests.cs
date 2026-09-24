using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinElevationTooltipCatalogTests
{
    [Fact]
    public void Catalog_MapsAllTwelveRoleMetricCombinationsToDistinctFiles()
    {
        var files = AutomaticPurlinElevationTooltipCatalog.AllEntries
            .Select(entry => entry.FileName)
            .ToArray();
        Assert.Equal(12, files.Length);
        Assert.Equal(12, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(
            AutomaticPurlinElevationTooltipCatalog.AllEntries,
            entry =>
            {
                var name = AutomaticPurlinElevationTooltipCatalog.GetFileName(
                    entry.Role,
                    entry.Metric);
                Assert.Equal(entry.FileName, name);
                Assert.StartsWith(
                    "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/RoofPurlins/Tooltips/",
                    AutomaticPurlinElevationTooltipCatalog.GetPackUriString(entry.Role, entry.Metric),
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Catalog_EmbeddedResourcesResolveAsBitmapImages()
    {
        RunSta(() =>
        {
            _ = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            Assert.All(
                AutomaticPurlinElevationTooltipCatalog.AllEntries,
                entry =>
                {
                    Assert.True(
                        AutomaticPurlinElevationTooltipCatalog.TryCreateBitmapImage(
                            entry.Role,
                            entry.Metric,
                            out var image),
                        entry.FileName);
                    Assert.NotNull(image);
                    Assert.True(image!.PixelWidth > 0, entry.FileName);
                    Assert.True(image.PixelHeight > 0, entry.FileName);
                });
        });
    }

    [Fact]
    public void Factory_UsesLiveFormattedValueNotStaticImageText()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        string Text(string key) => UiStrings.GetString(key, culture);
        var tip = AutomaticPurlinElevationTooltipFactory.Create(
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            AutomaticPurlinElevationMetric.RoofPlane,
            "+0,206",
            Text);
        Assert.Equal(Text("AutomaticPurlin_RoofPlaneElevation"), tip.Title);
        Assert.Contains("+0,206 m", tip.CurrentValueLine, StringComparison.Ordinal);
        Assert.StartsWith("Aktuálna hodnota:", tip.CurrentValueLine, StringComparison.Ordinal);

        var tip2 = AutomaticPurlinElevationTooltipFactory.Create(
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            AutomaticPurlinElevationMetric.RoofPlane,
            "±0,000",
            Text);
        Assert.Contains("±0,000 m", tip2.CurrentValueLine, StringComparison.Ordinal);
        Assert.NotEqual(tip.CurrentValueLine, tip2.CurrentValueLine);
    }

    [Fact]
    public void Factory_RidgeRoofPlaneUsesRidgeSpecificDescription()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        string Text(string key) => UiStrings.GetString(key, culture);
        var tip = AutomaticPurlinElevationTooltipFactory.Create(
            RoofAutomaticPurlinGeneratorRole.Ridge,
            AutomaticPurlinElevationMetric.RoofPlane,
            "+1,234",
            Text);
        Assert.Equal(
            Text("AutomaticPurlin_Tooltip_RoofPlaneRidgeDescription"),
            tip.Description);
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
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
