using Phonepads.Protocol;

namespace Phonepads.Core;

public enum SessionPhase
{
    Idle,
    Claiming,
    Lobby,
    Running,
    Paused,
    Ended,
}

/// <summary>A player in the session together with the pad slot they drive and what kind of pad it is.</summary>
public sealed record SessionPlayer(PlayerInfo Info, int Slot, PadBackend Backend)
{
    /// <summary>Slot given to players beyond the fourth, who get no pad at all (PLAY-2).</summary>
    public const int Unassigned = -1;

    public bool HasPad => Slot != Unassigned;
}

/// <summary>
/// Runs one session end to end: claims it, tracks the lobby, and turns input frames and
/// motion samples into virtual pads. Owns the pads, so closing it leaves nothing behind (PLAY-5).
/// </summary>
public sealed class SessionManager : IAsyncDisposable
{
    /// <summary>Windows accepts at most four XInput controllers, whatever the service allows.</summary>
    public const int MaxPads = 4;

    private readonly IReadOnlyDictionary<PadBackend, IVirtualPadHub> _hubs;
    private readonly Func<Uri, string, ISessionConnection> _connect;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionPlayer> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _latestSeq = new(StringComparer.Ordinal);
    private readonly Dictionary<int, IVirtualPad> _pads = [];
    private readonly HashSet<PadBackend> _reportedMissing = [];

    private IReadOnlyList<MappedSchema> _offered = [];
    private ISessionConnection? _connection;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    public SessionManager(IReadOnlyDictionary<PadBackend, IVirtualPadHub> hubs)
        : this(hubs, (baseUri, wsPath) => new SessionConnection(baseUri, wsPath))
    {
    }

    public SessionManager(
        IReadOnlyDictionary<PadBackend, IVirtualPadHub> hubs,
        Func<Uri, string, ISessionConnection> connectionFactory)
    {
        _hubs = hubs;
        _connect = connectionFactory;
    }

    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    public string? JoinCode { get; private set; }

    public string? JoinUrl { get; private set; }

    public bool RumbleEnabled { get; set; } = true;

    public event Action<SessionPhase>? PhaseChanged;
    public event Action<IReadOnlyList<SessionPlayer>>? PlayersChanged;
    public event Action<ConnectionStatus>? ConnectionChanged;
    public event Action<string>? Notice;

    /// <summary>Raised for every applied input frame, so the UI can show what the game sees (MAP-4).</summary>
    public event Action<string, PadState>? PadUpdated;

    /// <summary>Raised with the newest sample of every motion batch that reached a pad.</summary>
    public event Action<string, MotionSample>? MotionUpdated;

    public IReadOnlyList<SessionPlayer> Players
    {
        get
        {
            lock (_gate) return _players.Values.OrderBy(p => p.Slot < 0).ThenBy(p => p.Slot).ToList();
        }
    }

    /// <summary>
    /// Claims the session with a single-use setup code and starts the WebSocket pump.
    /// The offered schemas become the layouts players can choose from.
    /// </summary>
    public async Task<SetupResponse> ClaimAsync(
        DriverClient client,
        string setupCode,
        IReadOnlyList<MappedSchema> offered,
        string? gameName,
        int minPlayers,
        int maxPlayers,
        CancellationToken ct)
    {
        ValidateOffer(offered);
        SetPhase(SessionPhase.Claiming);

        var config = new SessionConfig
        {
            Game = string.IsNullOrWhiteSpace(gameName) ? null : gameName,
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            Schemas = offered.Select(s => s.Schema.ToDto()).ToList(),
        };

        SetupResponse response;
        try
        {
            response = await client.ClaimAsync(setupCode, config, ct);
        }
        catch
        {
            SetPhase(SessionPhase.Idle);
            throw;
        }

        Attach(client.BaseUri, response, offered);
        return response;
    }

    /// <summary>
    /// Connects to an already claimed session. <see cref="ClaimAsync"/> calls this after the
    /// setup call; tests call it directly with a fake connection.
    /// </summary>
    public void Attach(Uri baseUri, SetupResponse response, IReadOnlyList<MappedSchema> offered)
    {
        ValidateOffer(offered);

        _offered = offered;
        JoinCode = response.JoinCode;
        JoinUrl = response.JoinUrl;

        var connection = _connect(baseUri, response.WsPath ?? string.Empty);
        connection.SnapshotReceived += OnSnapshot;
        connection.StateChanged += OnStateChanged;
        connection.PlayerChanged += OnPlayerChanged;
        connection.PlayerLeft += OnPlayerLeft;
        connection.InputReceived += OnInput;
        connection.MotionReceived += OnMotion;
        connection.StatusChanged += status => ConnectionChanged?.Invoke(status);
        connection.Stopped += reason =>
        {
            Notice?.Invoke(reason);
            ReleaseAllPads();
            SetPhase(SessionPhase.Ended);
        };

        _connection = connection;
        _cts = new CancellationTokenSource();
        _pump = connection.RunAsync(_cts.Token);

        SetPhase(SessionPhase.Lobby);
    }

