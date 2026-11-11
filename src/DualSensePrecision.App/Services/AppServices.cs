using DualSensePrecision.Configuration;
using DualSensePrecision.Profiles;
using DualSensePrecision.VirtualController;
using Microsoft.UI.Dispatching;

namespace DualSensePrecision.App.Services;

/// <summary>Small service locator — the app has a handful of singletons; a DI container would add weight without value.</summary>
public static class AppServices
{
    public static SettingsStore SettingsStore { get; } = new();
    public static AppSettings Settings { get; set; } = new();
    public static ProfileStore Profiles { get; private set; } = null!;
    public static Profile? ActiveProfile { get; set; }
    public static ControllerService Controller { get; } = new();
    public static OutputManager Output { get; private set; } = null!;
    public static ViiperXbox360Backend? Viiper { get; private set; }
    public static MacroEngine Macros { get; private set; } = null!;
    public static HideService Hide { get; private set; } = null!;
    public static List<string> StartupWarnings { get; } = new();

    public static void Initialize()
    {
        Settings = SettingsStore.Load();
        if (SettingsStore.LastError is { } settingsErr) StartupWarnings.Add(settingsErr);

        Profiles = new ProfileStore();
        Profiles.EnsureDefaults();
        var all = Profiles.LoadAll(out var errors);
        foreach (var e in errors) StartupWarnings.Add($"A profile could not be loaded: {e}");

        // A chosen startup profile wins; otherwise reopen whatever was used last.
        ActiveProfile = all.FirstOrDefault(p => p.Id == Settings.DefaultProfileId)
                     ?? all.FirstOrDefault(p => p.Id == Settings.ActiveProfileId)
                     ?? all.FirstOrDefault(p => p.Name == "Default")
                     ?? all.FirstOrDefault();
        if (ActiveProfile != null) Controller.Pipeline.Settings = ActiveProfile.ToPipelineSettings();

        Controller.VirtualPhase = () => Viiper?.State switch
        {
            VirtualState.Connecting => DualSensePrecision.HID.Protocol.VirtualPadPhase.Connecting,
            VirtualState.Connected => DualSensePrecision.HID.Protocol.VirtualPadPhase.Connected,
            _ => DualSensePrecision.HID.Protocol.VirtualPadPhase.Off,
        };
        Output = new OutputManager(Controller);
        Macros = new MacroEngine(Controller);
        if (Settings.AutomaticControllerDetection || Settings.UseMockController)
            Controller.Start(Settings);
        Hide = new HideService();
        ApplyVirtualController();
    }

    /// <summary>Creates or removes the virtual controller to match the active profile.</summary>
    public static void ApplyVirtualController()
    {
        var target = ActiveProfile?.VirtualController.Target ?? VirtualTarget.None;
        if (target == VirtualTarget.Xbox360)
        {
            if (Viiper == null)
            {
                Viiper = new ViiperXbox360Backend();
                Viiper.RumbleRequested += (l, r) => Output.SetGameRumble(l, r);
                Viiper.StateChanged += _ => Hide?.Reevaluate();
                Controller.SetBackend(Viiper);
            }
            Viiper.Connect();
        }
        else if (Viiper != null)
        {
            var old = Viiper;
            Viiper = null;
            Controller.SetBackend(new NullBackend());
            Output.SetGameRumble(0, 0);
            Hide?.Reevaluate(); // virtual pad gone → un-hide the real controller
            Task.Run(old.Dispose); // removing the device from VIIPER can take a moment; keep the UI responsive
        }
    }

    private static readonly object SettingsLock = new();

    public static void SaveSettings(AppSettings s)
    {
        lock (SettingsLock)
        {
            Settings = s;
            SettingsStore.Save(s);
        }
    }

    // ---------- Switching profiles ----------

    /// <summary>Raised on the UI thread after the active profile changes or the profile list changes.</summary>
    public static event Action? ProfilesChanged;

