using System.Windows;
using System.Windows.Markup;
using WpfBinding = System.Windows.Data.Binding;
using WpfBindingMode = System.Windows.Data.BindingMode;

using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return DependencyProperty.UnsetValue;
        }

        return new WpfBinding
        {
            Path = new PropertyPath($"[{Key}]"),
            Source = UiStringBindingSource.Shared,
            Mode = WpfBindingMode.OneWay,
        }.ProvideValue(serviceProvider);
    }
}
