using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonepads.Protocol;

/// <summary>
/// Source-generated serialisation for every wire type. Reflection-based serialisation does
/// not survive trimming, and the app ships as a trimmed single file.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SetupRequest))]
[JsonSerializable(typeof(SetupResponse))]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(DriverCommand))]
public partial class ProtocolJson : JsonSerializerContext
{
}
