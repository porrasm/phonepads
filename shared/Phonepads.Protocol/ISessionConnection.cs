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

    /// <summary>A player sent text from a text control: player id, control id, the whole text.</summary>
    event Action<string, string, string>? TextReceived;

    /// <summary>
    /// The service refused something the driver sent: code and message. Codes are a fixed set
    /// (invalid_state, cannot_start, profile_taken, profile_locked, unknown_schema,
    /// unknown_player) that may grow — treat unknown ones as generic. Not fatal: the session
    /// carries on.
    /// </summary>
    event Action<string, string>? ErrorReceived;

    event Action<ConnectionStatus>? StatusChanged;
    event Action<string>? Stopped;

    Task RunAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task ResumeAsync(CancellationToken ct);
    Task EndAsync(CancellationToken ct);

    /// <summary>Ends the round, not the session: back to the lobby with everyone's ready cleared.</summary>
    Task LobbyAsync(CancellationToken ct);

    /// <summary>Removes a player as if they had left. Not a ban — the join code still works for them.</summary>
    Task KickAsync(string playerId, CancellationToken ct);

    /// <summary>Puts one player (or everyone, with a null id) on one of the offered schemas.</summary>
    Task SetSchemaAsync(string? playerId, string schemaId, CancellationToken ct);

    Task VibrateAsync(string playerId, int milliseconds, CancellationToken ct);

    /// <summary>Shows a short line above one player's controls (or everyone's, with a null id).</summary>
    Task ShowTextAsync(string? playerId, string text, CancellationToken ct);
}
