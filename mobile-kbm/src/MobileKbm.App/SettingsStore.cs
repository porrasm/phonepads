using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonepads.Protocol;

namespace MobileKbm.App;

/// <summary>Everything the app remembers: where the service is, the driver key, and whether it is paused.</summary>
internal sealed class KbmSettings
{
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = DriverClient.DefaultBaseUrl;

    /// <summary>
    /// The driver key, encrypted with DPAPI for the current Windows user. Anyone holding the
    /// plain key can open sessions in its owner's name, so it never touches disk readable.
    /// </summary>
    [JsonPropertyName("driverKey")] public string? ProtectedDriverKey { get; set; }

    [JsonPropertyName("paused")] public bool Paused { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KbmSettings))]
internal partial class KbmSettingsJson : JsonSerializerContext
{
}

/// <summary>
/// Portable like Phonepads: settings live beside the executable. The key inside is bound to
/// this Windows user on this PC — moving the folder to another PC means entering it again.
/// </summary>
internal static class SettingsStore
{
    private static readonly byte[] Entropy = "mobile-kbm driver key"u8.ToArray();

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "mobile-kbm.settings.json");

    public static KbmSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new KbmSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(Path), KbmSettingsJson.Default.KbmSettings)
                   ?? new KbmSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken settings file must never stop an always-on app from starting.
            return new KbmSettings();
        }
    }

    public static void Save(KbmSettings settings)
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, KbmSettingsJson.Default.KbmSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only folder: the app still works, it just forgets on restart.
        }
    }

    public static string? ReadKey(KbmSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedDriverKey)) return null;
        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(settings.ProtectedDriverKey), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Another user's or another PC's key: ask for it again.
            return null;
        }
    }

    public static void WriteKey(KbmSettings settings, string? key)
    {
        settings.ProtectedDriverKey = string.IsNullOrEmpty(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
    }
}
