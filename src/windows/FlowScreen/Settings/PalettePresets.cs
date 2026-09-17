using System.Text.RegularExpressions;

namespace FlowScreen.Settings;

/// <summary>
/// Mirror of <c>src/renderer/src/config/palettes.ts</c>. The renderer owns the
/// authoritative values; these exist so the settings dialog can show swatches
/// without booting a WebView.
/// </summary>
public static partial class PalettePresets
{
    public static IReadOnlyList<string> DefaultCustom { get; } =
        ["#1E0030", "#560B5E", "#A81B7E", "#F04BA2", "#FF9DD2", "#FFF4FA"];

    public static IReadOnlyDictionary<PaletteId, string[]> Stops { get; } =
        new Dictionary<PaletteId, string[]>
        {
            [PaletteId.Pink] = ["#1E0030", "#560B5E", "#A81B7E", "#F04BA2", "#FF9DD2", "#FFF4FA"],
            [PaletteId.Purple] = ["#1E0533", "#4B1080", "#8A2BE2", "#B36BFF", "#DDB6FF", "#F6ECFF"],
            [PaletteId.Blue] = ["#001B3A", "#0A3D82", "#1E7BE0", "#5FB8FF", "#A8DDFF", "#EDF8FF"],
            [PaletteId.Cyber] = ["#04122B", "#0E5CA8", "#12D8E0", "#B14BFF", "#FF2ECC", "#FFF3FF"],
            [PaletteId.Mono] = ["#0A0A0C", "#2A2A30", "#5A5A66", "#9A9AA8", "#D2D2DC", "#FFFFFF"],
        };

    public static string[] Resolve(PaletteId id, IReadOnlyList<string> custom) =>
        id == PaletteId.Custom ? NormalizeCustom(custom) : Stops[id].ToArray();

    /// <summary>Guarantees exactly six well-formed <c>#rrggbb</c> stops.</summary>
    public static string[] NormalizeCustom(IReadOnlyList<string>? custom)
    {
        var result = DefaultCustom.ToArray();
        if (custom is null) return result;

        for (var i = 0; i < result.Length && i < custom.Count; i++)
        {
            var value = custom[i];
            if (!string.IsNullOrWhiteSpace(value) && HexColor().IsMatch(value.Trim()))
            {
                result[i] = value.Trim().ToUpperInvariant();
            }
        }

        return result;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
