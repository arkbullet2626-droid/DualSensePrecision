using Microsoft.UI.Xaml;

namespace DualSensePrecision.App;

/// <summary>x:Bind function converters (compile-time checked, no IValueConverter boilerplate).</summary>
public static class Converters
{
    public static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
