using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AcKrovy.AutoCAD.UI;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class RoofPhysical3DWarningWindowTests
{
    [Fact]
    public void Warning_UsesLocalizedBodyAndSingleAcknowledgementAction()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var window = new RoofPhysical3DWarningWindow();
                var content = Assert.IsType<StackPanel>(window.Content);
                var information = Assert.IsType<Border>(content.Children[0]);
                var body = Assert.IsType<TextBlock>(information.Child);
                Assert.DoesNotContain("RoofOrdinary", body.Text);
                Assert.Contains("3D", body.Text);
                Assert.Contains("2D", body.Text);
                var preference = Assert.IsType<CheckBox>(content.Children[1]);
                Assert.False(window.DoNotShowAgain);
                preference.IsChecked = true;
                Assert.True(window.DoNotShowAgain);
                Assert.DoesNotContain("Warning_", (string)preference.Content);
                Assert.NotNull(preference.Style);
                var actionSection = Assert.IsType<Border>(content.Children[2]);
                var actions = Assert.IsType<StackPanel>(actionSection.Child);
                var ok = Assert.IsType<Button>(actions.Children[0]);
                Assert.Equal("OK", ok.Content);
                Assert.True(ok.IsDefault);
                Assert.True(ok.IsCancel);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }
}
