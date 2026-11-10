using DualSensePrecision.Core;
using DualSensePrecision.InputEngine;
using DualSensePrecision.Profiles;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Starts and stops macros when buttons mapped to them are pressed, and routes their output:
/// controller buttons → virtual controller (merged with live input), keys/mouse → SendInput.
///
/// Safety:
///   • a macro can't trigger another macro (its output never goes back through the mapper);
///   • every macro releases everything it pressed when it ends or is stopped;
///   • Create + Options together stops every macro instantly;
///   • unplugging the controller, switching profile or closing the app stops every macro;
///   • time and step limits in <see cref="MacroLimits"/>.
/// </summary>
public sealed class MacroEngine : IDisposable
{
    private readonly ControllerService _controller;
    private readonly object _lock = new();
    private readonly Dictionary<DsButtons, MacroRun> _byTrigger = new();
    private readonly List<MacroRun> _manual = new();
    private readonly Dictionary<MacroRun, DsButtons> _held = new();
    private DsButtons _previousExternal;

    public const DsButtons EmergencyStop = DsButtons.Create | DsButtons.Options;

    public event Action? RunningChanged;

    public MacroEngine(ControllerService controller)
    {
        _controller = controller;
        _controller.ButtonHook = OnButtons;
        _controller.StatusChanged += s => { if (s != ConnectionStatus.Connected) StopAll("Controller disconnected"); };
    }

    public int RunningCount { get { lock (_lock) return _byTrigger.Count + _manual.Count; } }

    /// <summary>Plays a macro right now (the editor's Test button).</summary>
    public void PlayNow(MacroDefinition macro)
    {
        var run = Create(macro, repeat: false);
        lock (_lock) _manual.Add(run);
        run.Start();
        RunningChanged?.Invoke();
    }

    public void StopAll(string reason = "Stopped")
    {
        List<MacroRun> runs;
        lock (_lock)
        {
            runs = _byTrigger.Values.Concat(_manual).ToList();
            _byTrigger.Clear();
            _manual.Clear();
        }
        foreach (var r in runs) r.Stop(reason);
        foreach (var r in runs) r.Join(500);
        if (runs.Count > 0) RunningChanged?.Invoke();
    }

    // Reader thread
    private void OnButtons(DsButtons physical, DsButtons external, ButtonMapper mapper)
    {
        if ((physical & EmergencyStop) == EmergencyStop && RunningCount > 0)
        {
            ThreadPool.QueueUserWorkItem(_ => StopAll("Emergency stop (Create + Options)"));
            _previousExternal = external;
            return;
        }

        DsButtons changed = external ^ _previousExternal;
        if (changed == DsButtons.None) return;
        _previousExternal = external;

        foreach (var b in ButtonMapper.MappableButtons)
        {
            if ((changed & b) == 0) continue;
            bool down = (external & b) != 0;
            var m = mapper.Resolve(b);

            if (down)
            {
                if (m.Kind != MappingKind.Macro) continue;
                var macro = AppServices.ActiveProfile?.Macros.FirstOrDefault(x => x.Id == m.MacroId);
                if (macro == null || !macro.Enabled) continue;
                OnTriggerPressed(b, macro);
            }
            else
            {
                OnTriggerReleased(b);
            }
        }
    }

    private void OnTriggerPressed(DsButtons trigger, MacroDefinition macro)
    {
        MacroRun? toStop = null, toStart = null;
        lock (_lock)
        {
            _byTrigger.TryGetValue(trigger, out var existing);
            bool running = existing is { IsRunning: true };
            switch (macro.PlayMode)
            {
                case MacroPlayMode.Toggle:
                    if (running) { toStop = existing; _byTrigger.Remove(trigger); }
                    else toStart = Create(macro, repeat: true);
                    break;
                case MacroPlayMode.WhileHeld:
                    if (!running) toStart = Create(macro, repeat: true);
                    break;
                default:
                    if (!running) toStart = Create(macro, repeat: false); // a press while it's still playing is ignored
                    break;
            }
            if (toStart != null) _byTrigger[trigger] = toStart;
        }
        toStop?.Stop("Toggled off");
        toStart?.Start();
        RunningChanged?.Invoke();
    }

    private void OnTriggerReleased(DsButtons trigger)
    {
        MacroRun? run;
        lock (_lock)
        {
            // Only "while held" macros stop on release; toggles keep running until pressed again,
            // and "once" macros always play to the end.
            if (!_byTrigger.TryGetValue(trigger, out run) || !_whileHeld.Contains(run)) return;
            _byTrigger.Remove(trigger);
        }
        run.Stop("Released");
        RunningChanged?.Invoke();
    }

    private readonly HashSet<MacroRun> _whileHeld = new();

    private MacroRun Create(MacroDefinition macro, bool repeat)
    {
        MacroRun? run = null;
        var sink = new Sink(this, () => run!);
        run = new MacroRun(macro, sink, repeat);
        if (repeat && macro.PlayMode == MacroPlayMode.WhileHeld) lock (_lock) _whileHeld.Add(run);
        run.Finished += r =>
        {
            lock (_lock)
            {
                _whileHeld.Remove(r);
                _manual.Remove(r);
                foreach (var kv in _byTrigger.Where(kv => kv.Value == r).ToList()) _byTrigger.Remove(kv.Key);
                _held.Remove(r);
            }
            PushInjected();
            RunningChanged?.Invoke();
        };
        return run;
    }

    private void SetHeld(MacroRun run, DsButtons button, bool down)
    {
        lock (_lock)
        {
            _held.TryGetValue(run, out var cur);
            _held[run] = down ? cur | button : cur & ~button;
        }
        PushInjected();
    }

    private void PushInjected()
    {
        DsButtons all = DsButtons.None;
        lock (_lock) foreach (var v in _held.Values) all |= v;
        _controller.SetInjectedButtons(all);
    }

    private sealed class Sink(MacroEngine engine, Func<MacroRun> run) : IMacroSink
    {
        public void Button(DsButtons button, bool down) => engine.SetHeld(run(), button, down);
        public void Key(ushort virtualKey, bool down) => KeyMouseOutput.SendKey(virtualKey, down);
        public void Mouse(MouseButtonTarget button, bool down) => KeyMouseOutput.SendMouse(button, down);
    }

    public void Dispose()
    {
        _controller.ButtonHook = null;
        StopAll("App closing");
        _controller.SetInjectedButtons(DsButtons.None);
    }
}
