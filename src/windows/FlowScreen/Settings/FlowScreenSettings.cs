using System.Text.Json.Serialization;

namespace FlowScreen.Settings;

public enum EffectId
{
    FiberFlow,
}

public enum DensityLevel
{
    Low,
    Medium,
    High,
    Ultra,
}

public enum PaletteId
{
    Pink,
    Purple,
    Blue,
    Cyber,
    Mono,
    Custom,
}

public enum AdaptiveQualityMode
{
    Off,
    Balanced,
    Performance,
}

public enum MultiMonitorMode
{
    Independent,
    Synchronized,
}

/// <summary>
/// The settings contract. Serialised to <c>settings.json</c> and handed to the
/// renderer verbatim, so the property names here must match
/// <c>src/renderer/src/config/types.ts</c> exactly (camelCase after the JSON
/// naming policy is applied).
/// </summary>
public sealed class FlowScreenSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public EffectId Effect { get; set; } = EffectId.FiberFlow;

    public DensityLevel Density { get; set; } = DensityLevel.High;

    /// <summary>Simulation speed multiplier, 0.1 - 3.0.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>Fiber length multiplier, 0.2 - 3.0.</summary>
    public double FiberLength { get; set; } = 1.0;

    /// <summary>Bloom strength multiplier, 0.0 - 2.0.</summary>
    public double Glow { get; set; } = 1.0;

    /// <summary>Temporal persistence, 0.0 - 0.95.</summary>
    public double Trail { get; set; } = 0.85;

    public PaletteId Palette { get; set; } = PaletteId.Pink;

    /// <summary>Six sRGB hex stops, dark to light. Used only when <see cref="Palette"/> is Custom.</summary>
    public string[] CustomPalette { get; set; } = PalettePresets.DefaultCustom.ToArray();

    /// <summary>Frames per second cap. 0 means unlimited.</summary>
    public int FpsLimit { get; set; } = 60;

    public bool Vsync { get; set; } = true;

    /// <summary>0.5, 0.75 or 1.0.</summary>
    public double RenderScale { get; set; } = 1.0;

    public AdaptiveQualityMode AdaptiveQuality { get; set; } = AdaptiveQualityMode.Balanced;

    public MultiMonitorMode MultiMonitorMode { get; set; } = MultiMonitorMode.Synchronized;

    public bool RandomSeedOnLaunch { get; set; } = true;

    public int Seed { get; set; } = 12345;

    public bool ShowDebugOverlay { get; set; }

    [JsonIgnore]
    public static IReadOnlyList<int> FpsChoices { get; } = [30, 60, 120, 0];

    [JsonIgnore]
    public static IReadOnlyList<double> RenderScaleChoices { get; } = [0.5, 0.75, 1.0];

    public FlowScreenSettings Clone() => new()
    {
        Version = CurrentVersion,
        Effect = Effect,
        Density = Density,
        Speed = Speed,
        FiberLength = FiberLength,
        Glow = Glow,
        Trail = Trail,
        Palette = Palette,
        CustomPalette = CustomPalette.ToArray(),
        FpsLimit = FpsLimit,
        Vsync = Vsync,
        RenderScale = RenderScale,
        AdaptiveQuality = AdaptiveQuality,
        MultiMonitorMode = MultiMonitorMode,
        RandomSeedOnLaunch = RandomSeedOnLaunch,
        Seed = Seed,
        ShowDebugOverlay = ShowDebugOverlay,
    };

    /// <summary>
    /// Forces every field into its supported range. Called after loading and
    /// before saving, so a hand-edited or partially-written file can never put
    /// the renderer into a state it does not expect.
    /// </summary>
    public FlowScreenSettings Normalize()
    {
        Version = CurrentVersion;
        Effect = EffectId.FiberFlow;

        Density = Enum.IsDefined(Density) ? Density : DensityLevel.High;
        Palette = Enum.IsDefined(Palette) ? Palette : PaletteId.Pink;
        AdaptiveQuality = Enum.IsDefined(AdaptiveQuality) ? AdaptiveQuality : AdaptiveQualityMode.Balanced;
        MultiMonitorMode = Enum.IsDefined(MultiMonitorMode) ? MultiMonitorMode : MultiMonitorMode.Synchronized;

        Speed = Clamp(Speed, 0.1, 3.0, 1.0);
        FiberLength = Clamp(FiberLength, 0.2, 3.0, 1.0);
        Glow = Clamp(Glow, 0.0, 2.0, 1.0);
        Trail = Clamp(Trail, 0.0, 0.95, 0.85);

        FpsLimit = FpsLimit is >= 0 and <= 480 ? FpsLimit : 60;
        RenderScale = RenderScaleChoices.Contains(RenderScale) ? RenderScale : 1.0;

        if (Seed is < 0 or > 2147483646) Seed = 12345;

        CustomPalette = PalettePresets.NormalizeCustom(CustomPalette);
        return this;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
