using DualSensePrecision.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Applies the user's interface colours live. Every page uses shared brush objects from Themes/Tokens.xaml, so
/// changing a brush's colour repaints everything that uses it immediately — no restart, no page reload.
/// Six colours are chosen by the user; the rest are derived from them so any combination stays consistent.
/// </summary>
public static class ThemeService
{
    public static event Action? Applied;

    public static void Apply(ThemePalette p)
    {
        var d = ThemePalette.Opifex;
        var bg = ColorMath.Parse(p.Background, d.Background);
        var panel = ColorMath.Parse(p.Panel, d.Panel);
        var border = ColorMath.Parse(p.Border, d.Border);
        var text = ColorMath.Parse(p.Text, d.Text);
        var accent = ColorMath.Parse(p.Accent, d.Accent);
        var accent2 = ColorMath.Parse(p.Accent2, d.Accent2);

        var raised = ColorMath.Mix(panel, text, 0.035);
        var secondary = ColorMath.Mix(text, bg, 0.35);
        var tertiary = ColorMath.Mix(text, bg, 0.55);
        var darkInk = ColorMath.Mix(bg, new ColorMath.Rgb(0, 0, 0), 0.4);
        var onAccent = ColorMath.OnColor(accent, darkInk, text);

        var res = Tokens() ?? Application.Current.Resources;
        void Set(string key, ColorMath.Rgb c, byte a = 255)
        {
            if (res.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = W(c, a);
        }

        // App design tokens
        Set("DsBackgroundBrush", bg);
        Set("DsSurfaceBrush", panel);
        Set("DsSurfaceRaisedBrush", raised);
        Set("DsStrokeBrush", border);
        Set("DsTextPrimaryBrush", text);
        Set("DsTextSecondaryBrush", secondary);
        Set("DsTextTertiaryBrush", tertiary);
        Set("DsAccentBrush", accent);
        Set("DsAccentSoftBrush", accent, 0x26);
        Set("DsEmberBrush", accent2);
        Set("DsWarningBrush", accent2);
        Set("DsOnAccentBrush", onAccent);
        SetGradient(res, "DsVoltGradientBrush", W(accent), W(accent2));
        SetGradient(res, "DsHeroGradientBrush", W(ColorMath.Mix(panel, accent, 0.12)), W(panel));

        // Stock WinUI controls (theme dictionary in Tokens.xaml)
        if (Tokens()?.ThemeDictionaries.TryGetValue("Dark", out var td) == true && td is ResourceDictionary dark)
        {
            void T(string key, ColorMath.Rgb c, byte a = 255)
            {
                if (dark.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = W(c, a);
            }
            T("AccentFillColorDefaultBrush", accent);
            T("AccentFillColorSecondaryBrush", accent, 0xE6);
            T("AccentFillColorTertiaryBrush", accent, 0xCC);
            T("AccentTextFillColorPrimaryBrush", ColorMath.Mix(accent, text, 0.2));
            T("AccentTextFillColorSecondaryBrush", accent);
            T("AccentTextFillColorTertiaryBrush", ColorMath.Mix(accent, bg, 0.15));
            T("SystemControlHighlightAccentBrush", accent);
            T("SystemControlForegroundAccentBrush", accent);
            T("FocusStrokeColorOuterBrush", text);
            T("TextOnAccentFillColorPrimaryBrush", onAccent);
            T("TextOnAccentFillColorSecondaryBrush", ColorMath.Mix(onAccent, accent, 0.25));
            T("NavigationViewDefaultPaneBackground", bg);
            T("NavigationViewExpandedPaneBackground", bg);
            T("NavigationViewTopPaneBackground", bg);
            T("NavigationViewContentBackground", ColorMath.Mix(bg, panel, 0.3));
            T("NavigationViewContentGridBorderBrush", border);
            T("CardBackgroundFillColorDefaultBrush", panel);
            T("ControlFillColorDefaultBrush", raised);
            T("ControlFillColorSecondaryBrush", ColorMath.Mix(raised, text, 0.03));
            T("ControlFillColorTertiaryBrush", panel);
            T("ControlStrokeColorDefaultBrush", border);
            T("SolidBackgroundFillColorBaseBrush", bg);
            T("LayerFillColorDefaultBrush", ColorMath.Mix(bg, panel, 0.5));
            T("TextFillColorPrimaryBrush", text);
            T("TextFillColorSecondaryBrush", secondary);
            T("TextFillColorTertiaryBrush", tertiary);
        }
        Applied?.Invoke();
    }

    /// <summary>How readable the main text is on the background (WCAG contrast ratio, 1–21).</summary>
    public static double TextContrast(ThemePalette p) =>
        ColorMath.Contrast(ColorMath.Parse(p.Text, ThemePalette.Opifex.Text), ColorMath.Parse(p.Background, ThemePalette.Opifex.Background));

    private static ResourceDictionary? Tokens() =>
        Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source?.ToString().EndsWith("Tokens.xaml", StringComparison.OrdinalIgnoreCase) == true);

    private static void SetGradient(ResourceDictionary res, string key, WinColor a, WinColor b)
    {
        if (!res.TryGetValue(key, out var o) || o is not LinearGradientBrush g || g.GradientStops.Count < 2) return;
        g.GradientStops[0].Color = a;
        g.GradientStops[^1].Color = b;
    }

    public static WinColor W(ColorMath.Rgb c, byte a = 255) => WinColor.FromArgb(a, c.R, c.G, c.B);
}
