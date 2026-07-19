using System.Windows;

namespace AcKrovy.AutoCAD.UI;

public partial class InspectInfoWindow : Window
{
    public InspectInfoWindow(IReadOnlyList<InspectInfoRow> rows)
    {
        InitializeComponent();
        DataContext = new InspectInfoViewModel(rows);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed record InspectInfoRow(string Label, string Value);

internal sealed record InspectInfoViewModel(IReadOnlyList<InspectInfoRow> Rows);
