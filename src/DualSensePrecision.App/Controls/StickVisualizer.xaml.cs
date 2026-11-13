using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace DualSensePrecision.App.Controls;

/// <summary>Live stick view: raw position (grey), processed position (accent), deadzone boundaries.</summary>
public sealed partial class StickVisualizer : UserControl
{
    private const double C = 120, R = 104;

    public StickVisualizer()
    {
        InitializeComponent();
        SetDots(null);
    }

    public void SetZones(StickSettings s)
    {
        double inner = Math.Clamp(s.InnerDeadzone, 0, 0.95);
        double outer = Math.Clamp(s.OuterDeadzone, inner + 0.01, 1.0);
        bool radial = s.DeadzoneShape == DeadzoneShape.Radial;

        Show(InnerZone, radial);
        Show(OuterRing, radial);
        Show(InnerBandV, !radial);
        Show(InnerBandH, !radial);
        Show(OuterSquare, !radial);
        Show(BoundSquare, s.OutputShape == OutputShape.Square);

        if (radial)
        {
            Place(InnerZone, C - inner * R, C - inner * R, 2 * inner * R, 2 * inner * R);
            Place(OuterRing, C - outer * R, C - outer * R, 2 * outer * R, 2 * outer * R);
        }
        else
        {
            Place(InnerBandV, C - inner * R, C - R, 2 * inner * R, 2 * R);
            Place(InnerBandH, C - R, C - inner * R, 2 * R, 2 * inner * R);
            Place(OuterSquare, C - outer * R, C - outer * R, 2 * outer * R, 2 * outer * R);
        }
    }

    /// <summary>Normalised positions, Y up. Pass null to hide the dots (no controller).</summary>
    public void SetDots((float rx, float ry, float px, float py)? v)
    {
        if (v is not { } p)
        {
            RawDot.Visibility = ProcessedDot.Visibility = Link.Visibility = Visibility.Collapsed;
            return;
        }
        RawDot.Visibility = ProcessedDot.Visibility = Link.Visibility = Visibility.Visible;
        double rx = C + p.rx * R, ry = C - p.ry * R;
        double px = C + p.px * R, py = C - p.py * R;
        Canvas.SetLeft(RawDot, rx - 5); Canvas.SetTop(RawDot, ry - 5);
        Canvas.SetLeft(ProcessedDot, px - 7); Canvas.SetTop(ProcessedDot, py - 7);
        Link.X1 = rx; Link.Y1 = ry; Link.X2 = px; Link.Y2 = py;
    }

    private static void Show(UIElement e, bool visible) => e.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Place(Shape s, double left, double top, double w, double h)
    {
        Canvas.SetLeft(s, left);
        Canvas.SetTop(s, top);
        s.Width = Math.Max(0, w);
        s.Height = Math.Max(0, h);
    }
}
