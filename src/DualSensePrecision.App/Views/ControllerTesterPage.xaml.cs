using System.Diagnostics;
using DualSensePrecision.App.Services;
using DualSensePrecision.App.ViewModels;
using DualSensePrecision.Core;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.InputEngine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DualSensePrecision.App.Views;

/// <summary>
/// Diagnostics on raw hardware values. Analyzers are fed on the HID reader thread (every report, ~250 Hz)
/// so drift and range are measured precisely; the UI only samples them for display.
/// </summary>
public sealed partial class ControllerTesterPage : Page
{
    private enum Step { None, Buttons, Rest, Circles, Triggers, Touch, Done }

    // Analyzers (thread-safe; written on the reader thread)
    private readonly ButtonCoverage _buttons = new();
    private readonly RangeAnalyzer _leftRange = new(), _rightRange = new();
    private readonly DriftAnalyzer _leftDrift = new(), _rightDrift = new();
    private readonly TriggerAnalyzer _l2 = new(), _r2 = new();
    private readonly MotionAnalyzer _motion = new();
    private volatile bool _touchSeen, _twoTouchSeen;
    private long _driftUntil, _motionUntil; // Stopwatch ticks; 0 = not measuring

    // UI state
    private readonly Dictionary<DsButtons, Border> _chips = new();
    private readonly StickView _leftView, _rightView;
    private readonly List<Point>[] _touchTrails = { new(), new() };
    private readonly Polyline[] _touchLines = new Polyline[2];
    private readonly Ellipse[] _touchDots = new Ellipse[2];
    private UiSampler? _sampler;
    private Step _step = Step.None;
    private long _stepStarted;
    private DriftResult _lastLeftDrift = DriftResult.Empty, _lastRightDrift = DriftResult.Empty;
    private MotionResult _lastMotion = MotionResult.Empty;
    private bool _xbox; // labels and test set currently shown for an Xbox controller

    public ControllerTesterPage()
    {
        InitializeComponent();
        _leftView = new StickView(LeftCanvas);
        _rightView = new StickView(RightCanvas);
        BuildButtonGrid();
        BuildTouch();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ApplyFamily(ButtonNames.IsXbox);
        AppServices.Controller.RawObserved += OnRaw;
        _sampler = new UiSampler(DispatcherQueue, TimeSpan.FromMilliseconds(33));
        _sampler.Tick += OnTick;
        _sampler.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppServices.Controller.RawObserved -= OnRaw;
        _sampler?.Stop();
        _sampler = null;
    }

    private static Brush Res(string k) => (Brush)Application.Current.Resources[k];
    private static long Now => Stopwatch.GetTimestamp();
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    // ---------------- reader thread ----------------

    private void OnRaw(ControllerState s)
    {
        var buttons = s.Buttons;
        if (s.L2 > 200) buttons |= DsButtons.L2;
        if (s.R2 > 200) buttons |= DsButtons.R2;
        _buttons.Add(buttons);

        float lx = s.LX, ly = s.LY;
        float rx = s.RX, ry = s.RY;
        _leftRange.Add(lx, ly);
        _rightRange.Add(rx, ry);
        _l2.Add(s.L2);
        _r2.Add(s.R2);
        if (s.Touch0.Active || s.Touch1.Active) _touchSeen = true;
        if (s.Touch0.Active && s.Touch1.Active) _twoTouchSeen = true;

        long now = Now;
        if (now < Volatile.Read(ref _driftUntil) && now >= Volatile.Read(ref _driftGraceUntil))
        {
            _leftDrift.Add(lx, ly);
            _rightDrift.Add(rx, ry);
        }
        if (now < Volatile.Read(ref _motionUntil))
        {
            var cal = AppServices.Controller.Calibration;
            _motion.Add(cal.GyroDegPerSec(0, s.GyroPitch), cal.GyroDegPerSec(1, s.GyroYaw), cal.GyroDegPerSec(2, s.GyroRoll),
                        cal.AccelG(0, s.AccelX), cal.AccelG(1, s.AccelY), cal.AccelG(2, s.AccelZ));
        }
    }

