using DualSensePrecision.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace DualSensePrecision.App.Services;

/// <summary>
/// Applies the user's interface colours live. Every page uses shared brush objects from Themes/Tokens.xaml, so
/// changing a brush's colour repaints everything that uses it immediately — no restart, no page reload.
/// Four colours are chosen by the user (accents stay Opifex amber/ember); the rest are derived so any combination stays consistent.
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
        // Accents are always the Opifex amber/ember (not user-editable).
        var accent = ColorMath.Parse(ThemePalette.OpifexAccent, d.Accent);
        var accent2 = ColorMath.Parse(ThemePalette.OpifexAccent2, d.Accent2);

        var raised = ColorMath.Mix(panel, text, 0.035);
        var secondary = ColorMath.Mix(text, bg, 0.35);
        var tertiary = ColorMath.Mix(text, bg, 0.55);
        bool light = p.IsLight;
        var onAccent = ColorMath.OnColor(accent, new ColorMath.Rgb(0x1A, 0x14, 0x08), new ColorMath.Rgb(0xFF, 0xFF, 0xFF));
        // Amber text on a light background is hard to read: darken it there.
        var accentText = light ? ColorMath.Mix(accent, new ColorMath.Rgb(0, 0, 0), 0.45) : ColorMath.Mix(accent, text, 0.2);

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
        Set("DsLogoBrush", ColorMath.Parse(p.Logo, d.Logo));
        SetGradient(res, "DsVoltGradientBrush", W(accent), W(accent2));
        SetGradient(res, "DsHeroGradientBrush", W(ColorMath.Mix(panel, accent, 0.12)), W(panel));

        // Stock WinUI controls (theme dictionary in Tokens.xaml)
        foreach (var themeKey in new[] { "Dark", "Light" })
        {
            if (Tokens()?.ThemeDictionaries.TryGetValue(themeKey, out var td) != true || td is not ResourceDictionary dark) continue;
            void T(string key, ColorMath.Rgb c, byte a = 255)
            {
                if (dark.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = W(c, a);
            }
            T("AccentFillColorDefaultBrush", accent);
            T("AccentFillColorSecondaryBrush", accent, 0xE6);
            T("AccentFillColorTertiaryBrush", accent, 0xCC);
            T("AccentTextFillColorPrimaryBrush", accentText);
            T("AccentTextFillColorSecondaryBrush", light ? accentText : accent);
            T("AccentTextFillColorTertiaryBrush", ColorMath.Mix(accentText, bg, 0.15));
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
        IsLight = light;
        Applied?.Invoke();
    }

    /// <summary>True while a light palette is applied (built-in controls use their light style).</summary>
    public static bool IsLight { get; private set; }

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
