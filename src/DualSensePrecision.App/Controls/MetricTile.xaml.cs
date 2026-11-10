using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DualSensePrecision.App.Controls;

/// <summary>Reusable "label / big number / unit / hint" tile used across Home and Performance.</summary>
public sealed partial class MetricTile : UserControl
{
    public MetricTile() => InitializeComponent();

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label));
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), "—");
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit));
    public static readonly DependencyProperty HintProperty = Register(nameof(Hint));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public static readonly DependencyProperty ValueFontSizeProperty =
        DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(MetricTile), new PropertyMetadata(26.0));
    public double ValueFontSize { get => (double)GetValue(ValueFontSizeProperty); set => SetValue(ValueFontSizeProperty, value); }

    private static DependencyProperty Register(string name, string defaultValue = "") =>
        DependencyProperty.Register(name, typeof(string), typeof(MetricTile), new PropertyMetadata(defaultValue));
}