    public static void NotifyProfilesChanged() => ProfilesChanged?.Invoke();

    /// <summary>
    /// Makes another profile active immediately: input processing, lights/triggers, virtual controller and
    /// mappings all switch over on the next controller report. Running macros are stopped and held keys released.
    /// </summary>
    public static void SwitchProfile(Profile next)
    {
        if (ActiveProfile?.Id == next.Id) return;
        FlushProfileSave();
        Macros.StopAll("Profile switched");
        Controller.KeyOutput.ReleaseAll();

        var previousTarget = ActiveProfile?.VirtualController.Target;
        ActiveProfile = next;
        Controller.Pipeline.Settings = next.ToPipelineSettings();
        Output.Refresh();
        if (previousTarget != next.VirtualController.Target) ApplyVirtualController();

        try { SaveSettings(Settings with { ActiveProfileId = next.Id }); }
        catch (Exception ex) { StartupWarnings.Add($"Couldn't remember the active profile: {ex.Message}"); }
        ProfilesChanged?.Invoke();
    }

    /// <summary>Reloads the active profile from disk (after it was replaced by an import or restore).</summary>
    public static void ReloadActive()
    {
        if (ActiveProfile == null) return;
        var fresh = Profiles.Get(ActiveProfile.Id);
        if (fresh == null) return;
        var previousTarget = ActiveProfile.VirtualController.Target;
        ActiveProfile = fresh;
        Controller.Pipeline.Settings = fresh.ToPipelineSettings();
        Output.Refresh();
        if (previousTarget != fresh.VirtualController.Target) ApplyVirtualController();
        ProfilesChanged?.Invoke();
    }

    // ---------- Live profile editing ----------

    private static readonly object SaveLock = new();
    private static Timer? _saveTimer;
    private static bool _savePending;

    /// <summary>Raised (on a thread-pool thread) if a profile save fails. Never silent.</summary>
    public static event Action<string>? ProfileSaveFailed;

    /// <summary>
    /// Applies an edit to the active profile immediately (the reader thread picks it up on the next report)
    /// and saves it to disk 400 ms after the last change, so dragging a slider doesn't hammer the disk.
    /// </summary>
    public static void UpdateActiveProfile(Func<Profile, Profile> change)
    {
        var current = ActiveProfile;
        if (current == null) return;
        var updated = change(current);
        ActiveProfile = updated;
        Controller.Pipeline.Settings = updated.ToPipelineSettings();
        Output.Refresh();
        if (updated.VirtualController.Target != current.VirtualController.Target) ApplyVirtualController();

        lock (SaveLock)
        {
            _savePending = true;
            _saveTimer ??= new Timer(_ => FlushProfileSave(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(400, Timeout.Infinite);
        }
    }

    public static void FlushProfileSave()
    {
        lock (SaveLock)
        {
            if (!_savePending) return;
            _savePending = false;
            var p = ActiveProfile;
            if (p == null) return;
            try { Profiles.Update(p); }
            catch (Exception ex) { ProfileSaveFailed?.Invoke($"Couldn't save profile \"{p.Name}\": {ex.Message}"); }
        }
    }

    public static void Shutdown()
    {
        FlushProfileSave();
        _saveTimer?.Dispose();
        Macros.Dispose();
        Output.Dispose();
        Hide?.Dispose();   // un-hide the real controller before the virtual pad goes away
        Viiper?.Dispose(); // removes the virtual pad so games don't see a frozen controller
        Controller.Dispose();
    }
}

/// <summary>
/// One UI-thread timer (~60 Hz) that pages subscribe to while visible. Decouples the 250–1000 Hz report stream
/// from rendering, so the UI never blocks the reader and never floods its own dispatcher.
/// </summary>
public sealed class UiSampler
{
    private readonly DispatcherQueueTimer _timer;
    public event Action? Tick;

    public UiSampler(DispatcherQueue queue, TimeSpan interval)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
