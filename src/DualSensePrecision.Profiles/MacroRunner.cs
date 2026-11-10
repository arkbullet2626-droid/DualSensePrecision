using System.Diagnostics;
using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;

namespace DualSensePrecision.Profiles;

/// <summary>Where a running macro sends its output. Implemented by the app (virtual pad, SendInput) and by tests.</summary>
public interface IMacroSink
{
    void Button(DsButtons button, bool down);
    void Key(ushort virtualKey, bool down);
    void Mouse(MouseButtonTarget button, bool down);
}

/// <summary>
/// Hard safety limits. Macros only press buttons, keys and mouse buttons — there are deliberately no stick,
/// gyro or mouse-movement steps, so a macro can't aim or compensate recoil.
/// </summary>
public static class MacroLimits
{
    public const int MaxSteps = 256;
    public const int MaxDelayMs = 10_000;
    /// <summary>One play-through can't run longer than this.</summary>
    public const int MaxSingleRunMs = 60_000;
    /// <summary>A repeating macro stops itself after this long, even if the button is still held.</summary>
    public const int MaxRepeatMs = 10 * 60_000;
    /// <summary>Each repeat cycle takes at least this long, so a macro with no delays can't flood input.</summary>
    public const int MinCycleMs = 20;
    public const int MaxNameLength = 48;

    /// <summary>Clamps a macro into the safe limits. Never throws.</summary>
    public static MacroDefinition Sanitize(MacroDefinition m)
    {
        var steps = m.Steps.Take(MaxSteps)
            .Select(s => s.Kind == MacroStepKind.Delay ? s with { DelayMs = Math.Clamp(s.DelayMs, 0, MaxDelayMs) } : s)
            .ToList();
        string name = string.IsNullOrWhiteSpace(m.Name) ? "Macro" : m.Name.Trim();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        return m with { Name = name, Steps = steps };
    }

    /// <summary>Plain-language warnings shown in the editor. Not errors: the runner handles all of these safely.</summary>
    public static List<string> Warnings(MacroDefinition m)
    {
        var w = new List<string>();
        if (m.Steps.Count == 0) w.Add("This macro has no steps yet.");
        if (m.Steps.Count > MaxSteps) w.Add($"Only the first {MaxSteps} steps will run.");
        int total = TotalDelayMs(m);
        if (total > MaxSingleRunMs) w.Add($"Longer than {MaxSingleRunMs / 1000} seconds — it will be stopped at that point.");
        if (m.PlayMode != MacroPlayMode.Once && total < MinCycleMs)
            w.Add($"Repeats are spaced at least {MinCycleMs} ms apart so the macro can't flood input.");

        var down = new HashSet<string>();
        foreach (var s in m.Steps)
        {
            string? key = s.Kind switch
            {
                MacroStepKind.ButtonDown or MacroStepKind.ButtonUp => "b" + s.Button,
                MacroStepKind.KeyDown or MacroStepKind.KeyUp => "k" + s.VirtualKey,
                MacroStepKind.MouseDown or MacroStepKind.MouseUp => "m" + s.Mouse,
                _ => null,
            };
            if (key == null) continue;
            if (s.Kind is MacroStepKind.ButtonDown or MacroStepKind.KeyDown or MacroStepKind.MouseDown) down.Add(key);
            else down.Remove(key);
        }
        if (down.Count > 0) w.Add("Something is pressed but never released — it will be released automatically when the macro ends.");
        return w;
    }

    public static int TotalDelayMs(MacroDefinition m) =>
        m.Steps.Where(s => s.Kind == MacroStepKind.Delay).Sum(s => Math.Clamp(s.DelayMs, 0, MaxDelayMs));
}

/// <summary>
/// Plays one macro on its own thread. Everything it pressed is released when it finishes, is stopped,
/// or hits a limit — a macro can never leave a key or button stuck down.
/// </summary>
public sealed class MacroRun
{
    private readonly MacroDefinition _macro;
    private readonly IMacroSink _sink;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly Func<int, ManualResetEventSlim, bool> _wait;
    private readonly HashSet<DsButtons> _buttons = new();
    private readonly HashSet<ushort> _keys = new();
    private readonly HashSet<MouseButtonTarget> _mice = new();
    private Thread? _thread;

    public bool Repeat { get; }
    public bool IsRunning => _thread is { IsAlive: true };
    public int Cycles { get; private set; }
    public string? StopReason { get; private set; }
    public event Action<MacroRun>? Finished;