    // ---------------- UI tick ----------------

    private void OnTick()
    {
        var c = AppServices.Controller;
        bool connected = c.Status == ConnectionStatus.Connected;
        NoControllerBar.IsOpen = !connected;
        if (!connected) return;
        if (ButtonNames.IsXbox != _xbox) ApplyFamily(ButtonNames.IsXbox);

        c.Pipeline.Sample(out var s, out _);

        // Buttons
        var seen = _buttons.Seen;
        var now = s.Buttons | (s.L2 > 200 ? DsButtons.L2 : 0) | (s.R2 > 200 ? DsButtons.R2 : 0);
        foreach (var (b, chip) in _chips)
        {
            chip.Background = (now & b) != 0 ? Res("DsAccentBrush") : Res("DsSurfaceRaisedBrush");
            chip.BorderBrush = (seen & b) != 0 ? Res("DsSuccessBrush") : Res("DsStrokeBrush");
        }
        var expected = ExpectedButtons();
        ButtonsCount.Text = $"{expected.Count - _buttons.Missing(expected).Count} / {expected.Count} working";

        // Sticks
        float lx = s.LX, ly = s.LY;
        float rx = s.RX, ry = s.RY;
        _leftView.Update(lx, ly, _leftRange.Maxima());
        _rightView.Update(rx, ry, _rightRange.Maxima());
        LeftLive.Text = $"X {lx,7:+0.000;-0.000; 0.000}  Y {ly,7:+0.000;-0.000; 0.000}  ({Math.Sqrt(lx * lx + ly * ly) * 100,3:0}%)";
        RightLive.Text = $"X {rx,7:+0.000;-0.000; 0.000}  Y {ry,7:+0.000;-0.000; 0.000}  ({Math.Sqrt(rx * rx + ry * ry) * 100,3:0}%)";
        LeftRange.Text = RangeText(_leftRange.Result());
        RightRange.Text = RangeText(_rightRange.Result());

        long driftEnd = Volatile.Read(ref _driftUntil);
        if (driftEnd != 0)
        {
            if (Now < driftEnd)
            {
                double left = (driftEnd - Now) / (double)Stopwatch.Frequency;
                LeftDrift.Text = RightDrift.Text = $"Measuring drift… hands off the sticks ({left:0.0} s)";
            }
            else
            {
                Volatile.Write(ref _driftUntil, 0);
                _lastLeftDrift = _leftDrift.Result();
                _lastRightDrift = _rightDrift.Result();
                DriftButton.IsEnabled = true;
            }
        }
        if (Volatile.Read(ref _driftUntil) == 0)
        {
            LeftDrift.Text = DriftText(_lastLeftDrift);
            RightDrift.Text = DriftText(_lastRightDrift);
        }

        // Triggers
        SetBar(L2Track, L2Fill, s.L2);
        SetBar(R2Track, R2Fill, s.R2);
        L2Text.Text = TriggerText(s.L2, _l2.Result());
        R2Text.Text = TriggerText(s.R2, _r2.Result());

        if (_xbox)
        {
            if (_step is not (Step.None or Step.Done)) AdvanceWizard(s);
            return; // no touchpad or motion sensors
        }

        // Touchpad
        UpdateTouch(s);

        // Motion
        var cal = c.Calibration;
        GyroText.Text = $"Gyro  °/s   pitch {cal.GyroDegPerSec(0, s.GyroPitch),8:0.0}   yaw {cal.GyroDegPerSec(1, s.GyroYaw),8:0.0}   roll {cal.GyroDegPerSec(2, s.GyroRoll),8:0.0}";
        double ax = cal.AccelG(0, s.AccelX), ay = cal.AccelG(1, s.AccelY), az = cal.AccelG(2, s.AccelZ);
        AccelText.Text = $"Accel g     X {ax,8:0.00}   Y {ay,8:0.00}   Z {az,8:0.00}   |g| {Math.Sqrt(ax * ax + ay * ay + az * az),5:0.00}";
        long motionEnd = Volatile.Read(ref _motionUntil);
        if (motionEnd != 0)
        {
            if (Now < motionEnd) MotionResultText.Text = "Measuring… keep the controller still.";
            else
            {
                Volatile.Write(ref _motionUntil, 0);
                _lastMotion = _motion.Result();
                MotionButton.IsEnabled = true;
            }
        }
        if (Volatile.Read(ref _motionUntil) == 0) MotionResultText.Text = MotionText(_lastMotion);

        // Wizard
        if (_step is not (Step.None or Step.Done)) AdvanceWizard(s);
    }

