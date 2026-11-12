using System.Text.Json;
using System.Text.Json.Serialization;
using DualSensePrecision.Core;

namespace DualSensePrecision.Configuration;

public enum AppTheme { Dark, Light, System }

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool LaunchOnStartup { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public bool AutomaticControllerDetection { get; init; } = true;
    public string? DefaultProfileId { get; init; }
    public string? ActiveProfileId { get; init; }
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public string AccentColor { get; init; } = "#3D7BFF";
    public bool Animations { get; init; } = true;
    public bool PerformanceLogging { get; init; }
    public bool DiagnosticLogging { get; init; }
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Development mode: use the simulated controller. Always shown with a "SIMULATED" badge.</summary>
    public bool UseMockController { get; init; }

    /// <summary>Virtual output rate cap in Hz. 0 = event-driven (one output per input report, no added latency).</summary>
    public int OutputRateHz { get; init; }

    /// <summary>Which controller to use when a DualSense and an Xbox controller are both connected.</summary>
    public ControllerFamily PreferredController { get; init; } = ControllerFamily.DualSense;

    /// <summary>Hide the real controller from games while the virtual pad runs (needs HidHide; off until the user agrees).</summary>
    public bool HideRealController { get; init; }

    /// <summary>The user has read and accepted what hiding does.</summary>
    public bool HidHideConsent { get; init; }

    /// <summary>Device instance IDs this app added to HidHide's list — only these are ever removed again.</summary>
    public string[]? HiddenByApp { get; init; }

    /// <summary>True if this app switched HidHide's hiding on (so it switches it back off afterwards).</summary>
    public bool HidHideActivatedByApp { get; init; }

    /// <summary>Interface colours (Settings → Interface). Defaults to the Opifex palette.</summary>
    public ThemePalette Palette { get; init; } = new();

    /// <summary>Interface scale, 0.75–1.5 (1 = 100 %).</summary>
    public double UiScale { get; init; } = 1.0;

    /// <summary>The welcome guide has been shown once.</summary>
    public bool FirstRunDone { get; init; }

    /// <summary>The "still running in the tray" notice has been shown once.</summary>
    public bool TrayHintShown { get; init; }
}

public static class AppPaths
{
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSensePrecision");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ProfilesDir => Path.Combine(Root, "profiles");
    public static string LogsDir => Path.Combine(Root, "logs");
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Write to a temp file then replace, so a crash mid-write never corrupts the original.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class SettingsStore
{
    private readonly string _path;
    public string? LastError { get; private set; }

    public SettingsStore(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json.Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Corrupt settings: keep a copy for diagnosis and continue with defaults — never fail silently.
            LastError = $"Settings file could not be read and was reset ({ex.Message}). A backup was kept as settings.json.bad.";
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch { }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) => Json.WriteAtomic(_path, settings);
}
