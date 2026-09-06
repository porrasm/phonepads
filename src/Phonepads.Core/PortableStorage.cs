using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonepads.Core;

/// <summary>Everything the app remembers between runs. Kept small and human-readable.</summary>
public sealed class AppSettings
{
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "https://gamepad.porras.club";
    [JsonPropertyName("gameName")] public string? GameName { get; set; }
    [JsonPropertyName("driverToken")] public string? DriverToken { get; set; }
    [JsonPropertyName("lastSchemaIds")] public List<string> LastSchemaIds { get; set; } = [];
    [JsonPropertyName("rumbleEnabled")] public bool RumbleEnabled { get; set; } = true;

    /// <summary>UDP port of the DSU server that Dolphin connects to for Wii Remote mode.</summary>
    [JsonPropertyName("dsuPort")] public int DsuPort { get; set; } = 26760;
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
public partial class SettingsJson : JsonSerializerContext
{
}

/// <summary>
/// Portable storage (SETUP-1): everything lives beside the executable, so moving the folder
/// carries the data with it. Nothing is written to the registry or %AppData%.
/// </summary>
public static class PortableStorage
{
    /// <summary>
    /// The folder holding the executable. Under a single-file publish this is the real app
    /// folder, not the temporary directory the runtime unpacks native libraries into.
    /// </summary>
    public static string Root { get; } = AppContext.BaseDirectory;

    public static string SettingsPath => Path.Combine(Root, "phonepads.settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable settings file should never stop the app starting.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Running from read-only media is not fatal; the session still works.
        }
    }
}
