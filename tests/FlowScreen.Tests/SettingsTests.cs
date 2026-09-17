using System.Text.Json;
using FlowScreen.Hosting;
using FlowScreen.Settings;
using Xunit;

namespace FlowScreen.Tests;

/// <summary>
/// Settings must survive anything: a hand-edited file, a truncated write, a file
/// from a future version. Normalisation is the single choke point that
/// guarantees the renderer never receives a value it does not expect.
/// </summary>
public class SettingsTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var settings = new FlowScreenSettings().Normalize();

        Assert.Equal(FlowScreenSettings.CurrentVersion, settings.Version);
        Assert.Equal(EffectId.FiberFlow, settings.Effect);
        Assert.InRange(settings.Speed, 0.1, 3.0);
        Assert.InRange(settings.Trail, 0.0, 0.95);
        Assert.Equal(6, settings.CustomPalette.Length);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var settings = new FlowScreenSettings
        {
            Speed = 99,
            FiberLength = -4,
            Glow = 12,
            Trail = 5,
            RenderScale = 3.7,
            FpsLimit = 100000,
            Seed = -12,
        }.Normalize();

        Assert.Equal(3.0, settings.Speed);
        Assert.Equal(0.2, settings.FiberLength);
        Assert.Equal(2.0, settings.Glow);
        Assert.Equal(0.95, settings.Trail);
        Assert.Equal(1.0, settings.RenderScale);
        Assert.Equal(60, settings.FpsLimit);
        Assert.Equal(12345, settings.Seed);
    }

    [Fact]
    public void NonFiniteValuesFallBackToDefaults()
    {
        var settings = new FlowScreenSettings
        {
            Speed = double.NaN,
            Glow = double.PositiveInfinity,
        }.Normalize();

        Assert.Equal(1.0, settings.Speed);
        Assert.Equal(1.0, settings.Glow);
    }

    [Fact]
    public void UnlimitedFrameRateIsPreserved()
    {
        // 0 means "no cap" and must not be clamped up to a default.
        Assert.Equal(0, new FlowScreenSettings { FpsLimit = 0 }.Normalize().FpsLimit);
    }

    [Fact]
    public void InvalidEnumValuesFallBack()
    {
        var settings = new FlowScreenSettings
        {
            Density = (DensityLevel)99,
            Palette = (PaletteId)(-3),
            AdaptiveQuality = (AdaptiveQualityMode)42,
            MultiMonitorMode = (MultiMonitorMode)7,
        }.Normalize();

        Assert.Equal(DensityLevel.High, settings.Density);
        Assert.Equal(PaletteId.Pink, settings.Palette);
        Assert.Equal(AdaptiveQualityMode.Balanced, settings.AdaptiveQuality);
        Assert.Equal(MultiMonitorMode.Synchronized, settings.MultiMonitorMode);
    }

    [Fact]
    public void MalformedPaletteStopsAreReplacedIndividually()
    {
        var settings = new FlowScreenSettings
        {
            CustomPalette = ["#112233", "nonsense", "#445566", "", "#AABBCC", "#zz1122"],
        }.Normalize();

        Assert.Equal(6, settings.CustomPalette.Length);
        Assert.Equal("#112233", settings.CustomPalette[0]);
        Assert.Equal("#445566", settings.CustomPalette[2]);
        Assert.Equal("#AABBCC", settings.CustomPalette[4]);
        // The bad entries fall back to the corresponding default stop.
        Assert.Equal(PalettePresets.DefaultCustom[1], settings.CustomPalette[1]);
        Assert.Equal(PalettePresets.DefaultCustom[5], settings.CustomPalette[5]);
    }

    [Fact]
    public void ShortPaletteArraysAreFilledOut()
    {
        var settings = new FlowScreenSettings { CustomPalette = ["#010203"] }.Normalize();
        Assert.Equal(6, settings.CustomPalette.Length);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var original = new FlowScreenSettings().Normalize();
        var copy = original.Clone();

        copy.Speed = 2.5;
        copy.CustomPalette[0] = "#000000";

        Assert.NotEqual(copy.Speed, original.Speed);
        Assert.NotEqual(copy.CustomPalette[0], original.CustomPalette[0]);
    }

    [Fact]
    public void EveryPresetHasSixStops()
    {
        foreach (var (id, stops) in PalettePresets.Stops)
        {
            Assert.Equal(6, stops.Length);
            Assert.All(stops, stop => Assert.Matches("^#[0-9A-Fa-f]{6}$", stop));
            Assert.NotEqual(PaletteId.Custom, id);
        }
    }
}

/// <summary>
/// The renderer payload is the wire format between the host and the browser. Its
/// property names have to match <c>config/types.ts</c> exactly, so they are
/// asserted rather than trusted.
/// </summary>
public class RendererPayloadTests
{
    private static JsonElement Serialize()
    {
        var json = RendererPayload.Serialize(
            new FlowScreenSettings().Normalize(),
            ViewContextDto.Standalone(1920, 1080, 1_700_000_000_000));
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void UsesCamelCaseNames()
    {
        var root = Serialize();
        var settings = root.GetProperty("settings");

        Assert.True(settings.TryGetProperty("fiberLength", out _));
        Assert.True(settings.TryGetProperty("randomSeedOnLaunch", out _));
        Assert.True(settings.TryGetProperty("multiMonitorMode", out _));
        Assert.True(settings.TryGetProperty("showDebugOverlay", out _));
    }

    [Fact]
    public void SerialisesEnumsAsCamelCaseStrings()
    {
        var settings = Serialize().GetProperty("settings");

        Assert.Equal("fiberFlow", settings.GetProperty("effect").GetString());
        Assert.Equal("high", settings.GetProperty("density").GetString());
        Assert.Equal("pink", settings.GetProperty("palette").GetString());
        Assert.Equal("balanced", settings.GetProperty("adaptiveQuality").GetString());
        Assert.Equal("synchronized", settings.GetProperty("multiMonitorMode").GetString());
    }

    [Fact]
    public void ViewContextCarriesBothRectangles()
    {
        var view = Serialize().GetProperty("view");

        Assert.Equal(1_700_000_000_000, view.GetProperty("epochMs").GetInt64());
        Assert.Equal(1920, view.GetProperty("bounds").GetProperty("w").GetInt32());
        Assert.Equal(1080, view.GetProperty("virtual").GetProperty("h").GetInt32());
    }

    [Fact]
    public void EncodesAsUrlSafeBase64()
    {
        var encoded = RendererPayload.Encode(
            new FlowScreenSettings().Normalize(),
            ViewContextDto.Standalone(1920, 1080, 0));

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void EncodedPayloadRoundTrips()
    {
        var encoded = RendererPayload.Encode(
            new FlowScreenSettings { Seed = 4242 }.Normalize(),
            ViewContextDto.Standalone(2560, 1440, 12345));

        // Reproduce exactly what the renderer's decodeBase64Utf8 does.
        var normalised = encoded.Replace('-', '+').Replace('_', '/');
        normalised = normalised.PadRight(normalised.Length + ((4 - (normalised.Length % 4)) % 4), '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(normalised));

        using var document = JsonDocument.Parse(json);
        Assert.Equal(4242, document.RootElement.GetProperty("settings").GetProperty("seed").GetInt32());
        Assert.Equal(2560, document.RootElement.GetProperty("view").GetProperty("bounds").GetProperty("w").GetInt32());
    }
}
