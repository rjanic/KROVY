using System.Windows;
using System.Windows.Controls;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Presentation-only selector for WallPlate / Ridge / Intermediate editor templates.
/// </summary>
internal sealed class AutomaticPurlinEditorTabContentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? WallPlateTemplate { get; set; }
    public DataTemplate? RidgeTemplate { get; set; }
    public DataTemplate? IntermediateTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not AutomaticPurlinEditorTabViewModel tab)
        {
            return base.SelectTemplate(item, container);
        }

        return tab.Kind switch
        {
            AutomaticPurlinEditorTabKind.WallPlate => WallPlateTemplate,
            AutomaticPurlinEditorTabKind.Ridge => RidgeTemplate,
            AutomaticPurlinEditorTabKind.Intermediate => IntermediateTemplate,
            _ => base.SelectTemplate(item, container),
        };
    }
}
