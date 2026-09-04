using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonepads.Protocol;

// ---- Setup (HTTP) ----

public sealed class SetupRequest
{
    [JsonPropertyName("setupCode")] public required string SetupCode { get; init; }
    [JsonPropertyName("config")] public SessionConfig? Config { get; init; }
}

public sealed class SessionConfig
{
    [JsonPropertyName("game")] public string? Game { get; init; }
    [JsonPropertyName("minPlayers")] public int? MinPlayers { get; init; }
    [JsonPropertyName("maxPlayers")] public int? MaxPlayers { get; init; }
    [JsonPropertyName("schemas")] public List<SchemaDto>? Schemas { get; init; }
    [JsonPropertyName("colors")] public List<string>? Colors { get; init; }
}

public sealed class SchemaDto
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("orientation")] public string? Orientation { get; init; }
    [JsonPropertyName("controls")] public required List<ControlDto> Controls { get; init; }
}

public sealed class ControlDto
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    [JsonPropertyName("zone")] public string? Zone { get; init; }
    [JsonPropertyName("size")] public string? Size { get; init; }
    [JsonPropertyName("range")] public int? Range { get; init; }
}

public sealed class SetupResponse
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; init; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
    [JsonPropertyName("joinCode")] public string? JoinCode { get; init; }
    [JsonPropertyName("joinUrl")] public string? JoinUrl { get; init; }
    [JsonPropertyName("driverToken")] public string? DriverToken { get; init; }
    [JsonPropertyName("wsPath")] public string? WsPath { get; init; }
    [JsonPropertyName("metadata")] public string? Metadata { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

// ---- Session (WebSocket) ----

/// <summary>
/// A server message, parsed only as far as its discriminator. Payload shapes differ per
/// type and the protocol is still in beta, so the rest is read lazily from <see cref="Raw"/>.
/// </summary>
public sealed class ServerMessage
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("playerId")] public string? PlayerId { get; init; }
    [JsonPropertyName("seq")] public long Seq { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("snapshot")] public JsonElement Snapshot { get; init; }
    [JsonPropertyName("player")] public JsonElement Player { get; init; }
    [JsonPropertyName("controls")] public JsonElement Controls { get; init; }

    /// <summary>Control values of an input frame, decoded from their three JSON shapes.</summary>
    public Dictionary<string, ControlValue> ReadControls()
    {
        var result = new Dictionary<string, ControlValue>(StringComparer.Ordinal);
        if (Controls.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in Controls.EnumerateObject())
            result[property.Name] = ControlValue.FromJson(property.Value);
        return result;
    }
}

/// <summary>A player as it appears in a snapshot or a player_* event.</summary>
public sealed record PlayerInfo(
    string Id,
    string Name,
    string Color,
    bool Ready,
    string? SchemaId,
    bool Connected)
{
    /// <summary>
    /// Reads a player object defensively: the service is in beta and spells the schema
    /// and connection fields more than one way across its docs.
    /// </summary>
    public static PlayerInfo? FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var id = Str(element, "id") ?? Str(element, "playerId");
        if (id is null) return null;
        return new PlayerInfo(
            id,
            Str(element, "name") ?? "Player",
            Str(element, "color") ?? "#888888",
            Bool(element, "ready") ?? false,
            Str(element, "schemaId") ?? Str(element, "schema"),
            Bool(element, "connected") ?? true);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}

/// <summary>The session snapshot sent on connect and on every reconnect.</summary>
public sealed record SessionSnapshot(string State, IReadOnlyList<PlayerInfo> Players)
{
    public static SessionSnapshot FromJson(JsonElement element)
    {
        var state = element.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? "unknown"
            : "unknown";

        var players = new List<PlayerInfo>();
        if (element.TryGetProperty("players", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (PlayerInfo.FromJson(item) is { } player) players.Add(player);
            }
        }

        return new SessionSnapshot(state, players);
    }
}

/// <summary>Commands the driver sends. Only the driver may start, pause, resume and end.</summary>
public sealed class DriverCommand
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("playerId")] public string? PlayerId { get; init; }
    [JsonPropertyName("payload")] public MessagePayload? Payload { get; init; }
}

/// <summary>
/// Payload of a "message" command. Only the vibrate form is modelled; the service passes
/// any other payload through to the player client untouched.
/// </summary>
public sealed class MessagePayload
{
    [JsonPropertyName("vibrateMs")] public int? VibrateMs { get; init; }
}