    // ---------------- text helpers ----------------

    private static string DriftText(DriftResult d) => d.Verdict switch
    {
        Verdict.NotTested => "Drift: not tested yet.",
        Verdict.Good => $"Drift: good — wanders {d.MaxDeviation * 100:0.0}% at rest. Suggested deadzone {d.SuggestedDeadzone * 100:0}%.",
        Verdict.Warning => $"Drift: slight — wanders {d.MaxDeviation * 100:0.0}% at rest. Use at least a {d.SuggestedDeadzone * 100:0}% inner deadzone.",
        _ => $"Drift: noticeable — wanders {d.MaxDeviation * 100:0.0}% at rest. Use a {d.SuggestedDeadzone * 100:0}% inner deadzone; the stick may need cleaning or repair.",
    };

    private static string RangeText(RangeResult r) => r.Verdict switch
    {
        Verdict.NotTested => $"Range: rotate around the edge — {r.Coverage * 100:0}% of directions mapped.",
        Verdict.Good => $"Range: good — reaches {r.AverageEdge * 100:0}% (weakest direction {r.WeakestEdge * 100:0}%).",
        Verdict.Warning => $"Range: slightly short in one direction ({r.WeakestEdge * 100:0}%). Lowering the outer deadzone helps.",
        _ => $"Range: short ({r.WeakestEdge * 100:0}% in the weakest direction). Lower the outer deadzone to about {Math.Floor(r.WeakestEdge * 100) - 2:0}%.",
    };

    private static string TriggerText(byte now, TriggerResult t) =>
        $"now {now,3}   rest {(t.Min == 0 && t.Max == 0 ? "—" : t.Min.ToString()),3}   max {t.Max,3}   " + (t.Verdict switch
        {
            Verdict.Good => "✓ full range",
            Verdict.Warning => t.Min > 3 ? "doesn't fully return to 0" : "didn't reach 255",
            _ => "pull it all the way",
        });

    private static string MotionText(MotionResult m) => m.Verdict switch
    {
        Verdict.NotTested => "Not checked yet.",
        Verdict.Good => $"Good — gyro reads {m.GyroBias:0.00}°/s at rest (noise {m.GyroNoise:0.00}), gravity {m.Gravity:0.00} g.",
        Verdict.Warning => $"Slightly off — gyro {m.GyroBias:0.00}°/s at rest, gravity {m.Gravity:0.00} g. Make sure it was completely still.",
        _ => $"Unusual — gyro {m.GyroBias:0.00}°/s at rest, gravity {m.Gravity:0.00} g. Retry on a flat, still surface.",
    };

    private static void SetBar(FrameworkElement track, FrameworkElement fill, byte v) =>
        fill.Width = track.ActualWidth * v / 255.0;

    // ---------------- buttons ----------------

