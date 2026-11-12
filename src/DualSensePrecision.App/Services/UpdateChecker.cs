using System.Net.Http;
using System.Text.Json;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Asks GitHub for the newest release's version number. That's the only thing the app ever sends over the
/// internet: GitHub sees the request (IP address and a "DualSensePrecision/x.y.z" user agent), nothing else.
/// Can be switched off in Settings.
/// </summary>
public static class UpdateChecker
{
    public const string Owner = "arkbullet2626-droid";
    public const string Repo = "DualSensePrecision";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases/latest";
    public static string DirectDownload => $"https://github.com/{Owner}/{Repo}/releases/latest/download/DualSensePrecision-Setup.exe";
    public static string IssuesPage => $"https://github.com/{Owner}/{Repo}/issues";
    public static string SourcePage => $"https://github.com/{Owner}/{Repo}";

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public sealed record Result(bool Ok, Version? Latest, bool UpdateAvailable, string Message);

    public static async Task<Result> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"DualSensePrecision/{Current}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
                return new Result(false, null, false, $"Couldn't check for updates (GitHub answered {(int)resp.StatusCode}).");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var latest))
                return new Result(false, null, false, "Couldn't read the latest version from GitHub.");
            bool newer = latest > Current;
            var msg = newer ? $"Version {latest} is available (you have {Current})." : $"You're up to date ({Current}).";
            AppLog.Info($"Update check: latest {latest}, current {Current}");
            return new Result(true, latest, newer, msg);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update check failed: {ex.Message}");
            return new Result(false, null, false, "Couldn't check for updates (no internet connection?).");
        }
    }
}
