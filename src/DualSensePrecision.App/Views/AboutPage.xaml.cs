using DualSensePrecision.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DualSensePrecision.App.Views;

public sealed partial class AboutPage : Page
{
    public const string PrivacyUrl = "https://opifexstudios.ca/#dualsense-precision-privacy";
    public const string WebsiteUrl = "https://opifexstudios.ca";

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"Version {UpdateChecker.Current}";
    }

    private static async void Open(string url) => await global::Windows.System.Launcher.LaunchUriAsync(new Uri(url));

    private static void OpenFile(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        else
            Open(UpdateChecker.SourcePage);
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckRing.IsActive = true;
        UpdateText.Text = "Checking…";
        var r = await UpdateChecker.CheckAsync();
        UpdateText.Text = r.Message;
        GetUpdate.Visibility = r.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        CheckRing.IsActive = false;
        CheckButton.IsEnabled = true;
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e) => Open(UpdateChecker.ReleasesPage);
    private async void OnWelcome(object sender, RoutedEventArgs e) { if (MainWindow.Current is { } w) await w.ShowWelcomeAsync(); }
    private void OnIssues(object sender, RoutedEventArgs e) => Open(UpdateChecker.IssuesPage);
    private void OnLicense(object sender, RoutedEventArgs e) => OpenFile("LICENSE.txt");
    private void OnNotices(object sender, RoutedEventArgs e) => OpenFile("THIRD-PARTY-NOTICES.txt");
    private void OnPrivacy(object sender, RoutedEventArgs e) => Open(PrivacyUrl);
    private void OnSource(object sender, RoutedEventArgs e) => Open(UpdateChecker.SourcePage);
    private void OnWebsite(object sender, RoutedEventArgs e) => Open(WebsiteUrl);

    private async void OnExport(object sender, RoutedEventArgs e) => HelpText.Text = await SettingsPage.ExportDiagnosticsAsync();
}
