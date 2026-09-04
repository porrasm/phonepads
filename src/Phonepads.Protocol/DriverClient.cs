using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Phonepads.Protocol;

/// <summary>Raised when the service refuses a setup code, with a message fit to show a user.</summary>
public sealed class SetupException(string message) : Exception(message);

/// <summary>
/// Claims a session by exchanging a single-use setup code for a driver token
/// (step 1 of the driver flow).
/// </summary>
public sealed class DriverClient(HttpClient http, Uri baseUri)
{
    public const string DefaultBaseUrl = "https://gamepad.porras.club";

    private readonly HttpClient _http = http;

    public Uri BaseUri { get; } = baseUri;

    public DriverClient() : this(new HttpClient(), new Uri(DefaultBaseUrl)) { }

    public async Task<SetupResponse> ClaimAsync(
        string setupCode,
        SessionConfig? config,
        CancellationToken ct)
    {
        var request = new SetupRequest { SetupCode = setupCode.Trim().ToUpperInvariant(), Config = config };
        var json = JsonSerializer.Serialize(request, ProtocolJson.Default.SetupRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(
                new Uri(BaseUri, "/api/gamepad/driver/setup"), content, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SetupException(
                "Could not reach the Gamepad service. Check your internet connection. " + ex.Message);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            SetupResponse? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize(body, ProtocolJson.Default.SetupResponse);
            }
            catch (JsonException)
            {
                // Fall through to the status-based message below.
            }

            if (response.IsSuccessStatusCode && parsed is { Success: true })
            {
                if (string.IsNullOrEmpty(parsed.DriverToken) || string.IsNullOrEmpty(parsed.WsPath))
                    throw new SetupException("The service accepted the code but returned no session to connect to.");
                return parsed;
            }

            throw new SetupException(Explain(response.StatusCode, parsed?.Error));
        }
    }

    private static string Explain(HttpStatusCode status, string? error) => status switch
    {
        HttpStatusCode.NotFound =>
            "That setup code is not valid, or it has already been used. Each code works once.",
        HttpStatusCode.Conflict =>
            "That session has already been claimed by another app.",
        HttpStatusCode.TooManyRequests =>
            "Too many attempts. Wait a minute and try again.",
        HttpStatusCode.BadRequest =>
            "The service rejected the request: " + (error ?? "the code looks malformed."),
        _ => error ?? $"The service returned an unexpected error ({(int)status}).",
    };
}