    private static void ValidateOffer(IReadOnlyList<MappedSchema> offered)
    {
        if (offered.Count is < 1 or > 4)
            throw new ArgumentException("A session offers 1 to 4 schemas.", nameof(offered));

        var problems = offered.SelectMany(s => s.Schema.Validate()).ToList();
        if (problems.Count > 0)
            throw new SetupException(string.Join(" ", problems));
    }

    /// <summary>Creates the pads for assigned players and tells the service to begin (PLAY-3).</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_connection is null) return;

        int created;
        lock (_gate)
        {
            created = _players.Values.Count(p => p.HasPad && EnsurePad(p));
        }

        if (created == 0 && _players.Values.Any(p => p.HasPad))
        {
            Notice?.Invoke("No controller could be created for any player, so the game was not started.");
            return;
        }

        await _connection.StartAsync(ct);
        SetPhase(SessionPhase.Running);
    }

    /// <summary>Stops input reaching the game and releases every pad to neutral (PLAY-4).</summary>
    public async Task PauseAsync(CancellationToken ct)
    {
        if (_connection is null) return;
        await _connection.PauseAsync(ct);
        ReleaseAllPads();
        SetPhase(SessionPhase.Paused);
    }

    public async Task ResumeAsync(CancellationToken ct)
    {
        if (_connection is null) return;

        // Players may have switched schema while paused; their pad may need to change kind.
        lock (_gate)
        {
            foreach (var player in _players.Values.Where(p => p.HasPad)) EnsurePad(player);
        }

        await _connection.ResumeAsync(ct);
        SetPhase(SessionPhase.Running);
    }

    /// <summary>Ends the session and removes every pad (PLAY-5).</summary>
    public async Task EndAsync(CancellationToken ct)
    {
        if (_connection is not null) await _connection.EndAsync(ct);
        DisposePads();
        SetPhase(SessionPhase.Ended);
    }

    private void OnSnapshot(SessionSnapshot snapshot)
    {
        lock (_gate)
        {
            // Keep the slots already handed out; the snapshot is authoritative about who is here.
            var known = snapshot.Players.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var goneId in _players.Keys.Where(id => !known.Contains(id)).ToList())
                Release(goneId);

            foreach (var player in snapshot.Players) Upsert(player);
        }

        SetPhase(snapshot.State switch
        {
            "waiting_for_players" => SessionPhase.Lobby,
            "in_progress" => SessionPhase.Running,
            "paused" => SessionPhase.Paused,
            "ended" => SessionPhase.Ended,
            _ => Phase,
        });

        RaisePlayersChanged();
    }

    private void OnStateChanged(string state, string? reason)
    {
        switch (state)
        {
            case "paused":
                ReleaseAllPads();
                SetPhase(SessionPhase.Paused);
                if (reason == "driver_disconnected")
                    Notice?.Invoke("The connection dropped, so the session auto-paused. Resume when you are ready.");
                break;

            case "in_progress":
                SetPhase(SessionPhase.Running);
                break;

            case "ended":
                DisposePads();
                SetPhase(SessionPhase.Ended);
                Notice?.Invoke(reason switch
                {
                    null => "The session ended.",
                    "driver_lost" => "The service closed the session because this app was away for too long. Claim a new one.",
                    _ => $"The session ended ({reason}).",
                });
                break;

            case "waiting_for_players":
                SetPhase(SessionPhase.Lobby);
                break;
        }
    }

    private void OnPlayerChanged(PlayerInfo info)
    {
        lock (_gate)
        {
            Upsert(info);

            if (!_players.TryGetValue(info.Id, out var player) || !player.HasPad) return;

            // A player who drops keeps their slot; their pad just goes quiet (PLAY-6).
            if (!info.Connected && _pads.TryGetValue(player.Slot, out var pad))
                pad.Reset();

            // Switching schema mid-session may mean a different kind of pad.
            if (Phase is SessionPhase.Running or SessionPhase.Paused) EnsurePad(player);
        }

        RaisePlayersChanged();
    }

    private void OnPlayerLeft(string playerId)
    {
        lock (_gate) Release(playerId);
        RaisePlayersChanged();
    }

    private void OnInput(string playerId, long seq, IReadOnlyDictionary<string, ControlValue> controls)
    {
        PadState state;
        IVirtualPad? pad;

        lock (_gate)
        {
            // Frames can overtake each other; keep only the newest per player.
            if (_latestSeq.TryGetValue(playerId, out var last) && seq <= last) return;
            _latestSeq[playerId] = seq;

            if (Phase != SessionPhase.Running) return;
            if (!_players.TryGetValue(playerId, out var player) || !player.HasPad) return;
            if (!_pads.TryGetValue(player.Slot, out pad)) return;

            var mapped = SchemaFor(player.Info.SchemaId);
            state = MappingEngine.Apply(mapped.Schema, mapped.Mapping, controls);
        }

        pad.Update(state);
        PadUpdated?.Invoke(playerId, state);
    }

    private void OnMotion(string playerId, IReadOnlyList<MotionSample> samples)
    {
        if (samples.Count == 0) return;

        IVirtualPad? pad;
        lock (_gate)
        {
            if (Phase != SessionPhase.Running) return;
            if (!_players.TryGetValue(playerId, out var player) || !player.HasPad) return;
            if (!_pads.TryGetValue(player.Slot, out pad)) return;
        }

        // The stream has no sequence numbers and nothing is deduplicated: every sample counts.
        pad.PushMotion(samples is MotionSample[] array ? array : samples.ToArray());
        MotionUpdated?.Invoke(playerId, samples[^1]);
    }

    private MappedSchema SchemaFor(string? schemaId) =>
        _offered.FirstOrDefault(s => string.Equals(s.Schema.Id, schemaId, StringComparison.Ordinal))
        ?? _offered[0];

    /// <summary>Adds or refreshes a player, assigning the next free pad slot on first sight.</summary>
    private void Upsert(PlayerInfo info)
    {
        var backend = SchemaFor(info.SchemaId).Mapping.Backend;

        if (_players.TryGetValue(info.Id, out var existing))
        {
            _players[info.Id] = existing with { Info = info, Backend = backend };
            return;
        }

        _players[info.Id] = new SessionPlayer(info, NextFreeSlot(), backend);
    }

    private int NextFreeSlot()
    {
        var taken = _players.Values.Where(p => p.HasPad).Select(p => p.Slot).ToHashSet();
        for (var slot = 0; slot < MaxPads; slot++)
        {
            if (!taken.Contains(slot)) return slot;
        }

        return SessionPlayer.Unassigned;
    }

    /// <summary>Removes a player for good, freeing their slot for someone waiting (PLAY-6).</summary>
    private void Release(string playerId)
    {
        if (!_players.Remove(playerId, out var player)) return;
        _latestSeq.Remove(playerId);

        if (player.HasPad && _pads.Remove(player.Slot, out var pad))
        {
            pad.Reset();
            pad.Dispose();
        }
    }

    /// <summary>
    /// Makes sure the player's slot holds a pad of the player's backend. Returns false when
    /// that backend is unavailable on this machine; the reason is reported once.
    /// </summary>
    private bool EnsurePad(SessionPlayer player)
    {
        if (_pads.TryGetValue(player.Slot, out var existing))
        {
            if (existing.Backend == player.Backend) return true;

            existing.Reset();
            existing.Dispose();
            _pads.Remove(player.Slot);
        }

        if (!_hubs.TryGetValue(player.Backend, out var hub) || !hub.IsAvailable)
        {
            if (_reportedMissing.Add(player.Backend))
            {
                Notice?.Invoke(hub?.UnavailableReason
                    ?? $"No {PadBackendInfo.Label(player.Backend)} backend is available on this machine.");
            }

            return false;
        }

        var pad = hub.Create(player.Slot);
        pad.RumbleChanged += (large, small) => OnRumble(player.Slot, large, small);
        _pads[player.Slot] = pad;
        return true;
    }

    private void OnRumble(int slot, byte large, byte small)
    {
        if (!RumbleEnabled) return;

        string? playerId;
        lock (_gate)
        {
            playerId = _players.Values.FirstOrDefault(p => p.Slot == slot)?.Info.Id;
        }

        if (playerId is null || _connection is null) return;

        var strength = Math.Max(large, small);
        if (strength == 0) return;

        // Scale the motor strength into a short buzz the phone can actually render.
        var milliseconds = 40 + (int)(strength / 255d * 160);
        _ = _connection.VibrateAsync(playerId, milliseconds, CancellationToken.None);
    }

    private void ReleaseAllPads()
    {
        lock (_gate)
        {
            foreach (var pad in _pads.Values) pad.Reset();
        }
    }

    private void DisposePads()
    {
        lock (_gate)
        {
            foreach (var pad in _pads.Values)
            {
                pad.Reset();
                pad.Dispose();
            }

            _pads.Clear();
        }
    }

    private void SetPhase(SessionPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    private void RaisePlayersChanged() => PlayersChanged?.Invoke(Players);

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null) await _cts.CancelAsync();

        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        if (_connection is not null) await _connection.DisposeAsync();
        _cts?.Dispose();

        // The hubs outlive the session: the next session reuses them.
        DisposePads();
    }
}
