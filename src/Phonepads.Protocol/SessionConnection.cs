using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;

namespace Phonepads.Protocol;

public enum ConnectionStatus
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Closed,
}

/// <summary>
/// The driver side of a claimed session: receives the snapshot and every event, sends the
/// lifecycle commands, and reconnects with backoff after a transient drop.
/// </summary>
public sealed class SessionConnection(Uri baseUri, string wsPath) : IAsyncDisposable
{
    /// <summary>Close codes the protocol defines as terminal — reconnecting would be pointless.</summary>
    private static readonly HashSet<int> TerminalCloseCodes = [4000, 4001, 4004, 4005, 4008, 4010];

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Idle;

    public event Action<SessionSnapshot>? SnapshotReceived;
    public event Action<string, string?>? StateChanged;
    public event Action<PlayerInfo>? PlayerChanged;
    public event Action<string>? PlayerLeft;
    public event Action<string, long, IReadOnlyDictionary<string, ControlValue>>? InputReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? Stopped;

    /// <summary>
    /// Connects and pumps messages until cancelled or until the session ends for good.
    /// Transient drops are retried with backoff; terminal close codes stop the loop.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            SetStatus(attempt == 0 ? ConnectionStatus.Connecting : ConnectionStatus.Reconnecting);

            try
            {
                var socket = new ClientWebSocket();
                await socket.ConnectAsync(SocketUri(), ct);
                _socket = socket;
                attempt = 0;
                SetStatus(ConnectionStatus.Connected);

                await ReceiveLoopAsync(socket, ct);

                var closeCode = (int?)socket.CloseStatus ?? 0;
                if (TerminalCloseCodes.Contains(closeCode))
                {
                    Stop(DescribeCloseCode(closeCode));
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException) when (!ct.IsCancellationRequested)
            {
                // Transient: fall through to the backoff below.
            }
            finally
            {
                _socket?.Dispose();
                _socket = null;
            }

            if (ct.IsCancellationRequested) break;

            // 1s, 2s, 4s … capped at 30s.
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));
            attempt++;
            SetStatus(ConnectionStatus.Reconnecting);
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetStatus(ConnectionStatus.Closed);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        // The service caps messages at 8 KB; this leaves headroom without growing.
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var text = new MemoryStream();
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    text.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                Dispatch(text.ToArray());
                text.SetLength(0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Dispatch(byte[] utf8)
    {
        ServerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(utf8, ProtocolJson.Default.ServerMessage);
        }
        catch (JsonException)
        {
            return; // A malformed frame is not worth tearing the session down for.
        }

        if (message?.Type is not { } type) return;

        switch (type)
        {
            case "snapshot":
                SnapshotReceived?.Invoke(SessionSnapshot.FromJson(message.Snapshot));
                break;

            case "state_changed":
                StateChanged?.Invoke(message.State ?? "unknown", message.Reason);
                break;

            case "player_joined":
            case "player_updated":
            case "player_connected":
            case "player_disconnected":
                if (PlayerInfo.FromJson(message.Player) is { } player)
                {
                    // The event name carries the connection state that the object may omit.
                    PlayerChanged?.Invoke(type switch
                    {
                        "player_connected" => player with { Connected = true },
                        "player_disconnected" => player with { Connected = false },
                        _ => player,
                    });
                }
                break;

            case "player_left":
                var leftId = message.PlayerId ?? PlayerInfo.FromJson(message.Player)?.Id;
                if (leftId is not null) PlayerLeft?.Invoke(leftId);
                break;

            case "input":
                if (message.PlayerId is { } inputPlayer)
                    InputReceived?.Invoke(inputPlayer, message.Seq, message.ReadControls());
                break;

            case "error":
                Stop(message.Message ?? message.Code ?? "The service reported an error.");
                break;
        }
    }

    public Task StartAsync(CancellationToken ct) => SendAsync(new DriverCommand { Type = "start" }, ct);

    public Task PauseAsync(CancellationToken ct) => SendAsync(new DriverCommand { Type = "pause" }, ct);

    public Task ResumeAsync(CancellationToken ct) => SendAsync(new DriverCommand { Type = "resume" }, ct);

    public Task EndAsync(CancellationToken ct) => SendAsync(new DriverCommand { Type = "end" }, ct);

    public Task VibrateAsync(string playerId, int milliseconds, CancellationToken ct) =>
        SendAsync(new DriverCommand
        {
            Type = "message",
            PlayerId = playerId,
            Payload = new MessagePayload { VibrateMs = milliseconds },
        }, ct);

    public async Task SendAsync(DriverCommand command, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open }) return;

        var json = JsonSerializer.SerializeToUtf8Bytes(command, ProtocolJson.Default.DriverCommand);
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (WebSocketException)
        {
            // The receive loop owns reconnection; the caller retries once it is back.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private Uri SocketUri()
    {
        var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        return new Uri(scheme + "://" + baseUri.Authority + wsPath);
    }

    private static string DescribeCloseCode(int code) => code switch
    {
        4000 => "The service rejected the connection as a bad request.",
        4001 => "This driver token is no longer authorised.",
        4004 => "This session no longer exists. You will need a new setup code.",
        4005 => "The session was ended.",
        4008 => "The service is rate limiting this connection.",
        4010 => "Another app connected as the driver for this session.",
        _ => "The connection closed.",
    };

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void Stop(string reason)
    {
        SetStatus(ConnectionStatus.Closed);
        Stopped?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        var socket = _socket;
        if (socket is { State: WebSocketState.Open })
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Closing cleanly is best-effort.
            }
        }

        socket?.Dispose();
        _sendLock.Dispose();
    }
}
