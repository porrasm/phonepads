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

/// <summary>What the session tells the service about the game, beyond the layouts on offer.</summary>
public sealed record SessionOptions
{
    public string? GameName { get; init; }
    public int MinPlayers { get; init; } = 1;
    public int MaxPlayers { get; init; } = 4;

    /// <summary>Let players join while the game runs. A late joiner lands straight on the controller.</summary>
    public bool AllowLateJoin { get; init; }
}

/// <summary>
/// Runs one session end to end: claims it, tracks the lobby, and turns input frames and
/// motion samples into virtual pads. Owns the pads, so closing it leaves nothing behind (PLAY-5).
/// </summary>
public sealed class SessionManager : IAsyncDisposable
{
    /// <summary>Windows accepts at most four XInput controllers, whatever the service allows.</summary>
    public const int MaxPads = 4;

    /// <summary>The service accepts up to this many phone layouts per session.</summary>
    public const int MaxSchemas = 32;

    /// <summary>
    /// Phonepads' identity towards phones, generated once and shipped with the app. Phones file
    /// the layouts players edit under it, so a player's tweaks to a preset survive between
    /// sessions. The service never sees or interprets it beyond relaying it.
    /// </summary>
    public const string DriverAppUuid = "3f0c9a7e-5b21-4d8e-9a64-2c7b1e8f0d53";

    private readonly IReadOnlyDictionary<PadBackend, IVirtualPadHub> _hubs;
    private readonly Func<Uri, ISessionConnection> _connect;
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
        : this(hubs, socketUri => new SessionConnection(socketUri))
    {
    }

    public SessionManager(
        IReadOnlyDictionary<PadBackend, IVirtualPadHub> hubs,
        Func<Uri, ISessionConnection> connectionFactory)
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
    /// The offered schemas become the layouts players can choose from; offering
    /// <see cref="PhysicalGamepad.Mapped"/> among them adds the "Real gamepad" option.
    /// </summary>
    public Task<SetupResponse> ClaimAsync(
        DriverClient client,
        string setupCode,
        IReadOnlyList<MappedSchema> offered,
        SessionOptions options,
        CancellationToken ct) =>
        ObtainAsync(client, offered, options, config => client.ClaimAsync(setupCode, config, ct));

    /// <summary>
    /// Creates a session with a driver key instead of a setup code — no host in a browser
    /// needed. Otherwise identical to <see cref="ClaimAsync"/>.
    /// </summary>
    public Task<SetupResponse> CreateAsync(
        DriverClient client,
        string driverKey,
        bool replaceExisting,
        IReadOnlyList<MappedSchema> offered,
        SessionOptions options,
        CancellationToken ct) =>
        ObtainAsync(client, offered, options, config => client.CreateAsync(driverKey, config, replaceExisting, ct));

