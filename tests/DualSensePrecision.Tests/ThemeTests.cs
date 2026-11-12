using DualSensePrecision.Configuration;
using Xunit;

namespace DualSensePrecision.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("#F2B544", 0xF2, 0xB5, 0x44)]
    [InlineData("f2b544", 0xF2, 0xB5, 0x44)]
    [InlineData(" #0b0906 ", 0x0B, 0x09, 0x06)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void ParsesHex(string hex, int r, int g, int b)
    {
        Assert.True(ColorMath.TryParse(hex, out var c));
        Assert.Equal(new ColorMath.Rgb((byte)r, (byte)g, (byte)b), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("blue")]
    [InlineData(null)]
    public void RejectsBadHex(string? hex) => Assert.False(ColorMath.TryParse(hex, out _));

    [Fact]
    public void HexRoundTripsUppercase() => Assert.Equal("#F2B544", ColorMath.Parse("#f2b544", "#000000").Hex);

    [Fact]
    public void BadSlotFallsBack() => Assert.Equal("#0B0906", ColorMath.Parse("nope", "#0B0906").Hex);

    [Fact]
    public void MixBlends()
    {
        var a = new ColorMath.Rgb(0, 0, 0);
        var b = new ColorMath.Rgb(200, 100, 50);
        Assert.Equal(a, ColorMath.Mix(a, b, 0));
        Assert.Equal(b, ColorMath.Mix(a, b, 1));
        Assert.Equal(new ColorMath.Rgb(100, 50, 25), ColorMath.Mix(a, b, 0.5));
        Assert.Equal(b, ColorMath.Mix(a, b, 7)); // clamped
    }

    [Fact]
    public void ContrastMatchesWcag()
    {
        var black = new ColorMath.Rgb(0, 0, 0);
        var white = new ColorMath.Rgb(255, 255, 255);
        Assert.Equal(21, ColorMath.Contrast(black, white), 1);
        Assert.Equal(1, ColorMath.Contrast(white, white), 3);
        // The Opifex defaults are comfortably readable.
        Assert.True(ColorMath.Contrast(ColorMath.Parse("#F2EBDD", ""), ColorMath.Parse("#0B0906", "")) > 15);
    }

    [Fact]
    public void TextOnAccentPicksTheReadableOne()
    {
        var dark = new ColorMath.Rgb(10, 8, 5);
        var light = new ColorMath.Rgb(242, 235, 221);
        Assert.Equal(dark, ColorMath.OnColor(ColorMath.Parse("#F2B544", ""), dark, light));  // amber → dark text
        Assert.Equal(light, ColorMath.OnColor(ColorMath.Parse("#1F3FA0", ""), dark, light)); // deep blue → light text
    }

    [Fact]
    public void PresetsAreValidAndDefaultIsOpifex()
    {
        Assert.All(ThemePalette.Presets, p => Assert.True(p.Palette.IsValid, p.Name));
        Assert.Equal(ThemePalette.Opifex, ThemePalette.Presets[0].Palette);
        Assert.Equal("#F2B544", new ThemePalette().Accent);
        Assert.False((new ThemePalette() with { Text = "#12" }).IsValid);
        Assert.All(ThemePalette.Presets, p => Assert.Equal(ThemePalette.OpifexAccent, p.Palette.Accent)); // accents aren't themable
    }

    [Fact]
    public void SettingsRoundTripKeepsPaletteAndScale()
    {
        var s = new AppSettings { Palette = ThemePalette.Presets[1].Palette, UiScale = 1.25 };
        var json = System.Text.Json.JsonSerializer.Serialize(s, Json.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, Json.Options)!;
        Assert.Equal(s.Palette, back.Palette);
        Assert.Equal(1.25, back.UiScale);
    }
}
