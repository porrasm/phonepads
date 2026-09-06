using Phonepads.Core;
using Phonepads.Protocol;

namespace Phonepads.Tests;

/// <summary>A session connection driven by the test instead of a socket.</summary>
public sealed class FakeConnection : ISessionConnection
{
    public List<string> Commands { get; } = [];

    public List<(string PlayerId, int Ms)> Vibrations { get; } = [];

    public bool Disposed { get; private set; }

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Idle;

    public event Action<SessionSnapshot>? SnapshotReceived;
    public event Action<string, string?>? StateChanged;
    public event Action<PlayerInfo>? PlayerChanged;
    public event Action<string>? PlayerLeft;
    public event Action<string, long, IReadOnlyDictionary<string, ControlValue>>? InputReceived;
    public event Action<string, IReadOnlyList<MotionSample>>? MotionReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? Stopped;

    public Task RunAsync(CancellationToken ct)
    {
        Status = ConnectionStatus.Connected;
        StatusChanged?.Invoke(Status);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct) => Record("start");

    public Task PauseAsync(CancellationToken ct) => Record("pause");

    public Task ResumeAsync(CancellationToken ct) => Record("resume");

    public Task EndAsync(CancellationToken ct) => Record("end");

    public Task VibrateAsync(string playerId, int milliseconds, CancellationToken ct)
    {
        Vibrations.Add((playerId, milliseconds));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    // ---- Test-side triggers ----

    public void RaiseSnapshot(string state, params PlayerInfo[] players) =>
        SnapshotReceived?.Invoke(new SessionSnapshot(state, players));

    public void RaiseStateChanged(string state, string? reason = null) => StateChanged?.Invoke(state, reason);

    public void RaisePlayerChanged(PlayerInfo player) => PlayerChanged?.Invoke(player);

    public void RaisePlayerLeft(string playerId) => PlayerLeft?.Invoke(playerId);

    public void RaiseInput(string playerId, long seq, Dictionary<string, ControlValue> controls) =>
        InputReceived?.Invoke(playerId, seq, controls);

    public void RaiseMotion(string playerId, params MotionSample[] samples) =>
        MotionReceived?.Invoke(playerId, samples);

    public void RaiseStopped(string reason) => Stopped?.Invoke(reason);

    private Task Record(string command)
    {
        Commands.Add(command);
        return Task.CompletedTask;
    }
}

/// <summary>A pad that remembers everything done to it.</summary>
public sealed class FakePad(int slot, PadBackend backend) : IVirtualPad
{
    public int Slot { get; } = slot;

    public PadBackend Backend { get; } = backend;

    public List<PadState> Updates { get; } = [];

    public List<MotionSample> Motion { get; } = [];

    public int Resets { get; private set; }

    public bool Disposed { get; private set; }

    public event Action<byte, byte>? RumbleChanged;

    public void Update(PadState state) => Updates.Add(state);

    public void PushMotion(ReadOnlySpan<MotionSample> samples) => Motion.AddRange(samples.ToArray());

    public void Reset() => Resets++;

    public void Dispose() => Disposed = true;

    public void Rumble(byte large, byte small) => RumbleChanged?.Invoke(large, small);
}

/// <summary>A hub whose pads are all <see cref="FakePad"/>s, optionally pretending to be absent.</summary>
public sealed class FakeHub(PadBackend backend, bool available = true) : IVirtualPadHub
{
    public PadBackend Backend { get; } = backend;

    public bool IsAvailable { get; } = available;

    public string? UnavailableReason => IsAvailable ? null : $"{Backend} is not installed here.";

    public List<FakePad> Created { get; } = [];

    public bool Disposed { get; private set; }

    public IVirtualPad Create(int slot)
    {
        var pad = new FakePad(slot, Backend);
        Created.Add(pad);
        return pad;
    }

    public void Dispose() => Disposed = true;

    /// <summary>The live (not disposed) pad at a slot, if any.</summary>
    public FakePad? At(int slot) => Created.LastOrDefault(p => p.Slot == slot && !p.Disposed);
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        _now += by;
        _ticks += by.Ticks;
    }
}
