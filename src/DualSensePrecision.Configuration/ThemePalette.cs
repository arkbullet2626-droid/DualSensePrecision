using System.Globalization;

namespace DualSensePrecision.Configuration;

/// <summary>
/// The interface colours (hex #RRGGBB). Users edit the four surface/text colours; the accents are the fixed
/// Opifex Studios amber and ember. Every other shade the app uses is derived from these.
/// </summary>
public sealed record ThemePalette
{
    public string Background { get; init; } = "#0B0906";
    public string Panel { get; init; } = "#15110B";
    public string Border { get; init; } = "#2E2617";
    public string Text { get; init; } = "#F2EBDD";

    /// <summary>The DP logo's colour. Follows the theme; each preset has its own.</summary>
    public string Logo { get; init; } = "#F2B544";
    public string Accent { get; init; } = OpifexAccent;
    public string Accent2 { get; init; } = OpifexAccent2;

    public const string OpifexAccent = "#F2B544";
    public const string OpifexAccent2 = "#E0782F";

    public static ThemePalette Opifex { get; } = new();

    public static IReadOnlyList<(string Name, ThemePalette Palette)> Presets { get; } = new[]
    {
        // Dark
        ("Opifex (default)", Opifex),
        ("Midnight", new ThemePalette { Background = "#0B0D12", Panel = "#12151C", Border = "#232836", Text = "#E8EBF2", Logo = "#3D7BFF" }),
        ("Forest", new ThemePalette { Background = "#0A0F0B", Panel = "#111A13", Border = "#22332A", Text = "#E6F2E9", Logo = "#5BE3A6" }),
        ("Deep Red", new ThemePalette { Background = "#120405", Panel = "#1F080A", Border = "#451318", Text = "#F7E4E4", Logo = "#FF3B4E" }),
        ("Crimson", new ThemePalette { Background = "#0E0809", Panel = "#180E10", Border = "#33191D", Text = "#F4E6E8", Logo = "#FF6B7A" }),
        ("Graphite", new ThemePalette { Background = "#0A0A0A", Panel = "#141414", Border = "#2A2A2A", Text = "#F0F0F0", Logo = "#E8E8E8" }),
        // Fun
        ("Synthwave", new ThemePalette { Background = "#120A1F", Panel = "#1E1033", Border = "#46236E", Text = "#FFE3F6", Logo = "#FF4FB8" }),
        ("Deep Sea", new ThemePalette { Background = "#04131A", Panel = "#082331", Border = "#13455C", Text = "#DDF6FF", Logo = "#2EC4E6" }),
        ("Toxic", new ThemePalette { Background = "#0A0F04", Panel = "#131D08", Border = "#2F4A10", Text = "#EEFFD6", Logo = "#B6FF3B" }),
        // Light
        ("Paper (light)", new ThemePalette { Background = "#F5F1E8", Panel = "#FFFDF8", Border = "#E0D6C2", Text = "#1E1A14", Logo = "#B5761A" }),
        ("Daylight (light)", new ThemePalette { Background = "#F2F3F6", Panel = "#FFFFFF", Border = "#D7DAE1", Text = "#15171C", Logo = "#2F6BFF" }),
        ("Sand (light)", new ThemePalette { Background = "#EAE0CC", Panel = "#F6EFE1", Border = "#CDBD9C", Text = "#2A2117", Logo = "#A0521C" }),
    };

    /// <summary>Light backgrounds switch the app's built-in controls to their light style.</summary>
    public bool IsLight => ColorMath.Luminance(ColorMath.Parse(Background, Opifex.Background)) > 0.4;

    /// <summary>True if every slot is a valid #RRGGBB colour.</summary>
    public bool IsValid => new[] { Background, Panel, Border, Text, Logo, Accent, Accent2 }.All(h => ColorMath.TryParse(h, out _));
}

/// <summary>Small, dependency-free colour helpers (sRGB, 0–255 channels).</summary>
public static class ColorMath
{
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    }

    public static bool TryParse(string? hex, out Rgb c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(ch => new string(ch, 2))); // #abc → #aabbcc
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        c = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Parse(string hex, string fallback) => TryParse(hex, out var c) ? c : TryParse(fallback, out var f) ? f : default;

    /// <summary>Linear blend: t = 0 → a, t = 1 → b.</summary>
    public static Rgb Mix(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return new Rgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Rgb c)
    {
        static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary>WCAG contrast ratio, 1 to 21. 4.5+ is comfortable for body text.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Text colour for labels drawn on top of <paramref name="fill"/>: whichever of dark or light reads better.</summary>
    public static Rgb OnColor(Rgb fill, Rgb dark, Rgb light) => Contrast(fill, dark) >= Contrast(fill, light) ? dark : light;
}