    private void BuildButtonGrid()
    {
        foreach (var b in ButtonCoverage.Standard.Concat(ButtonCoverage.EdgeExtras))
        {
            var chip = new Border
            {
                Width = 110,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = Res("DsStrokeBrush"),
                Background = Res("DsSurfaceRaisedBrush"),
                Child = new TextBlock
                {
                    Text = ButtonNames.Name(b),
                    FontSize = 12,
                    Foreground = Res("DsTextPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            if (ButtonCoverage.EdgeExtras.Contains(b)) chip.Opacity = 0.5; // Edge only
            _chips[b] = chip;
            ButtonGrid.Children.Add(chip);
        }
    }

    private void OnResetButtons(object sender, RoutedEventArgs e) => _buttons.Reset();

    /// <summary>The buttons the connected controller actually has.</summary>
    private List<DsButtons> ExpectedButtons()
    {
        if (_xbox) return XboxProtocol.Buttons.ToList();
        bool edge = AppServices.Controller.Info?.Model == DualSenseModel.DualSenseEdge;
        return ButtonCoverage.Standard.Concat(edge ? ButtonCoverage.EdgeExtras : Array.Empty<DsButtons>()).ToList();
    }

    /// <summary>Switches labels, button chips and visible tests between DualSense and Xbox.</summary>
    private void ApplyFamily(bool xbox)
    {
        _xbox = xbox;
        XboxBar.IsOpen = xbox;
        TouchCard.Visibility = MotionCard.Visibility = xbox ? Visibility.Collapsed : Visibility.Visible;
        L2Label.Text = xbox ? "LT" : "L2";
        R2Label.Text = xbox ? "RT" : "R2";
        foreach (var (b, chip) in _chips)
        {
            ((TextBlock)chip.Child).Text = ButtonNames.Name(b, xbox ? ControllerFamily.Xbox : ControllerFamily.DualSense);
            chip.Visibility = !xbox || XboxProtocol.Buttons.Contains(b) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------------- sticks ----------------

    private void OnDriftTest(object sender, RoutedEventArgs e) => StartDrift();

    private void StartDrift()
    {
        _leftDrift.Reset();
        _rightDrift.Reset();
        DriftButton.IsEnabled = false;
        // Short grace period so letting go of the sticks isn't measured as drift.
        Volatile.Write(ref _driftUntil, Now + Ticks(5.5));
        Volatile.Write(ref _driftGraceUntil, Now + Ticks(0.5));
    }
    private long _driftGraceUntil;

    private void OnResetRange(object sender, RoutedEventArgs e)
    {
        _leftRange.Reset();
        _rightRange.Reset();
    }

    /// <summary>Draws one stick: outline, mapped range, short trail, live dot.</summary>
    private sealed class StickView
    {
        private const double C = 90, R = 80;
        private readonly Polygon _range;
        private readonly Polyline _trail;
        private readonly Ellipse _dot;
        private readonly Queue<Point> _points = new();

        public StickView(Canvas canvas)
        {
            var outline = new Ellipse { Width = 2 * R, Height = 2 * R, Stroke = Res("DsStrokeBrush"), StrokeThickness = 1.5, Fill = Res("DsSurfaceRaisedBrush") };
            Canvas.SetLeft(outline, C - R);
            Canvas.SetTop(outline, C - R);
            canvas.Children.Add(outline);
            canvas.Children.Add(new Line { X1 = C - R, Y1 = C, X2 = C + R, Y2 = C, Stroke = Res("DsStrokeBrush") });
            canvas.Children.Add(new Line { X1 = C, Y1 = C - R, X2 = C, Y2 = C + R, Stroke = Res("DsStrokeBrush") });
            _range = new Polygon { Stroke = Res("DsAccentBrush"), StrokeThickness = 1.5, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 0xF2, 0xB5, 0x44)) };
            _trail = new Polyline { Stroke = Res("DsTextTertiaryBrush"), StrokeThickness = 1 };
            _dot = new Ellipse { Width = 12, Height = 12, Fill = Res("DsAccentBrush"), Stroke = Res("DsTextPrimaryBrush"), StrokeThickness = 1.5 };
            canvas.Children.Add(_range);
            canvas.Children.Add(_trail);
            canvas.Children.Add(_dot);
        }

        public void Update(float x, float y, double[] maxima)
        {
            var p = new Point(C + x * R, C - y * R);
            _points.Enqueue(p);
            while (_points.Count > 45) _points.Dequeue();
            var trail = new PointCollection();
            foreach (var q in _points) trail.Add(q);
            _trail.Points = trail;
            Canvas.SetLeft(_dot, p.X - 6);
            Canvas.SetTop(_dot, p.Y - 6);

            var poly = new PointCollection();
            if (maxima.Count(v => v > 0) >= 3)
            {
                for (int i = 0; i < maxima.Length; i++)
                {
                    if (maxima[i] <= 0) continue;
                    double a = RangeAnalyzer.BinAngle(i), m = Math.Min(maxima[i], 1.2);
                    poly.Add(new Point(C + Math.Cos(a) * m * R, C - Math.Sin(a) * m * R));
                }
            }
            _range.Points = poly;
        }
    }

    // ---------------- triggers ----------------

    private void OnResetTriggers(object sender, RoutedEventArgs e) { _l2.Reset(); _r2.Reset(); }

    // ---------------- touchpad ----------------

    private void BuildTouch()
    {
        var colors = new[] { Res("DsAccentBrush"), Res("DsSuccessBrush") };
        for (int i = 0; i < 2; i++)
        {
            _touchLines[i] = new Polyline { Stroke = colors[i], StrokeThickness = 1.5, Opacity = 0.6 };
            _touchDots[i] = new Ellipse { Width = 16, Height = 16, Fill = colors[i], Visibility = Visibility.Collapsed };
            TouchCanvas.Children.Add(_touchLines[i]);
            TouchCanvas.Children.Add(_touchDots[i]);
        }
    }

    private void UpdateTouch(ControllerState s)
    {
        var touches = new[] { s.Touch0, s.Touch1 };
        var lines = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            var t = touches[i];
            if (!t.Active)
            {
                _touchDots[i].Visibility = Visibility.Collapsed;
                if (_touchTrails[i].Count > 0) { _touchTrails[i].Clear(); _touchLines[i].Points = new PointCollection(); }
                continue;
            }
            var p = new Point(t.X / 1920.0 * 320, t.Y / 1080.0 * 180);
            _touchTrails[i].Add(p);
            if (_touchTrails[i].Count > 120) _touchTrails[i].RemoveAt(0);
            var pc = new PointCollection();
            foreach (var q in _touchTrails[i]) pc.Add(q);
            _touchLines[i].Points = pc;
            _touchDots[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(_touchDots[i], p.X - 8);
            Canvas.SetTop(_touchDots[i], p.Y - 8);
            lines.Add($"finger {i + 1}: ({t.X,4}, {t.Y,4})");
        }
        TouchText.Text = lines.Count == 0 ? "No touch." : string.Join("   ", lines);
        TouchClick.Text = s.IsPressed(DsButtons.Touchpad) ? "CLICK" : "";
    }

    // ---------------- motion ----------------

    private void OnMotionTest(object sender, RoutedEventArgs e)
    {
        _motion.Reset();
        MotionButton.IsEnabled = false;
        Volatile.Write(ref _motionUntil, Now + Ticks(3));
    }

    // ---------------- full diagnostic ----------------

    private void OnWizardStart(object sender, RoutedEventArgs e)
    {
        if (AppServices.Controller.Status != ConnectionStatus.Connected) return;
        _buttons.Reset(); _leftRange.Reset(); _rightRange.Reset(); _l2.Reset(); _r2.Reset();
        _leftDrift.Reset(); _rightDrift.Reset(); _motion.Reset();
        _touchSeen = _twoTouchSeen = false;
        _lastLeftDrift = _lastRightDrift = DriftResult.Empty;
        _lastMotion = MotionResult.Empty;
        ReportPanel.Visibility = Visibility.Collapsed;
        WizardPanel.Visibility = Visibility.Visible;
        WizardSkip.Visibility = Visibility.Visible;
        WizardButton.Content = "Restart";
        GoTo(Step.Buttons);
    }

    private void OnWizardSkip(object sender, RoutedEventArgs e) => GoTo(_step + 1);

    private void GoTo(Step step)
    {
        // Finish measurements belonging to the step we're leaving.
        if (_step == Step.Rest)
        {
            Volatile.Write(ref _driftUntil, 0);
            Volatile.Write(ref _motionUntil, 0);
            _lastLeftDrift = _leftDrift.Result();
            _lastRightDrift = _rightDrift.Result();
            _lastMotion = _motion.Result();
            DriftButton.IsEnabled = MotionButton.IsEnabled = true;
        }

        if (_xbox && step == Step.Touch) step = Step.Done; // no touchpad on Xbox pads
        _step = step;
        _stepStarted = Now;
        WizardProgress.Maximum = _xbox ? 4 : 5;
        WizardProgress.Value = Math.Min((int)step - 1, _xbox ? 4 : 5);
        (string title, string text) = _xbox ? step switch
        {
            Step.Buttons => ("1 of 4 · Buttons", "Press every button once: A, B, X, Y, D-pad, LB/RB, LT/RT fully, both stick clicks, View, Menu and the Xbox button."),
            Step.Rest => ("2 of 4 · Stick drift", "Put the controller down on a flat surface and let go completely. Measuring for 5 seconds…"),
            Step.Circles => ("3 of 4 · Stick range", "Rotate both sticks slowly around their outer edge — two or three full circles each."),
            Step.Triggers => ("4 of 4 · Triggers", "Pull LT and RT all the way down, then let go."),
            _ => ("Done", ""),
        } : step switch
        {
            Step.Buttons => ("1 of 5 · Buttons", "Press every button once: face buttons, D-pad, L1/R1, L2/R2 fully, L3/R3 (click the sticks), Create, Options, PS, Mute and the touchpad click."),
            Step.Rest => ("2 of 5 · Stick drift and motion sensors", "Put the controller down on a flat surface and let go completely. Measuring for 5 seconds…"),
            Step.Circles => ("3 of 5 · Stick range", "Rotate both sticks slowly around their outer edge — two or three full circles each."),
            Step.Triggers => ("4 of 5 · Triggers", "Pull L2 and R2 all the way down, then let go."),
            Step.Touch => ("5 of 5 · Touchpad", "Touch the touchpad with two fingers at once."),
            _ => ("Done", ""),
        };
        WizardStepTitle.Text = title;
        WizardStepText.Text = text;

        if (step == Step.Rest)
        {
            _leftDrift.Reset(); _rightDrift.Reset(); _motion.Reset();
            // 1 s to put it down, then 5 s of measurement.
            _restMeasureFrom = Now + Ticks(1.0);
            Volatile.Write(ref _driftGraceUntil, _restMeasureFrom);
        }
        if (step == Step.Done) FinishWizard();
    }
    private long _restMeasureFrom;

    private void AdvanceWizard(ControllerState s)
    {
        double elapsed = (Now - _stepStarted) / (double)Stopwatch.Frequency;
        switch (_step)
        {
            case Step.Buttons:
            {
                var missing = _buttons.Missing(ExpectedButtons());
                WizardStepStatus.Text = missing.Count == 0 ? "All buttons registered ✓" : "Still to press: " + string.Join(", ", missing.Select(b => ButtonNames.Name(b)));
                if (missing.Count == 0) GoTo(Step.Rest);
                break;
            }
            case Step.Rest:
            {
                long now = Now;
                if (now < _restMeasureFrom)
                {
                    WizardStepStatus.Text = "Let go…";
                }
                else
                {
                    if (Volatile.Read(ref _driftUntil) == 0)
                    {
                        Volatile.Write(ref _driftUntil, _restMeasureFrom + Ticks(5));
                        Volatile.Write(ref _motionUntil, _restMeasureFrom + Ticks(5));
                    }
                    double left = Math.Max(0, (_restMeasureFrom + Ticks(5) - now) / (double)Stopwatch.Frequency);
                    WizardStepStatus.Text = $"Measuring… {left:0.0} s";
                    if (left <= 0) GoTo(Step.Circles);
                }
                break;
            }
            case Step.Circles:
            {
                var l = _leftRange.Result();
                var r = _rightRange.Result();
                WizardStepStatus.Text = $"Left stick {l.Coverage * 100:0}% mapped · right stick {r.Coverage * 100:0}% mapped";
                if ((l.Coverage >= 1 && r.Coverage >= 1 && elapsed > 3) || elapsed > 25) GoTo(Step.Triggers);
                break;
            }
            case Step.Triggers:
            {
                var l2 = _l2.Result();
                var r2 = _r2.Result();
                WizardStepStatus.Text = _xbox ? $"LT max {l2.Max} · RT max {r2.Max}" : $"L2 max {l2.Max} · R2 max {r2.Max}";
                if ((l2.Max >= 250 && r2.Max >= 250 && s.L2 < 10 && s.R2 < 10) || elapsed > 20) GoTo(Step.Touch);
                break;
            }
            case Step.Touch:
                WizardStepStatus.Text = _twoTouchSeen ? "Two fingers detected ✓" : _touchSeen ? "One finger detected — now two at once." : "Waiting for touch…";
                if (_twoTouchSeen || elapsed > 20) GoTo(Step.Done);
                break;
        }
    }

    private void FinishWizard()
    {
        WizardPanel.Visibility = Visibility.Collapsed;
        WizardSkip.Visibility = Visibility.Collapsed;
        WizardButton.Content = "Run again";
        var c = AppServices.Controller;
        var stats = c.Telemetry?.Snapshot();
        ReportBox.Text = DiagnosticReport.Build(
            c.Info?.Name ?? "Controller", c.Info?.FirmwareVersion ?? (_xbox ? "not available through XInput" : "not reported"),
            stats is { HasData: true } st ? st.MeasuredRateHz : 0,
            _buttons.Missing(ExpectedButtons()), ButtonNames.Name,
            _lastLeftDrift, _lastRightDrift,
            _leftRange.Result(), _rightRange.Result(),
            _l2.Result(), _r2.Result(),
            _touchSeen, _lastMotion, DateTime.Now, _xbox);
        ApplyDeadzonesButton.IsEnabled = _lastLeftDrift.Verdict != Verdict.NotTested;
        ReportStatus.Text = "";
        ReportPanel.Visibility = Visibility.Visible;
    }

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(ReportBox.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        ReportStatus.Text = "Copied.";
    }

    private async void OnSaveReport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current!));
            picker.FileTypeChoices.Add("Text file", new List<string> { ".txt" });
            picker.SuggestedFileName = $"{(_xbox ? "Xbox controller" : "DualSense")} diagnostic {DateTime.Now:yyyy-MM-dd HHmm}";
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await Windows.Storage.FileIO.WriteTextAsync(file, ReportBox.Text);
            ReportStatus.Text = $"Saved to {file.Path}";
        }
        catch (Exception ex) { ReportStatus.Text = $"Couldn't save: {ex.Message}"; }
    }

    /// <summary>Raises (never lowers) each stick's inner deadzone in the active profile to the measured suggestion.</summary>
    private void OnApplyDeadzones(object sender, RoutedEventArgs e)
    {
        var p = AppServices.ActiveProfile;
        if (p == null) return;
        double l = Math.Max(p.LeftStick.InnerDeadzone, _lastLeftDrift.SuggestedDeadzone);
        double r = Math.Max(p.RightStick.InnerDeadzone, _lastRightDrift.SuggestedDeadzone);
        AppServices.UpdateActiveProfile(x => x with
        {
            LeftStick = x.LeftStick with { InnerDeadzone = l, OuterDeadzone = Math.Max(x.LeftStick.OuterDeadzone, l + 0.05) },
            RightStick = x.RightStick with { InnerDeadzone = r, OuterDeadzone = Math.Max(x.RightStick.OuterDeadzone, r + 0.05) },
        });
        ReportStatus.Text = $"Profile \"{p.Name}\": left inner deadzone {l * 100:0}%, right {r * 100:0}%. (Existing larger deadzones were kept.)";
    }

    // ---------------- links ----------------

    private void OnOpenController(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Controller");
    private void OnOpenPerformance(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo("Performance");
}
