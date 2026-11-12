using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DualSensePrecision.App.Controls;

/// <summary>Raw pull vs processed output, with deadzone / activation / full-press / max markers on the raw bar.</summary>
public sealed partial class TriggerMeter : UserControl
{
    private TriggerSettings? _settings;
    private double _raw = -1, _out;

    public TriggerMeter() => InitializeComponent();

    public void SetMarkers(TriggerSettings s) { _settings = s; DrawMarkers(); }

    /// <summary>raw/out in 0..1, or raw &lt; 0 for "no controller".</summary>
    public void SetValues(double raw, double output)
    {
        _raw = raw; _out = output;
        double w = RawTrack.ActualWidth;
        if (raw < 0)
        {
            RawFill.Width = OutFill.Width = 0;
            RawText.Text = OutText.Text = "—";
            return;
        }
        RawFill.Width = Math.Clamp(raw, 0, 1) * w;
        OutFill.Width = Math.Clamp(output, 0, 1) * w;
        RawText.Text = $"{raw * 100,3:0}%";
        OutText.Text = $"{output * 100,3:0}%";
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawMarkers();
        SetValues(_raw, _out);
    }

    private void DrawMarkers()
    {
        Markers.Children.Clear();
        var s = _settings;
        double w = RawTrack.ActualWidth, h = RawTrack.ActualHeight;
        if (s == null || w < 10) return;

        Brush B(string k) => (Brush)Application.Current.Resources[k];

        if (s.Mode != TriggerMode.Digital)
        {
            // Deadzone: shaded start of the track
            var dz = new Rectangle { Width = Math.Clamp(s.Deadzone, 0, 1) * w, Height = h, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0x7A, 0x6B)), RadiusX = 4, RadiusY = 4 };
            Markers.Children.Add(dz);
            Mark(s.MaxPoint, B("DsAccentBrush"));
        }
        Mark(s.ActivationPoint, B("DsSuccessBrush"));
        if (s.Mode == TriggerMode.DualZone) Mark(s.SecondStagePoint, B("DsWarningBrush"));

        void Mark(double at, Brush brush)
        {
            double x = Math.Clamp(at, 0, 1) * w;
            Markers.Children.Add(new Line { X1 = x, X2 = x, Y1 = -3, Y2 = h + 3, Stroke = brush, StrokeThickness = 2 });
        }
    }
}
