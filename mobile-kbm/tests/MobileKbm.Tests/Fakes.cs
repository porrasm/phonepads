using System.Threading.Channels;
using MobileKbm.Core;
using Phonepads.Protocol;

namespace MobileKbm.Tests;

/// <summary>Records every call as a short line, so tests can assert on the exact sequence.</summary>
public sealed class RecordingSink : IInputSink
{
    public List<string> Events { get; } = [];

    public int ScreenHeight => 1000;

    public void MoveMouse(int dx, int dy) => Events.Add($"move {dx},{dy}");

    public void SetMouseButton(MouseButton button, bool down) => Events.Add($"{(down ? "down" : "up")} {button}");

    public void Wheel(int delta, bool horizontal) => Events.Add($"{(horizontal ? "hwheel" : "wheel")} {delta}");

    public void SetKey(Key key, bool down) => Events.Add($"{(down ? "key+" : "key-")} {key}");

    public void TypeText(string text) => Events.Add($"text {text}");

    public List<string> Without(string prefix) => Events.Where(e => !e.StartsWith(prefix, StringComparison.Ordinal)).ToList();
}

/// <summary>A session connection that stays "connected" until cancelled, driven by the test.</summary>
public sealed class FakeConnection(Action<FakeConnection> onRunning) : ISessionConnection
{
    public List<string> Commands { get; } = [];

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Idle;

    public event Action<SessionSnapshot>? SnapshotReceived;
    public event Action<string, string?>? StateChanged;
    public event Action<PlayerInfo>? PlayerChanged;
    public event Action<string>? PlayerLeft;
    public event Action<string, long, IReadOnlyDictionary<string, ControlValue>>? InputReceived;
    public event Action<string, IReadOnlyList<MotionSample>>? MotionReceived;
    public event Action<string, string, string>? TextReceived;
    public event Action<string, string>? ErrorReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? Stopped;

    public async Task RunAsync(CancellationToken ct)
    {
        Status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(Status);
        onRunning(this);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ct.Register(() => done.TrySetResult()))
        {
            await done.Task;
        }

        Status = ConnectionStatus.Closed;
    }

    public Task StartAsync(CancellationToken ct) => Record("start");

    public Task PauseAsync(CancellationToken ct) => Record("pause");

    public Task ResumeAsync(CancellationToken ct) => Record("resume");

    public Task EndAsync(CancellationToken ct) => Record("end");

    public Task VibrateAsync(string playerId, int milliseconds, CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseSnapshot(string state, params PlayerInfo[] players) =>
        SnapshotReceived?.Invoke(new SessionSnapshot(state, players));

    public void RaiseStateChanged(string state, string? reason = null) => StateChanged?.Invoke(state, reason);

    public void RaisePlayerChanged(PlayerInfo player) => PlayerChanged?.Invoke(player);

    public void RaiseInput(string playerId, long seq, Dictionary<string, ControlValue> controls) =>
        InputReceived?.Invoke(playerId, seq, controls);

    public void RaiseText(string playerId, string controlId, string text) =>
        TextReceived?.Invoke(playerId, controlId, text);

    public void RaiseStopped(string reason) => Stopped?.Invoke(reason);

    // Unused by the keeper, declared for the interface.
    public void Unused()
    {
        PlayerLeft?.Invoke("");
        MotionReceived?.Invoke("", []);
        ErrorReceived?.Invoke("", "");
    }

    private Task Record(string command)
    {
        lock (Commands) Commands.Add(command);
        return Task.CompletedTask;
    }
}

/// <summary>The service, as far as the keeper sees it.</summary>
public sealed class FakeDriverApi : IDriverApi
{
    private readonly Channel<FakeConnection> _running = Channel.CreateUnbounded<FakeConnection>();

    public List<string> KeysUsed { get; } = [];

    /// <summary>When set, the next create fails with this instead of succeeding.</summary>
    public Exception? FailNext { get; set; }

    public Task<SetupResponse> CreateAsync(string driverKey, SessionConfig config, CancellationToken ct)
    {
        lock (KeysUsed) KeysUsed.Add(driverKey);

        if (FailNext is { } failure)
        {
            FailNext = null;
            return Task.FromException<SetupResponse>(failure);
        }

        return Task.FromResult(new SetupResponse
        {
            Success = true,
            DriverToken = "token",
            WsPath = "/ws",
            JoinUrl = "https://example.test/join",
        });
    }

    public ISessionConnection Connect(SetupResponse session) =>
        new FakeConnection(connection => _running.Writer.TryWrite(connection));

    /// <summary>The next connection once it is running, with the keeper's handlers attached.</summary>
    public async Task<FakeConnection> NextConnectionAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _running.Reader.ReadAsync(timeout.Token);
    }
}

public static class Wait
{
    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition never became true.");
            await Task.Delay(10);
        }
    }
}
