using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonepads.Protocol;

// ---- Setup (HTTP) ----

public sealed class SetupRequest
{
    [JsonPropertyName("setupCode")] public required string SetupCode { get; init; }
    [JsonPropertyName("protocolVersion")] public int? ProtocolVersion { get; init; }
    [JsonPropertyName("config")] public SessionConfig? Config { get; init; }
}

/// <summary>Body of <c>POST /api/gamepad/driver/create</c>, authenticated with a driver key.</summary>
public sealed class CreateRequest
{
    [JsonPropertyName("config")] public SessionConfig? Config { get; init; }
    [JsonPropertyName("protocolVersion")] public int? ProtocolVersion { get; init; }

    /// <summary>Ends this key's previous session first instead of failing with 409.</summary>
    [JsonPropertyName("replaceExisting")] public bool ReplaceExisting { get; init; }
}

public sealed class SessionConfig
{
    [JsonPropertyName("game")] public string? Game { get; init; }

    /// <summary>
    /// A UUID generated once and shipped with the game. Phones file the layouts players edit
    /// under it, so the same value on every setup makes those edits survive between sessions.
    /// </summary>
    [JsonPropertyName("driverAppUuid")] public string? DriverAppUuid { get; init; }

    [JsonPropertyName("minPlayers")] public int? MinPlayers { get; init; }
    [JsonPropertyName("maxPlayers")] public int? MaxPlayers { get; init; }

    /// <summary>Adds the "Real gamepad" option: a controller paired with the phone, relayed as-is.</summary>
    [JsonPropertyName("allowPhysicalGamepad")] public bool? AllowPhysicalGamepad { get; init; }

    /// <summary>Lets players join while the game runs or is paused.</summary>
    [JsonPropertyName("allowLateJoin")] public bool? AllowLateJoin { get; init; }

    [JsonPropertyName("schemas")] public List<SchemaDto>? Schemas { get; init; }
    [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; init; }
    [JsonPropertyName("colors")] public List<string>? Colors { get; init; }

    /// <summary>Invite-only: the join code stops working and only the owner (and linked emails) can join.</summary>
    [JsonPropertyName("private")] public bool? Private { get; init; }

    /// <summary>No rounds: start is accepted with nobody ready, and joining stays open while running.</summary>
    [JsonPropertyName("skipLobby")] public bool? SkipLobby { get; init; }
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

    /// <summary>Buttons and text: "round" (default) or "rect".</summary>
    [JsonPropertyName("shape")] public string? Shape { get; init; }

    /// <summary>Touchpad: width / height, 0.25–4.</summary>
    [JsonPropertyName("aspect")] public double? Aspect { get; init; }

    /// <summary>Text: longest text the player can send, 1–1000.</summary>
    [JsonPropertyName("maxLength")] public int? MaxLength { get; init; }

    /// <summary>
    /// Buttons, joysticks, text and touchpads: exact centre as a percentage (0–100) of the
    /// controller's width, from the left. The phone still picks the size.
    /// </summary>
    [JsonPropertyName("x")] public double? X { get; init; }

    /// <summary>Exact centre as a percentage (0–100) of the controller's height, from the top.</summary>
    [JsonPropertyName("y")] public double? Y { get; init; }
}

public sealed class SetupResponse
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; init; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; init; }
    [JsonPropertyName("joinCode")] public string? JoinCode { get; init; }
    [JsonPropertyName("joinUrl")] public string? JoinUrl { get; init; }
    [JsonPropertyName("driverToken")] public string? DriverToken { get; init; }

    /// <summary>The absolute WebSocket address to connect to. Preferred over <see cref="WsPath"/>.</summary>
    [JsonPropertyName("wsUrl")] public string? WsUrl { get; init; }

    /// <summary>The same address relative to the service host, for a driver with its own base URL.</summary>
    [JsonPropertyName("wsPath")] public string? WsPath { get; init; }

    [JsonPropertyName("metadata")] public string? Metadata { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>The protocol versions the server speaks, sent with an "Unsupported protocol version" refusal.</summary>
    [JsonPropertyName("supported")] public List<int>? Supported { get; init; }

    /// <summary>
    /// Where to open the session socket: <see cref="WsUrl"/> when the service sent one, else
    /// <see cref="WsPath"/> against the base the setup call went to. Null when neither is usable.
    /// </summary>
    public Uri? SocketUri(Uri baseUri)
    {
        if (!string.IsNullOrEmpty(WsUrl) && Uri.TryCreate(WsUrl, UriKind.Absolute, out var absolute))
            return absolute;
        if (string.IsNullOrEmpty(WsPath)) return null;

        var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        return new Uri(scheme + "://" + baseUri.Authority + WsPath);
    }
}

