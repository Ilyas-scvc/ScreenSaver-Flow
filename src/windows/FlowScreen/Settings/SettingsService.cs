using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowScreen.Logging;

namespace FlowScreen.Settings;

/// <summary>
/// Loads and saves <c>%LOCALAPPDATA%\FlowScreen\settings.json</c>.
///
/// Loading never throws and never surfaces an error to the user: a corrupt file
/// silently falls back to defaults (and the bad file is kept as
/// <c>settings.corrupt.json</c> so it can be inspected). Saving is atomic, so a
/// crash or a power cut during a write cannot leave a truncated file behind.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly IAppLogger logger;

    public SettingsService(IAppLogger logger) => this.logger = logger;

    public string FilePath => AppPaths.SettingsFile;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            // The payload travels through a URL query string; escaping the
            // non-ASCII-safe set keeps that transport trivially correct.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public FlowScreenSettings Load()
    {
        var path = FilePath;

        try
        {
            if (!File.Exists(path))
            {
                logger.Info("no settings file, using defaults");
                return new FlowScreenSettings().Normalize();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<FlowScreenSettings>(json, Options);
            if (loaded is null)
            {
                logger.Warn("settings file deserialised to null, using defaults");
                return new FlowScreenSettings().Normalize();
            }

            logger.Info($"settings loaded from {path}");
            return loaded.Normalize();
        }
        catch (Exception ex)
        {
            logger.Warn($"settings file at {path} is unreadable, falling back to defaults", ex);
            QuarantineCorruptFile(path);
            return new FlowScreenSettings().Normalize();
        }
    }

    public bool Save(FlowScreenSettings settings)
    {
        var path = FilePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(settings.Normalize(), Options);

            // Write to a sibling then swap: a half-written settings.json would be
            // silently replaced by defaults on the next launch, losing the user's
            // configuration without any indication why.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);

            logger.Info($"settings saved to {path}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"failed to save settings to {path}", ex);
            return false;
        }
    }

    /// <summary>Serialises for transport to the renderer (compact, no indentation).</summary>
    public static string ToJson(FlowScreenSettings settings) =>
        JsonSerializer.Serialize(settings, Options);

    private void QuarantineCorruptFile(string path)
    {
        try
        {
            var target = Path.Combine(Path.GetDirectoryName(path)!, "settings.corrupt.json");
            File.Copy(path, target, overwrite: true);
            logger.Info($"corrupt settings copied to {target}");
        }
        catch (Exception ex)
        {
            logger.Warn("could not quarantine the corrupt settings file", ex);
        }
    }
}
