using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DualSensePrecision.App.Controls;

/// <summary>
/// Scales its content like a zoom level: the child lays out in (size ÷ scale) and is drawn × scale, so at 125 %
/// everything is 25 % bigger and still fills the window exactly (unlike a plain RenderTransform, which would
/// overflow or leave gaps). Used for Settings → Interface → Interface scale.
/// </summary>
public sealed partial class ScaleHost : Panel
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(ScaleHost), new PropertyMetadata(1.0, (d, _) => ((ScaleHost)d).InvalidateMeasure()));

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, Math.Clamp(value, 0.5, 2.0));
    }

    protected override Size MeasureOverride(Size available)
    {
        double s = Scale;
        var inner = new Size(Div(available.Width, s), Div(available.Height, s));
        foreach (var child in Children) child.Measure(inner);
        var first = Children.FirstOrDefault();
        return first == null ? new Size(0, 0) : new Size(first.DesiredSize.Width * s, first.DesiredSize.Height * s);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Scale;
        var inner = new Rect(0, 0, finalSize.Width / s, finalSize.Height / s);
        foreach (var child in Children)
        {
            child.RenderTransformOrigin = new Point(0, 0);
            child.RenderTransform = new ScaleTransform { ScaleX = s, ScaleY = s };
            child.Arrange(inner);
        }
        return finalSize;
    }

    private static double Div(double v, double s) => double.IsInfinity(v) ? v : v / s;
}
