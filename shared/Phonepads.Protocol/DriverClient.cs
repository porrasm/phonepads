using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Phonepads.Protocol;

/// <summary>
/// Raised when the service refuses to hand out a session, with a message fit to show a user.
/// <see cref="Status"/> is the HTTP status when the service answered at all; null means it
/// could not be reached.
/// </summary>
public sealed class SetupException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Gets a session for the driver (step 1 of the driver flow): either claims one with a
/// single-use setup code, or creates one with a driver key.
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
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "/api/gamepad/driver/setup"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return await SendAsync(message, ExplainSetup, ct);
    }

    /// <summary>
    /// Creates a session with a driver key — no setup code, no person in a browser. With
    /// <paramref name="replaceExisting"/> the key's previous session is ended first, which is
    /// what a driver restarting after a crash wants.
    /// </summary>
    public async Task<SetupResponse> CreateAsync(
        string driverKey,
        SessionConfig? config,
        bool replaceExisting,
        CancellationToken ct)
    {
        var request = new CreateRequest { Config = config, ProtocolVersion = 1, ReplaceExisting = replaceExisting };
        var json = JsonSerializer.Serialize(request, ProtocolJson.Default.CreateRequest);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "/api/gamepad/driver/create"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverKey.Trim());

        return await SendAsync(message, ExplainCreate, ct);
    }

    private async Task<SetupResponse> SendAsync(
        HttpRequestMessage request,
        Func<HttpStatusCode, string?, string> explain,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SetupException(
                "Could not reach the Gamepad service. Check your internet connection. " + ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            throw new SetupException("The Gamepad service did not answer in time. " + ex.Message);
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
                    throw new SetupException("The service accepted the request but returned no session to connect to.");
                return parsed;
            }

            throw new SetupException(explain(response.StatusCode, parsed?.Error), response.StatusCode);
        }
    }

    private static string ExplainCreate(HttpStatusCode status, string? error) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "The driver key was not accepted. It may have been revoked — issue a new one on the Gamepad website.",
        HttpStatusCode.Conflict =>
            "This driver key already has an active session.",
        HttpStatusCode.TooManyRequests =>
            "Too many attempts. Wait a minute and try again.",
        HttpStatusCode.BadRequest =>
            "The service rejected the request: " + (error ?? "the request looks malformed."),
        _ => error ?? $"The service returned an unexpected error ({(int)status}).",
    };

    private static string ExplainSetup(HttpStatusCode status, string? error) => status switch
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
