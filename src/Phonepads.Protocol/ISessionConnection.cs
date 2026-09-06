namespace Phonepads.Protocol;

/// <summary>
/// The driver's view of a live session. <see cref="SessionConnection"/> is the real thing;
/// the interface exists so session orchestration can be exercised without a socket.
/// </summary>
public interface ISessionConnection : IAsyncDisposable
{
    ConnectionStatus Status { get; }

    event Action<SessionSnapshot>? SnapshotReceived;
    event Action<string, string?>? StateChanged;
    event Action<PlayerInfo>? PlayerChanged;
    event Action<string>? PlayerLeft;
    event Action<string, long, IReadOnlyDictionary<string, ControlValue>>? InputReceived;

    /// <summary>Raw inertial samples for one player, oldest first. A stream, not state: every sample counts.</summary>
    event Action<string, IReadOnlyList<MotionSample>>? MotionReceived;

    event Action<ConnectionStatus>? StatusChanged;
    event Action<string>? Stopped;

    Task RunAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task ResumeAsync(CancellationToken ct);
    Task EndAsync(CancellationToken ct);
    Task VibrateAsync(string playerId, int milliseconds, CancellationToken ct);
}
