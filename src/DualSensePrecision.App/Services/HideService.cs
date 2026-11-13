using System.Diagnostics;
using DualSensePrecision.Core;
using DualSensePrecision.HID.HidHide;
using DualSensePrecision.HID.Protocol;
using DualSensePrecision.VirtualController;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Optional: hides the real controller from games (via the HidHide driver) while the virtual pad is running,
/// so games see one controller instead of two. Only active when the user has switched it on and agreed.
///
/// Rules it keeps:
///  - Hides only while the virtual pad is connected; un-hides when it stops, the app closes, or the setting is off.
///  - Adds only its own entries to HidHide's lists and removes only those, so other apps' settings are left alone.
///  - Remembers what it hid (in settings), so after a crash the next start un-hides it.
///  - Dual Precision itself is on HidHide's allowed-apps list, so it keeps reading the controller.
/// </summary>
public sealed class HideService : IDisposable
{
    private readonly object _lock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private volatile bool _stop;

    public bool IsInstalled { get; private set; }

    /// <summary>Installed but its driver isn't running yet — Windows needs a restart.</summary>
    public bool NeedsRestart { get; private set; }
    public bool IsHiding { get; private set; }
    public IReadOnlyList<string> Hidden { get; private set; } = Array.Empty<string>();
    public string Status { get; private set; } = "";
    public string? LastError { get; private set; }

    /// <summary>Raised (on a background thread) whenever the state above changes.</summary>
    public event Action? Changed;

