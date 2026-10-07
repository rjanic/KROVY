using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AcKrovy.AutoCAD.UI;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class RoofIndependentOrdinaryDetachWindowTests
{
    [Fact]
    public void Confirmation_UsesExactSlovakTextAndExplicitYesNoActions()
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
                var window = new RoofIndependentOrdinaryDetachWindow();
                var content = Assert.IsType<StackPanel>(window.Content);
                var information = Assert.IsType<Border>(content.Children[0]);
                var explanation = Assert.IsType<TextBlock>(information.Child);
                Assert.DoesNotContain("RoofOrdinary", explanation.Text);
                Assert.Contains("?", explanation.Text);
                var preference = Assert.IsType<CheckBox>(content.Children[1]);
                Assert.False(window.DoNotShowAgain);
                Assert.DoesNotContain("Warning_", (string)preference.Content);
                preference.IsChecked = true;
                Assert.True(window.DoNotShowAgain);
                Assert.NotNull(preference.Style);
                var actionSection = Assert.IsType<Border>(content.Children[2]);
                var actions = Assert.IsType<StackPanel>(actionSection.Child);
                var yes = Assert.IsType<Button>(actions.Children[0]);
                var no = Assert.IsType<Button>(actions.Children[1]);
                Assert.NotEqual("RoofOrdinaryDetachYes", yes.Content);
                Assert.NotEqual("RoofOrdinaryDetachNo", no.Content);
                Assert.True(yes.IsDefault);
                Assert.True(no.IsCancel);
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