    /// <param name="wait">Waits the given ms or until the event is set; returns false if stopped. Tests pass an instant fake.</param>
    public MacroRun(MacroDefinition macro, IMacroSink sink, bool repeat, Func<int, ManualResetEventSlim, bool>? wait = null)
    {
        _macro = MacroLimits.Sanitize(macro);
        _sink = sink;
        Repeat = repeat;
        _wait = wait ?? PreciseWait;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = $"Macro: {_macro.Name}", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Synchronous variant (tests).</summary>
    public void RunToEnd() => Run();

    public void Stop(string reason = "Stopped")
    {
        StopReason ??= reason;
        _stopEvent.Set();
    }

    public void Join(int timeoutMs = 2000) => _thread?.Join(timeoutMs);

    private void Run()
    {
        var total = Stopwatch.StartNew();
        try
        {
            do
            {
                var cycle = Stopwatch.StartNew();
                foreach (var step in _macro.Steps)
                {
                    if (_stopEvent.IsSet) return;
                    if (total.ElapsedMilliseconds > (Repeat ? MacroLimits.MaxRepeatMs : MacroLimits.MaxSingleRunMs))
                    {
                        StopReason ??= "Time limit reached";
                        return;
                    }
                    if (!Execute(step)) return;
                }
                Cycles++;

                if (Repeat)
                {
                    // Release between cycles so a repeated "press" is seen as separate presses.
                    ReleaseAll();
                    int rest = MacroLimits.MinCycleMs - (int)cycle.ElapsedMilliseconds;
                    if (rest > 0 && !_wait(rest, _stopEvent)) return;
                    if (total.ElapsedMilliseconds > MacroLimits.MaxRepeatMs) { StopReason ??= "Time limit reached"; return; }
                }
            }
            while (Repeat && !_stopEvent.IsSet);
        }
        finally
        {
            ReleaseAll();
            StopReason ??= "Finished";
            try { Finished?.Invoke(this); } catch { }
        }
    }

    private bool Execute(MacroStep s)
    {
        switch (s.Kind)
        {
            case MacroStepKind.Delay:
                return s.DelayMs <= 0 || _wait(s.DelayMs, _stopEvent);
            case MacroStepKind.ButtonDown:
                if (s.Button != DsButtons.None && _buttons.Add(s.Button)) _sink.Button(s.Button, true);
                break;
            case MacroStepKind.ButtonUp:
                if (_buttons.Remove(s.Button)) _sink.Button(s.Button, false);
                break;
            case MacroStepKind.KeyDown:
                if (s.VirtualKey != 0 && _keys.Add(s.VirtualKey)) _sink.Key(s.VirtualKey, true);
                break;
            case MacroStepKind.KeyUp:
                if (_keys.Remove(s.VirtualKey)) _sink.Key(s.VirtualKey, false);
                break;
            case MacroStepKind.MouseDown:
                if (_mice.Add(s.Mouse)) _sink.Mouse(s.Mouse, true);
                break;
            case MacroStepKind.MouseUp:
                if (_mice.Remove(s.Mouse)) _sink.Mouse(s.Mouse, false);
                break;
        }
        return true;
    }

    private void ReleaseAll()
    {
        foreach (var k in _keys) _sink.Key(k, false);
        foreach (var m in _mice) _sink.Mouse(m, false);
        foreach (var b in _buttons) _sink.Button(b, false);
        _keys.Clear(); _mice.Clear(); _buttons.Clear();
    }

    /// <summary>
    /// Millisecond-accurate wait that stays stoppable. Windows' sleep granularity can be ~15 ms, so it sleeps
    /// in coarse chunks and spins only for the final stretch.
    /// </summary>
    public static bool PreciseWait(int ms, ManualResetEventSlim stop)
    {
        long deadline = Stopwatch.GetTimestamp() + ms * Stopwatch.Frequency / 1000;
        while (true)
        {
            // Compare in raw timer ticks: rounding to whole milliseconds would end waits up to 1 ms early.
            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0) return !stop.IsSet;
            double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 20)
            {
                if (stop.Wait((int)remainingMs - 16)) return false;
            }
            else
            {
                if (stop.IsSet) return false;
                Thread.Yield();
            }
        }
    }
}

/// <summary>
/// Records DualSense button presses (not sticks) with their timing, from timestamps taken on the HID reader thread.
/// Stops itself at the step or time limit.
/// </summary>
public sealed class MacroRecorder
{
    private readonly object _lock = new();
    private readonly List<MacroStep> _steps = new();
    private DsButtons _previous;
    private long _lastEventTicks;
    private long _startTicks;

    public bool IsRecording { get; private set; }
    public int StepCount { get { lock (_lock) return _steps.Count; } }

    public void Start(DsButtons currentlyHeld)
    {
        lock (_lock)
        {
            _steps.Clear();
            _previous = currentlyHeld; // ignore buttons already held when recording starts
            _lastEventTicks = 0;
            _startTicks = Stopwatch.GetTimestamp();
            IsRecording = true;
        }
    }

    /// <summary>Called for every input report while recording.</summary>
    public void OnReport(DsButtons physical, long timestamp)
    {
        if (!IsRecording) return;
        lock (_lock)
        {
            if (!IsRecording) return;
            if ((timestamp - _startTicks) * 1000 / Stopwatch.Frequency > MacroLimits.MaxSingleRunMs) { IsRecording = false; return; }
            DsButtons changed = physical ^ _previous;
            if (changed == DsButtons.None) return;
            _previous = physical;

            if (_lastEventTicks != 0)
            {
                int ms = (int)Math.Min(MacroLimits.MaxDelayMs, (timestamp - _lastEventTicks) * 1000 / Stopwatch.Frequency);
                if (ms > 0) _steps.Add(new MacroStep(MacroStepKind.Delay, DelayMs: ms));
            }
            _lastEventTicks = timestamp;

            foreach (var b in ButtonMapper.MappableButtons)
            {
                if ((changed & b) == 0) continue;
                _steps.Add(new MacroStep((physical & b) != 0 ? MacroStepKind.ButtonDown : MacroStepKind.ButtonUp, b));
            }
            if (_steps.Count >= MacroLimits.MaxSteps - 8) IsRecording = false;
        }
    }

    /// <summary>Stops and returns the steps, releasing anything still held at the end.</summary>
    public List<MacroStep> Stop()
    {
        lock (_lock)
        {
            IsRecording = false;
            var result = new List<MacroStep>(_steps);
            foreach (var b in ButtonMapper.MappableButtons)
                if ((_previous & b) != 0 && result.Any(s => s.Kind == MacroStepKind.ButtonDown && s.Button == b))
                    result.Add(new MacroStep(MacroStepKind.ButtonUp, b));
            return result;
        }
    }
}
