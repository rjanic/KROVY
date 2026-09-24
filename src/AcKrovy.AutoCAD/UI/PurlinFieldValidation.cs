using System.Windows;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Attached validation visuals for AK_ROOF_PURLINS editable fields.
/// Domain rules stay in the ViewModel; this only drives presentation.
/// </summary>
internal static class PurlinFieldValidation
{
    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.RegisterAttached(
            "HasError",
            typeof(bool),
            typeof(PurlinFieldValidation),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ErrorTextProperty =
        DependencyProperty.RegisterAttached(
            "ErrorText",
            typeof(string),
            typeof(PurlinFieldValidation),
            new PropertyMetadata(null));

    public static void SetHasError(DependencyObject element, bool value) =>
        element.SetValue(HasErrorProperty, value);

    public static bool GetHasError(DependencyObject element) =>
        (bool)element.GetValue(HasErrorProperty);

    public static void SetErrorText(DependencyObject element, string? value) =>
        element.SetValue(ErrorTextProperty, value);

    public static string? GetErrorText(DependencyObject element) =>
        (string?)element.GetValue(ErrorTextProperty);
}
