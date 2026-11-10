using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace DualSensePrecision.App.Views;

public sealed partial class PerformancePage : Page
{
    private const int GraphSamples = 500;
    private readonly float[] _recent = new float[GraphSamples];
    private UiSampler? _sampler;
    private int _tick;

    public PerformanceViewModel ViewModel { get; } = new();

    public PerformancePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(33));
        _sampler.Tick += OnTick;
        _sampler.Start();
        ViewModel.Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _sampler?.Stop();
        _sampler = null;
    }

    private void OnTick()
    {
        // Statistics at ~10 Hz (sorting 4k samples), graph at ~30 Hz.
        if (++_tick % 3 == 0) ViewModel.Refresh();
        DrawGraph();
    }

    private void OnGraphSizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

    private void DrawGraph()
    {
        var c = AppServices.Controller;
        var t = c.Telemetry;
        double w = GraphHost.ActualWidth, h = GraphHost.ActualHeight;
        var points = new PointCollection();
        if (t == null || c.Status != ConnectionStatus.Connected || w < 10)
        {
            IntervalLine.Points = points;
            MedianLine.Visibility = Visibility.Collapsed;
            GraphScale.Text = "";
            return;
        }

        int n = t.CopyRecentIntervals(_recent);
        double median = ViewModel.Last.P50IntervalUs;
        double top = Math.Max(median * 2.5, ViewModel.Last.P99IntervalUs * 1.2);
        if (top <= 0) top = 10_000;

        for (int i = 0; i < n; i++)
        {
            double x = w * i / Math.Max(1, GraphSamples - 1);
            double y = h - Math.Min(_recent[i] / top, 1.0) * (h - 8) - 4;
            points.Add(new Point(x, y));
        }
        IntervalLine.Points = points;

        if (median > 0)
        {
            double my = h - median / top * (h - 8) - 4;
            MedianLine.X1 = 0; MedianLine.X2 = w; MedianLine.Y1 = my; MedianLine.Y2 = my;
            MedianLine.Visibility = Visibility.Visible;
        }
        GraphScale.Text = $"0 – {top / 1000:0.0} ms · dashed = median";
    }
}