    public HideService()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "HidHide" };
        _worker.Start();
        AppServices.Controller.StatusChanged += OnControllerStatus;
    }

    private void OnControllerStatus(ConnectionStatus _) => Reevaluate();

    /// <summary>Re-check what should be hidden (cheap to call often; work happens on the worker thread).
    /// Safe to call after Dispose (the controller can still report "disconnected" while the app shuts down).</summary>
    public void Reevaluate()
    {
        if (_stop) return;
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void Run()
    {
        // First pass: undo anything left over from a previous run that didn't exit cleanly.
        try
        {
            IsInstalled = HidHideClient.IsInstalled;
            NeedsRestart = !IsInstalled && HidHideClient.IsRegistered;
            Restore("Cleaned up after the last session.");
            Apply(); // fills in the status line straight away
        }
        catch (Exception ex) { LastError = ex.Message; }

        while (!_stop)
        {
            _wake.WaitOne(5000); // periodic pass catches a second real controller plugged in while hiding
            if (_stop) break;
            try { Apply(); }
            catch (Exception ex) { LastError = ex.Message; Status = "Couldn't change HidHide: " + ex.Message; AppLog.Error("HidHide change failed", ex); Notify(); }
        }
    }

    private bool Wanted(out ControllerFamily family, out string? path)
    {
        var c = AppServices.Controller;
        family = c.Family;
        path = c.Info?.DevicePath;
        return AppServices.Settings.HideRealController
            && AppServices.Viiper?.State == VirtualState.Connected
            && c.Status == ConnectionStatus.Connected
            && !c.IsMock;
    }

    private void Apply()
    {
        lock (_lock)
        {
            IsInstalled = HidHideClient.IsInstalled;
            NeedsRestart = !IsInstalled && HidHideClient.IsRegistered;
            if (!AppServices.Settings.HideRealController)
            {
                Restore(null, CurrentTargetsOrEmpty());
                Status = "Off. Games see your real controller as well as the virtual one.";
                Notify();
                return;
            }
            if (!IsInstalled)
            {
                Status = NeedsRestart
                    ? "HidHide is installed but not running yet. Restart Windows to finish installing it."
                    : "HidHide isn't installed, so nothing can be hidden yet.";
                Notify();
                return;
            }
            if (!Wanted(out var family, out var path))
            {
                Restore(null);
                Status = AppServices.Viiper?.State == VirtualState.Connected
                    ? "Waiting for a controller."
                    : "Waiting for the virtual controller to be switched on. Your controller stays visible until then.";
                Notify();
                return;
            }

            var targets = family == ControllerFamily.Xbox
                ? HideTargets.ForXboxControllers()
                : path != null ? HideTargets.ForDualSense(path) : new List<string>();
            if (targets.Count == 0)
            {
                Status = "Couldn't find your controller's device entries to hide.";
                Notify();
                return;
            }

            var settings = AppServices.Settings;
            var ours = settings.HiddenByApp ?? Array.Empty<string>();
            if (IsHiding && targets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Hidden)) return; // nothing changed

            // 1. Make sure this app can still read the controller.
            var me = HidHideClient.OwnImagePath() ?? throw new HidHideException("Couldn't work out this app's path for HidHide's allowed list.");
            var wl = HidHideClient.GetWhitelist();
            if (!wl.Contains(me, StringComparer.OrdinalIgnoreCase)) HidHideClient.SetWhitelist(wl.Append(me));

            // 2. Record what we're about to hide BEFORE hiding it, so a failure or crash at any later step can
            //    still be undone (on the next pass, on exit, or on the next start). Every target counts as ours.
            var claim = ours.Union(targets, StringComparer.OrdinalIgnoreCase).ToArray();
            AppServices.SaveSettings(AppServices.Settings with { HiddenByApp = claim });

            // 3. Swap our old entries for the new ones (other apps' entries untouched).
            var bl = HidHideLogic.RemoveEntries(HidHideClient.GetBlacklist(), ours);
            var (newList, _) = HidHideLogic.AddEntries(bl, targets);
            HidHideClient.SetBlacklist(newList);
            AppServices.SaveSettings(AppServices.Settings with { HiddenByApp = targets.ToArray() });

            // 4. Switch hiding on if it was off (and remember to switch it back).
            if (!HidHideClient.GetActive())
            {
                AppServices.SaveSettings(AppServices.Settings with { HidHideActivatedByApp = true });
                HidHideClient.SetActive(true);
            }

            IsHiding = true;
            Hidden = targets;
            AppLog.Info($"Hiding real {family} controller from games ({targets.Count} device entries)");
            LastError = null;
            Status = $"Hiding your {(family == ControllerFamily.Xbox ? "Xbox controller" : "DualSense")} from games ({targets.Count} device entries). " +
                     "Start (or restart) your game now so it picks this up.";
            Notify();
        }
    }

    /// <summary>The current controller's device entries, if one is connected (used when the user switches hiding off).</summary>
    private static IReadOnlyList<string> CurrentTargetsOrEmpty()
    {
        try
        {
            var c = AppServices.Controller;
            if (c.Status != ConnectionStatus.Connected || c.IsMock) return Array.Empty<string>();
            return c.Family == ControllerFamily.Xbox ? HideTargets.ForXboxControllers()
                : c.Info?.DevicePath is { } p ? HideTargets.ForDualSense(p) : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Removes what this app hid (plus, when the user switches hiding off, the connected controller's entries, so it's
    /// visible again even if an earlier version lost track of them), and switches HidHide back off if this app switched it on.
    /// </summary>
    private void Restore(string? message, IReadOnlyList<string>? alsoUnhide = null)
    {
        lock (_lock)
        {
            var s = AppServices.Settings;
            var ours = (s.HiddenByApp ?? Array.Empty<string>()).Concat(alsoUnhide ?? Array.Empty<string>()).ToArray();
            if (ours.Length == 0 && !s.HidHideActivatedByApp) { IsHiding = false; Hidden = Array.Empty<string>(); return; }
            if (!HidHideClient.IsInstalled) return;

            if (ours.Length > 0)
            {
                var current = HidHideClient.GetBlacklist();
                var kept = HidHideLogic.RemoveEntries(current, ours);
                if (kept.Count != current.Count) HidHideClient.SetBlacklist(kept);
            }
            if (s.HidHideActivatedByApp && HidHideClient.GetBlacklist().Count == 0) HidHideClient.SetActive(false);
            if ((s.HiddenByApp?.Length ?? 0) > 0 || s.HidHideActivatedByApp) // only write settings when something was recorded
                AppServices.SaveSettings(AppServices.Settings with { HiddenByApp = Array.Empty<string>(), HidHideActivatedByApp = false });
            if (IsHiding || ours.Length > 0) AppLog.Info("Real controller un-hidden");
            IsHiding = false;
            Hidden = Array.Empty<string>();
            if (message != null) Status = message;
            Notify();
        }
    }

    private void Notify() { try { Changed?.Invoke(); } catch { } }

    /// <summary>
    /// Asks a separate program (PowerShell, which isn't on HidHide's allowed list — just like a game) how many
    /// Xbox-style controllers it can see through XInput. Takes a second or two.
    /// </summary>
    public static async Task<string> CheckWhatGamesSeeAsync()
    {
        const string script =
            "$t=Add-Type -MemberDefinition '[DllImport(\"xinput1_4.dll\")] public static extern int XInputGetState(int i, byte[] s);' -Name X -Namespace P -PassThru;" +
            "$n=0; foreach($i in 0..3){ $b=New-Object byte[] 16; if($t::XInputGetState($i,$b) -eq 0){$n++} }; $n";
        try
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (!int.TryParse(output.Trim().Split('\n').LastOrDefault()?.Trim(), out int games))
                return "The check couldn't run (PowerShell didn't answer).";
            int app = XInputProbe.Connected().Count;
            bool virtualOn = AppServices.Viiper?.State == VirtualState.Connected;
            var c = AppServices.Controller;
            if (c.Status == ConnectionStatus.Connected && c.Family == ControllerFamily.DualSense && !c.IsMock && c.Info?.DevicePath is { } dsPath)
            {
                // A DualSense isn't an XInput device, so also ask: can an outside program open the DualSense itself?
                bool gamesCanOpen = await CanOutsideProgramOpenAsync(dsPath);
                string ds = gamesCanOpen ? "Games CAN still see your DualSense." : "✓ Your DualSense is hidden from games.";
                return $"{ds} Games see {games} Xbox-style controller{(games == 1 ? "" : "s")} (the virtual pad{(games == 1 ? "" : "s")})." +
                       (!virtualOn ? " The virtual controller is off." : "");
            }
            string verdict = !virtualOn
                ? "The virtual controller is off."
                : games == 1 && app >= 2 ? "✓ Games see exactly one Xbox controller: the virtual pad. This app still sees your real one."
                : games == 1 ? "✓ Games see exactly one Xbox controller: the virtual pad."
                : games == 0 ? "Games see no Xbox controller — the virtual pad may still be starting."
                : $"Games still see {games} Xbox controllers. If you just switched hiding on, unplug and reconnect the controller (or restart Windows once after installing HidHide).";
            return $"Games see {games} Xbox-style controller{(games == 1 ? "" : "s")}; this app sees {app}. {verdict}";
        }
        catch (Exception ex)
        {
            return $"The check couldn't run: {ex.Message}";
        }
    }

    /// <summary>Runs PowerShell (not on HidHide's allowed list, like a game) and tries to open the device.</summary>
    private static async Task<bool> CanOutsideProgramOpenAsync(string devicePath)
    {
        string script =
            "$t=Add-Type -MemberDefinition '[DllImport(\"kernel32.dll\", CharSet=CharSet.Unicode, SetLastError=true)] public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string n, uint a, uint s, System.IntPtr p, uint c, uint f, System.IntPtr t);' -Name F -Namespace P -PassThru;" +
            "$h=$t::CreateFileW('" + devicePath.Replace("'", "''") + "', 0, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero); if ($h.IsInvalid) { 'no' } else { $h.Dispose(); 'yes' }";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return output.Trim().EndsWith("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un-hides on exit so the controller is never left invisible.</summary>
    public void Dispose()
    {
        if (_stop) return;
        _stop = true;
        AppServices.Controller.StatusChanged -= OnControllerStatus;
        _wake.Set();
        _worker.Join(2000);
        try { Restore(null); } catch { /* next start cleans up */ }
        _wake.Dispose();
    }
}
