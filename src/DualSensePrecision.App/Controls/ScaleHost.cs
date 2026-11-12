using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DualSensePrecision.App.Controls;

/// <summary>
/// Scales its content like a zoom level: the child lays out in (size ÷ scale) and is drawn × scale, so at 125 %
/// everything is 25 % bigger and still fills the window exactly (unlike a plain RenderTransform, which would
/// overflow or leave gaps). Used for Settings → Interface → Interface scale.
///
/// While the scale slider is being dragged, a visual-only <em>preview</em> zooms the content around a fixed anchor
/// point (the slider) without re-laying anything out, so the slider never moves under the mouse. The real scale is
/// committed when the drag ends, and <see cref="Settle"/> eases away any small difference left by the re-layout.
/// </summary>
public sealed partial class ScaleHost : Panel
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(ScaleHost), new PropertyMetadata(1.0, (d, _) => ((ScaleHost)d).InvalidateMeasure()));

    private readonly CompositeTransform _transform = new();
    private double _preview = 1.0;   // visual-only zoom factor on top of Scale
    private Point _anchor;           // host coordinates that stay put during the preview
    private Point _settle;           // leftover offset after committing, eased to zero

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, Math.Clamp(value, 0.5, 2.0));
    }

    /// <summary>Visual zoom by <paramref name="factor"/> around <paramref name="anchor"/> (host coordinates). No layout pass.</summary>
    public void SetPreview(double factor, Point anchor)
    {
        _preview = factor;
        _anchor = anchor;
        UpdateTransform();
    }

    public void ClearPreview()
    {
        _preview = 1.0;
        UpdateTransform();
    }

    /// <summary>Offsets the content by (dx, dy) screen pixels; call with values easing to 0 for a smooth settle.</summary>
    public void Settle(double dx, double dy)
    {
        _settle = new Point(dx, dy);
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        double s = Scale * _preview;
        _transform.ScaleX = _transform.ScaleY = s;
        // Zoom around the anchor: screen = anchor + k·(base − anchor)  ⇒  translate = anchor·(1 − k)
        _transform.TranslateX = _anchor.X * (1 - _preview) + _settle.X;
        _transform.TranslateY = _anchor.Y * (1 - _preview) + _settle.Y;
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
        UpdateTransform();
        foreach (var child in Children)
        {
            child.RenderTransformOrigin = new Point(0, 0);
            child.RenderTransform = _transform;
            child.Arrange(inner);
        }
        return finalSize;
    }

    private static double Div(double v, double s) => double.IsInfinity(v) ? v : v / s;
}