// ---- Session (WebSocket) ----

/// <summary>
/// A server message, parsed only as far as its discriminator plus the flat fields the event
/// types share. Payload shapes differ per type, so the nested parts stay raw JSON and are
/// read lazily. Unknown fields and types are additions, never errors, and are ignored.
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
    [JsonPropertyName("controlId")] public string? ControlId { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("snapshot")] public JsonElement Snapshot { get; init; }
    [JsonPropertyName("player")] public JsonElement Player { get; init; }
    [JsonPropertyName("controls")] public JsonElement Controls { get; init; }
    [JsonPropertyName("samples")] public JsonElement Samples { get; init; }

    /// <summary>Control values of an input frame, decoded by shape rather than by id.</summary>
    public Dictionary<string, ControlValue> ReadControls()
    {
        var result = new Dictionary<string, ControlValue>(StringComparer.Ordinal);
        if (Controls.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in Controls.EnumerateObject())
            result[property.Name] = ControlValue.FromJson(property.Value);
        return result;
    }

    /// <summary>
    /// Samples of a "motion" message, oldest first. Rows that are not seven numbers are
    /// skipped rather than failing the whole message.
    /// </summary>
    public List<MotionSample> ReadMotionSamples()
    {
        var result = new List<MotionSample>();
        if (Samples.ValueKind != JsonValueKind.Array) return result;

        foreach (var row in Samples.EnumerateArray())
        {
            if (MotionSample.TryFromJson(row, out var sample)) result.Add(sample);
        }

        return result;
    }
}

/// <summary>
/// One raw inertial sample from the phone: <c>[t, ax, ay, az, gx, gy, gz]</c> on the wire.
/// Axes are the device frame — x to the right of the screen in portrait, y toward the top
/// edge, z out of the screen toward the player — never the screen's.
/// </summary>
public readonly record struct MotionSample(
    /// <summary>The phone's monotonic sample time in milliseconds. Arbitrary origin; use differences.</summary>
    double Time,
    /// <summary>Acceleration including gravity, in g, as the reaction force: flat and still reads (0, 0, +1).</summary>
    double AccelX,
    double AccelY,
    double AccelZ,
    /// <summary>Angular rate about the same axes in degrees per second, right-hand rule.</summary>
    double GyroX,
    double GyroY,
    double GyroZ)
{
    public static bool TryFromJson(JsonElement row, out MotionSample sample)
    {
        sample = default;
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 7) return false;

        Span<double> values = stackalloc double[7];
        var index = 0;
        foreach (var cell in row.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.Number) return false;
            values[index++] = cell.GetDouble();
        }

        sample = new MotionSample(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
        return true;
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
    /// Reads a player object, tolerating missing fields: a player_* event may carry less
    /// than the snapshot does, and later protocol versions may add fields.
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

/// <summary>The session snapshot sent on connect and on every reconnect. Authoritative; events patch it.</summary>
public sealed record SessionSnapshot(
    string State,
    IReadOnlyList<PlayerInfo> Players,
    int ProtocolVersion = 0,
    bool DriverConnected = true)
{
    public static SessionSnapshot FromJson(JsonElement element)
    {
        var state = element.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? "unknown"
            : "unknown";

        var version = element.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : 0;

        var driverConnected = !element.TryGetProperty("driverConnected", out var d)
            || d.ValueKind != JsonValueKind.False;

        var players = new List<PlayerInfo>();
        if (element.TryGetProperty("players", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (PlayerInfo.FromJson(item) is { } player) players.Add(player);
            }
        }

        return new SessionSnapshot(state, players, version, driverConnected);
    }
}

/// <summary>
/// Commands the driver sends: start, pause, resume, end, lobby, ping, and the targeted ones —
/// message (payload), set_schema (schemaId) and kick — which name one player or, for the
/// first two, omit playerId to reach everyone.
/// </summary>
public sealed class DriverCommand
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("playerId")] public string? PlayerId { get; init; }
    [JsonPropertyName("schemaId")] public string? SchemaId { get; init; }
    [JsonPropertyName("payload")] public MessagePayload? Payload { get; init; }
}

/// <summary>
/// Payload of a "message" command: the two shapes the phone understands. Both are optional
/// and combinable; the service relays anything else untouched.
/// </summary>
public sealed class MessagePayload
{
    /// <summary>Buzz the phone (the phone caps it at one second). A bridged real controller rumbles instead.</summary>
    [JsonPropertyName("vibrateMs")] public int? VibrateMs { get; init; }

    /// <summary>A line shown above the controls for a few seconds; up to 64 characters (it expires on its own).</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }
}
