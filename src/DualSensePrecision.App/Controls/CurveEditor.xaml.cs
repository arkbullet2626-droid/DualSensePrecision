using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DualSensePrecision.App.Controls;

/// <summary>
/// Interactive response-curve editor. Drag interior points; double-click to add; right-click to remove.
/// Endpoints (0,0) and (1,1) are fixed. Points stay ordered and the curve stays monotone (never goes backwards).
/// Shows the live stick input as a green marker riding the curve.
/// </summary>
public sealed partial class CurveEditor : UserControl
{
    private const double Pad = 18;
    private const double HitRadius = 14;
    private const double MinGap = 0.03;
    private const int MaxPoints = 10;

    private readonly List<CurvePoint> _points = new() { new(0, 0), new(1, 1) };
    private int _drag = -1;
    private double _inputX = -1, _inputY;

    /// <summary>Raised while the user edits. The list is a copy.</summary>
    public event Action<IReadOnlyList<CurvePoint>>? PointsEdited;

    public CurveEditor() => InitializeComponent();

    public void SetPoints(IEnumerable<CurvePoint> points)
    {
        _drag = -1;
        _points.Clear();
        // Route through ResponseCurve so we display exactly what the engine will use.
        _points.AddRange(new ResponseCurve(points).Points);
        Redraw();
    }

    public void SetInput(double x, double y) { _inputX = x; _inputY = y; UpdateMarker(); }
    public void ClearInput() { if (_inputX < 0) return; _inputX = -1; UpdateMarker(); }

    private double PlotW => Math.Max(0, Root.ActualWidth - 2 * Pad);
    private double PlotH => Math.Max(0, Root.ActualHeight - 2 * Pad);
    private Point ToScreen(double x, double y) => new(Pad + x * PlotW, Pad + (1 - y) * PlotH);
    private (double x, double y) FromScreen(Point p) =>
        (Math.Clamp((p.X - Pad) / Math.Max(PlotW, 1), 0, 1), Math.Clamp(1 - (p.Y - Pad) / Math.Max(PlotH, 1), 0, 1));

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (PlotW < 10 || PlotH < 10) return;

        GridLayer.Children.Clear();
        var gridBrush = Res("DsStrokeBrush");
        for (int i = 0; i <= 4; i++)
        {
            double f = i / 4.0;
            var a = ToScreen(f, 0); var b = ToScreen(f, 1);
            GridLayer.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = gridBrush, StrokeThickness = 1 });
            a = ToScreen(0, f); b = ToScreen(1, f);
            GridLayer.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = gridBrush, StrokeThickness = 1 });
        }

        Diagonal.Points = new PointCollection { ToScreen(0, 0), ToScreen(1, 1) };

        var curve = new ResponseCurve(_points);
        var line = new PointCollection();
        for (int i = 0; i <= 160; i++)
        {
            double x = i / 160.0;
            line.Add(ToScreen(x, Math.Clamp(curve.EvaluateExact(x), 0, 1)));
        }
        CurveLine.Points = line;

        HandleLayer.Children.Clear();
        for (int i = 0; i < _points.Count; i++)
        {
            bool fixedPoint = i == 0 || i == _points.Count - 1;
            bool active = i == _drag;
            double size = active ? 16 : fixedPoint ? 8 : 13;
            var p = ToScreen(_points[i].X, _points[i].Y);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = fixedPoint ? Res("DsTextTertiaryBrush") : Res("DsAccentBrush"),
                Stroke = fixedPoint ? null : Res("DsTextPrimaryBrush"),
                StrokeThickness = active ? 2.5 : 1.5,
            };
            Canvas.SetLeft(dot, p.X - size / 2);
            Canvas.SetTop(dot, p.Y - size / 2);
            HandleLayer.Children.Add(dot);
        }
        UpdateMarker();
    }

    private void UpdateMarker()
    {
        if (_inputX < 0 || PlotW < 10)
        {
            InputLine.Visibility = InputDot.Visibility = Visibility.Collapsed;
            return;
        }
        var p = ToScreen(_inputX, _inputY);
        var bottom = ToScreen(_inputX, 0);
        InputLine.X1 = bottom.X; InputLine.Y1 = bottom.Y; InputLine.X2 = p.X; InputLine.Y2 = p.Y;
        Canvas.SetLeft(InputDot, p.X - 5);
        Canvas.SetTop(InputDot, p.Y - 5);
        InputLine.Visibility = InputDot.Visibility = Visibility.Visible;
    }

    private int HitTest(Point pos)
    {
        int best = -1;
        double bestDist = HitRadius;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = ToScreen(_points[i].X, _points[i].Y);
            double d = Math.Sqrt((p.X - pos.X) * (p.X - pos.X) + (p.Y - pos.Y) * (p.Y - pos.Y));
            if (d <= bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    private bool IsInterior(int i) => i > 0 && i < _points.Count - 1;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Root);
        if (!pt.Properties.IsLeftButtonPressed) return;
        int i = HitTest(pt.Position);
        if (!IsInterior(i)) return;
        _drag = i;
        Root.CapturePointer(e.Pointer);
        Redraw();
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag < 0) return;
        var (x, y) = FromScreen(e.GetCurrentPoint(Root).Position);
        var prev = _points[_drag - 1];
        var next = _points[_drag + 1];

        double minX = prev.X + MinGap, maxX = next.X - MinGap;
        x = minX <= maxX ? Math.Clamp(x, minX, maxX) : _points[_drag].X;
        y = Math.Clamp(y, prev.Y, next.Y); // keeps the curve monotone

        _points[_drag] = new CurvePoint(x, y);
        Redraw();
        PointsEdited?.Invoke(_points.ToList());
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        Root.ReleasePointerCapture(e.Pointer);
        Redraw();
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        Redraw();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var pos = e.GetPosition(Root);
        if (HitTest(pos) >= 0 || _points.Count >= MaxPoints) return;
        var (x, _) = FromScreen(pos);

        int insertAt = _points.FindIndex(p => p.X > x);
        if (insertAt <= 0) return;
        if (x - _points[insertAt - 1].X < MinGap || _points[insertAt].X - x < MinGap) return;

        // New point lands on the current curve, so adding a point never changes the shape by itself.
        double y = Math.Clamp(new ResponseCurve(_points).EvaluateExact(x), _points[insertAt - 1].Y, _points[insertAt].Y);
        _points.Insert(insertAt, new CurvePoint(x, y));
        Redraw();
        PointsEdited?.Invoke(_points.ToList());
        e.Handled = true;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        int i = HitTest(e.GetPosition(Root));
        if (!IsInterior(i)) return;
        _points.RemoveAt(i);
        Redraw();
        PointsEdited?.Invoke(_points.ToList());
        e.Handled = true;
    }
}