    private async Task<SetupResponse> ObtainAsync(
        DriverClient client,
        IReadOnlyList<MappedSchema> offered,
        SessionOptions options,
        Func<SessionConfig, Task<SetupResponse>> obtain)
    {
        ValidateOffer(offered);
        SetPhase(SessionPhase.Claiming);

        SetupResponse response;
        try
        {
            response = await obtain(BuildConfig(offered, options));
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
    /// The config the service is asked for: the phone layouts, the real-gamepad flag when
    /// that option is among the offer, and the app's identity so phones remember edits.
    /// </summary>
    public static SessionConfig BuildConfig(IReadOnlyList<MappedSchema> offered, SessionOptions options) => new()
    {
        Game = string.IsNullOrWhiteSpace(options.GameName) ? null : options.GameName,
        DriverAppUuid = DriverAppUuid,
        MinPlayers = options.MinPlayers,
        MaxPlayers = options.MaxPlayers,
        AllowPhysicalGamepad = offered.Any(s => s.Schema.IsPhysicalGamepad) ? true : null,
        AllowLateJoin = options.AllowLateJoin ? true : null,
        Schemas = offered.Where(s => !s.Schema.IsPhysicalGamepad).Select(s => s.Schema.ToDto()).ToList(),
    };

    /// <summary>
    /// Connects to an already obtained session. <see cref="ClaimAsync"/> and
    /// <see cref="CreateAsync"/> call this after the HTTP call; tests call it directly with a
    /// fake connection.
    /// </summary>
    public void Attach(Uri baseUri, SetupResponse response, IReadOnlyList<MappedSchema> offered)
    {
        ValidateOffer(offered);

        var socketUri = response.SocketUri(baseUri)
            ?? throw new SetupException("The service returned no address to connect to.");

        _offered = offered;
        JoinCode = response.JoinCode;
        JoinUrl = response.JoinUrl;

        var connection = _connect(socketUri);
        connection.SnapshotReceived += OnSnapshot;
        connection.StateChanged += OnStateChanged;
        connection.PlayerChanged += OnPlayerChanged;
        connection.PlayerLeft += OnPlayerLeft;
        connection.InputReceived += OnInput;
        connection.MotionReceived += OnMotion;
        connection.ErrorReceived += OnError;
        connection.StatusChanged += status => ConnectionChanged?.Invoke(status);
        connection.ErrorReceived += (_, message) => Notice?.Invoke(message);
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
        var layouts = offered.Count(s => !s.Schema.IsPhysicalGamepad);
        if (layouts < 1)
            throw new ArgumentException("A session offers at least one phone layout; a real gamepad is an extra option alongside.", nameof(offered));
        if (layouts > MaxSchemas)
            throw new ArgumentException($"A session offers at most {MaxSchemas} phone layouts.", nameof(offered));

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

    /// <summary>
    /// Ends the round but keeps the session: everyone goes back to the lobby with ready
    /// cleared, free to change name, colour and layout before the next start. Pads stay,
    /// released to neutral, and players keep their slots.
    /// </summary>
    public async Task ReturnToLobbyAsync(CancellationToken ct)
    {
        if (_connection is null) return;
        await _connection.LobbyAsync(ct);
        ReleaseAllPads();
        SetPhase(SessionPhase.Lobby);
    }

    /// <summary>Removes a player as if they had left; the service confirms with player_left.</summary>
    public Task KickAsync(string playerId, CancellationToken ct) =>
        _connection?.KickAsync(playerId, ct) ?? Task.CompletedTask;

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

        if (snapshot.ProtocolVersion != 0 && snapshot.ProtocolVersion != DriverClient.ProtocolVersion)
        {
            Notice?.Invoke(
                $"The service reports protocol version {snapshot.ProtocolVersion}; this app was built for " +
                $"version {DriverClient.ProtocolVersion}. Things may not work until Phonepads is updated.");
        }

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
                    null or "driver_command" => "The session ended.",
                    "driver_lost" => "The service closed the session because this app was away for too long. Claim a new one.",
                    "host_ended" => "The host ended the session from the website.",
                    "inactivity" => "The service closed the session after a day without activity.",
                    _ => $"The session ended ({reason}).",
                });
                break;

            case "waiting_for_players":
                // A round ended (lobby) or the game never left the lobby; no input arrives here.
                ReleaseAllPads();
                SetPhase(SessionPhase.Lobby);
                break;
        }
    }

    private void OnError(string code, string message)
    {
        // A refused start means the phase set optimistically in StartAsync never happened.
        if (code == "cannot_start" && Phase == SessionPhase.Running) SetPhase(SessionPhase.Lobby);

        Notice?.Invoke(code switch
        {
            "cannot_start" => "The game could not start: not everyone is joined, connected and ready.",
            "invalid_state" => "The service refused that: " + message,
            _ => message,
        });
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

            // Switching schema mid-session may mean a different kind of pad, and a late
            // joiner lands straight on the controller with no start to create one.
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

    /// <summary>
    /// The offered entry for a schema id, falling back to the first phone layout. A player on
    /// a layout that was not offered (there is none today) is treated like the first one.
    /// </summary>
    private MappedSchema SchemaFor(string? schemaId) =>
        _offered.FirstOrDefault(s => string.Equals(s.Schema.Id, schemaId, StringComparison.Ordinal))
        ?? _offered.First(s => !s.Schema.IsPhysicalGamepad);

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

        // Scale the motor strength into a short buzz the phone can actually render. A player
        // on a real gamepad feels it in the controller instead; the phone handles that.
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
